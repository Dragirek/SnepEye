"""Référence du passthrough : exactement le calcul fait par le shader HLSL (Ar6dofComposite.shader),
et l'assemblage des paramètres fait par le C# (Ar6dofPassthrough.cs). Sert à valider la géométrie.
Repères : écran de l'oeil e=(0..1, 0..1), origine en BAS à gauche, y vers le haut.
Caméra : x droite, y bas, z avant. Pixels : origine en HAUT à gauche, centre du pixel = entier."""
import numpy as np, math

def rot_from_to(a, b):
    a = np.asarray(a, float); b = np.asarray(b, float)
    a = a/np.linalg.norm(a); b = b/np.linalg.norm(b)
    v = np.cross(a, b); c = float(np.dot(a, b)); s = np.linalg.norm(v)
    if s < 1e-12:
        return np.eye(3)
    k = v/s
    K = np.array([[0,-k[2],k[1]],[k[2],0,-k[0]],[-k[1],k[0],0]])
    th = math.atan2(s, c)
    return np.eye(3) + math.sin(th)*K + (1-math.cos(th))*K@K

def rot_yaw_pitch(a):
    """Rotation Ry(lacet)*Rx(tangage) qui amène l'axe z sur le vecteur unitaire a (repère caméra : x droite, y bas, z avant).
    L'axe x de l'image reste horizontal : pas de roulis. Équivalent Unity : Quaternion.Euler(asin(a.y), atan2(a.x, a.z), 0)."""
    psi = math.atan2(a[0], a[2]); phi = -math.asin(max(-1.0, min(1.0, a[1])))
    Ry = np.array([[math.cos(psi),0,math.sin(psi)],[0,1,0],[-math.sin(psi),0,math.cos(psi)]])
    Rx = np.array([[1,0,0],[0,math.cos(phi),-math.sin(phi)],[0,math.sin(phi),math.cos(phi)]])
    return Ry @ Rx

class EyeParams:
    def __init__(self, pan=(0,0), lens_center=(0,0), k=(0,0,0), rot=0, mirror_x=False, mirror_y=False,
                 fov_deg=90.0, center_on_image=False):
        self.pan=pan; self.lens_center=lens_center; self.k=k; self.rot=rot
        self.mirror_x=mirror_x; self.mirror_y=mirror_y; self.fov_deg=fov_deg; self.center_on_image=center_on_image

class CamModel:
    """K (3x3), D (8), R (3x3 rectification), taille de calibration, P rectifiée (f,cx,cy)."""
    def __init__(self, K, D, R, calib_wh, rect_f, rect_cx, rect_cy):
        self.K=np.asarray(K,float).reshape(3,3); self.D=np.zeros(8); d=np.asarray(D,float).ravel(); self.D[:min(8,len(d))]=d[:8]
        self.R=np.asarray(R,float).reshape(3,3); self.calib_w,self.calib_h=calib_wh
        self.rect_f,self.rect_cx,self.rect_cy=rect_f,rect_cx,rect_cy

def pinhole_model(w, h, hfov_deg):
    f = (w/2)/math.tan(math.radians(hfov_deg)/2)
    K = [[f,0,(w-1)/2],[0,f,(h-1)/2],[0,0,1]]
    return CamModel(K, np.zeros(8), np.eye(3), (w,h), f, (w-1)/2, (h-1)/2)

def assemble(eye: EyeParams, cam: CamModel, src_w, src_h, src_x0=0, src_y0=0, dR_cam=None, overscan=1.0, clamp=False):
    """Ce que fait le C# : calcule les uniformes envoyés au shader pour UN oeil."""
    quarter = eye.rot % 4
    Wc, Hc = (src_w, src_h) if quarter % 2 == 0 else (src_h, src_w)   # dimensions de l'image corrigée
    sx, sy = Wc/cam.calib_w, Hc/cam.calib_h
    fx, fy = cam.K[0,0]*sx, cam.K[1,1]*sy
    cx, cy = (cam.K[0,2]+0.5)*sx-0.5, (cam.K[1,2]+0.5)*sy-0.5
    fP = cam.rect_f*sx; cxP=(cam.rect_cx+0.5)*sx-0.5; cyP=(cam.rect_cy+0.5)*sy-0.5
    if eye.center_on_image:
        tx = ((Wc-1)/2 - cxP)/fP; ty = ((Hc-1)/2 - cyP)/fP
    else:
        tx = ty = 0.0
    a = np.array([tx, ty, 1.0]); a = a/np.linalg.norm(a)
    Rv = rot_yaw_pitch(a)           # lacet puis tangage, SANS roulis (la rotation minimale ajoutait ~2° de roulis)
    # dR_cam : rotation de la caméra entre l'instant de capture et maintenant (reprojection), repère caméra
    M = cam.R.T @ (np.eye(3) if dR_cam is None else dR_cam) @ Rv
    return dict(pan=eye.pan, lc=eye.lens_center, k=eye.k, tanH=math.tan(math.radians(eye.fov_deg)/2),
                f=(fx,fy,cx,cy), D=cam.D.copy(), M=M, Wc=Wc, Hc=Hc, mx=eye.mirror_x, my=eye.mirror_y,
                rot=quarter, x0=src_x0, y0=src_y0, view_axis=a/np.linalg.norm(a), overscan=overscan, clamp=clamp)

def shade(e, U, tex_w, tex_h):
    """Équivalent du fragment shader pour un tableau e (...,2). Retourne (uv_texture (...,2), valide, uv_scène (...,2))."""
    e = np.asarray(e, float)
    p = e*2-1
    lc = np.array(U['lc']); pan = np.array(U['pan'])
    d = p - lc; r2 = (d**2).sum(-1)
    k1,k2,k3 = U['k']
    s = 1 + k1*r2 + k2*r2**2 + k3*r2**3
    q = lc + d*s[...,None] - pan
    suv = q/U['overscan']*0.5+0.5       # la scène est rendue avec une marge (overscan) : sinon le décalage IPD la rogne
    rv = np.stack([q[...,0]*U['tanH'], -q[...,1]*U['tanH'], np.ones_like(r2)], -1)
    rr = rv @ U['M'].T
    ok = rr[...,2] > 1e-4
    z = np.where(ok, rr[...,2], 1.0)
    x = rr[...,0]/z; y = rr[...,1]/z
    rc2 = x*x+y*y; r4=rc2*rc2; r6=r4*rc2
    k1c,k2c,p1,p2,k3c,k4c,k5c,k6c = U['D']
    rad = (1+k1c*rc2+k2c*r4+k3c*r6)/(1+k4c*rc2+k5c*r4+k6c*r6)
    xd = x*rad + 2*p1*x*y + p2*(rc2+2*x*x)
    yd = y*rad + p1*(rc2+2*y*y) + 2*p2*x*y
    fx,fy,cx,cy = U['f']
    u = fx*xd+cx; v = fy*yd+cy                       # pixels de l'image corrigée
    Wc,Hc,rot = U['Wc'],U['Hc'],U['rot']
    Ws,Hs = (Wc,Hc) if rot%2==0 else (Hc,Wc)         # dimensions de la source
    if rot==0:   um,vm = u, v
    elif rot==1: um,vm = v, Wc-1-u                   # 90° horaire : corrigée = rot(miroir(source))
    elif rot==2: um,vm = Ws-1-u, Hs-1-v
    else:        um,vm = Hc-1-v, u
    us = np.where(U['mx'], Ws-1-um, um)
    vs = np.where(U['my'], Hs-1-vm, vm)
    inside = (us>=-0.5)&(us<=Ws-0.5)&(vs>=-0.5)&(vs<=Hs-0.5)
    valid = ok & (inside | U['clamp'])
    usc = np.clip(us,0,Ws-1); vsc = np.clip(vs,0,Hs-1)
    ut = (U['x0']+usc+0.5)/tex_w; vt = 1-(U['y0']+vsc+0.5)/tex_h
    return np.stack([ut,vt],-1), valid, suv, np.stack([us,vs],-1)


# --- Reprojection : algèbre utilisée par le C# (Ar6dofPassthrough.cs) ---------------------------------
def quat_to_mat(q):
    w,x,y,z = q
    return np.array([[1-2*(y*y+z*z),2*(x*y-w*z),2*(x*z+w*y)],
                     [2*(x*y+w*z),1-2*(x*x+z*z),2*(y*z-w*x)],
                     [2*(x*z-w*y),2*(y*z+w*x),1-2*(x*x+y*y)]])

R_CB = np.array([[0,-1,0],[0,0,-1],[1,0,0]], float)    # repère casque (X avant, Y gauche, Z haut) -> caméra (x droite, y bas, z avant)

def delta_cam(q_cap_G, q_now_G):
    """Rotation de la caméra entre la capture et maintenant, à partir des orientations casque->monde (repère G)."""
    Rb_cap, Rb_now = quat_to_mat(q_cap_G), quat_to_mat(q_now_G)
    return R_CB @ (Rb_cap.T @ Rb_now) @ R_CB.T
