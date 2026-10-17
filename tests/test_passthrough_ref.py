"""Valide la géométrie du passthrough (même calcul que le shader) contre OpenCV et numpy.
Usage : python tests/test_passthrough_ref.py calibration/calib   (nécessite numpy et opencv-python)"""
import numpy as np, cv2, math
from passthrough_ref import *
import sys
PREFIX = sys.argv[1] if len(sys.argv) > 1 else "calibration/calib"   # ex. calibration/calib -> calib_left.yaml, calib_right.yaml, calib_pose.yaml
def rd(n):
    r=int(n.getNode("rows").real()); c=int(n.getNode("cols").real()); d=n.getNode("data")
    return np.array([d.at(i).real() for i in range(d.size())]).reshape(r,c)
def load(side):
    fs=cv2.FileStorage(f"{PREFIX}_{side}.yaml",cv2.FILE_STORAGE_READ)
    return rd(fs.getNode("camera_matrix")), rd(fs.getNode("distortion_coefficients"))
K1,D1=load("left"); K2,D2=load("right")
fs=cv2.FileStorage(f"{PREFIX}_pose.yaml",cv2.FILE_STORAGE_READ); R=rd(fs.getNode("rotation_matrix")); T=rd(fs.getNode("translation_matrix"))
T=T*(0.060/np.linalg.norm(T))               # base corrigée à 60 mm, comme dans le programme
size=(1600,1200)
R1,R2,P1,P2,Q,_,_=cv2.stereoRectify(K1,D1,K2,D2,size,R,T,flags=cv2.CALIB_ZERO_DISPARITY,alpha=0,newImageSize=size)
cam=CamModel(K1,D1,R1,size,P1[0,0],P1[0,2],P1[1,2])

# ---- T1 : projection caméra == cv2.initUndistortRectifyMap (calibration réelle, 1600x1200 puis 800x600)
for (W,H) in [(1600,1200),(800,600)]:
    sx=W/1600
    Ks=K1.copy(); Ks[0,0]*=sx;Ks[1,1]*=sx;Ks[0,2]=(K1[0,2]+.5)*sx-.5;Ks[1,2]=(K1[1,2]+.5)*sx-.5
    Ps=P1.copy(); Ps[0,0]*=sx;Ps[1,1]*=sx;Ps[0,2]=(P1[0,2]+.5)*sx-.5;Ps[1,2]=(P1[1,2]+.5)*sx-.5
    mx,my=cv2.initUndistortRectifyMap(Ks,D1,R1,Ps,(W,H),cv2.CV_32FC1)
    rng=np.random.default_rng(0)
    us=rng.integers(0,W,4000); vs=rng.integers(0,H,4000)
    # ray rectifié -> raw -> pixel (même chaîne que le shader, SANS les étapes d'affichage)
    ray=np.stack([(us-Ps[0,2])/Ps[0,0],(vs-Ps[1,2])/Ps[1,1],np.ones_like(us,float)],-1)
    raw=ray@R1                      # = R1^T * ray
    x=raw[:,0]/raw[:,2]; y=raw[:,1]/raw[:,2]; r2=x*x+y*y; r4=r2*r2; r6=r4*r2
    k1,k2,p1,p2,k3,k4,k5,k6=D1[0,:8]
    rad=(1+k1*r2+k2*r4+k3*r6)/(1+k4*r2+k5*r4+k6*r6)
    xd=x*rad+2*p1*x*y+p2*(r2+2*x*x); yd=y*rad+p1*(r2+2*y*y)+2*p2*x*y
    u=Ks[0,0]*xd+Ks[0,2]; v=Ks[1,1]*yd+Ks[1,2]
    err=np.hypot(u-mx[vs,us], v-my[vs,us])
    print(f"T1 {W}x{H}: écart max vs OpenCV = {err.max():.2e} px")
    assert err.max()<1e-2

# ---- T2 : cas identité (sténopé sans distorsion) : l'écran = recadrage carré central de la source
def synth(w,h):
    yy,xx=np.mgrid[0:h,0:w]; return np.dstack([(xx*255//(w-1)),(yy*255//(h-1)),((xx+yy)%256)]).astype(np.uint8)
def display(S, U, N, tex_w, tex_h):
    j,i=np.mgrid[0:N,0:N]; e=np.stack([(i+.5)/N,(j+.5)/N],-1)
    uv,valid,suv,src=shade(e,U,tex_w,tex_h)
    mapx=(uv[...,0]*tex_w-0.5).astype(np.float32); mapy=((1-uv[...,1])*tex_h-0.5).astype(np.float32)
    img=cv2.remap(S,mapx,mapy,cv2.INTER_NEAREST)
    img[~valid]=0
    return img[::-1]          # j=0 est le BAS de l'écran
W,H=1600,1200; S=synth(W,H)
cm=pinhole_model(W,H,90.0)
fov=2*math.degrees(math.atan(600/cm.K[0,0]))           # recadrage 1200x1200 central
U0=assemble(EyeParams(fov_deg=fov),cm,W,H)
img=display(S,U0,1200,W,H)
exp=S[:,200:1400]
print("T2 identité : écart max =",int(np.abs(img.astype(int)-exp.astype(int)).max()))
assert np.abs(img.astype(int)-exp.astype(int)).max()<=2

# ---- T3 : miroir et rotations contre numpy (source NON carrée 1600x1200 -> corrigée 1200x1600 pour 90/270)
def expected(S, rot, mx, my):
    M=S
    if mx: M=M[:,::-1]
    if my: M=M[::-1,:]
    return np.rot90(M, k=-rot)       # k=-1 : 90° horaire
for rot in range(4):
    for mx in (False,True):
        for my in (False,True):
            Wc,Hc=(W,H) if rot%2==0 else (H,W)
            cm2=pinhole_model(Wc,Hc,90.0)
            Nn=min(Wc,Hc)
            fovn=2*math.degrees(math.atan((Nn/2)/cm2.K[0,0]))
            Uu=assemble(EyeParams(fov_deg=fovn,rot=rot,mirror_x=mx,mirror_y=my),cm2,W,H)
            img=display(S,Uu,Nn,W,H)
            E=expected(S,rot,mx,my); x0=(Wc-Nn)//2; y0=(Hc-Nn)//2
            Ec=E[y0:y0+Nn,x0:x0+Nn]
            d=np.abs(img.astype(int)-Ec.astype(int)).max()
            assert d<=2,(rot,mx,my,d)
print("T3 rotations 0/90/180/270 x miroirs X/Y (source non carrée) : OK contre numpy")

# ---- T4 : décalage (IPD) : pan>0 déplace le contenu vers la DROITE de pan*N/2 pixels
N=1200
Ua=assemble(EyeParams(fov_deg=fov),cm,W,H); Ub=assemble(EyeParams(fov_deg=fov,pan=(0.1,0)),cm,W,H)
a=display(S,Ua,N,W,H); b=display(S,Ub,N,W,H)
shift=0.1*N/2
print("T4 pan: pixel (x=600) de 'b' == pixel (x=600-%d) de 'a' :"%shift, bool(np.abs(b[:,600].astype(int)-a[:,600-int(shift)].astype(int)).max()<=2))
assert np.abs(b[:,700].astype(int)-a[:,700-int(shift)].astype(int)).max()<=2

# ---- T5 : centrage sur l'image avec TA calibration : l'axe de vue rectifié passe par le centre de l'image
Ucal=assemble(EyeParams(fov_deg=90),cam,W,H)
ctr=shade(np.array([0.5,0.5]),Ucal,W,H)
print("T5 axe de vue -> rayon (x,y,z) =",np.round(Ucal['view_axis'],4)," ; pixel source du centre d'écran =",np.round(ctr[3],1))
rect_c=np.array([(W-1)/2,(H-1)/2])
ray=np.array([(rect_c[0]-(P1[0,2]+.5)+.5)/P1[0,0],(rect_c[1]-(P1[1,2]+.5)+.5)/P1[1,1],1])   # rayon rectifié du centre de l'image
print("   rayon rectifié du centre de l'image                 =",np.round(ray/np.linalg.norm(ray),4))
assert np.allclose(Ucal['view_axis'],ray/np.linalg.norm(ray),atol=2e-3)

# ---- T6 : distorsion des lentilles : k1>0 éloigne l'échantillonnage du centre (bords plus "étirés")
Uk=assemble(EyeParams(fov_deg=fov,k=(0.2,0,0)),cm,W,H)
e=np.array([[0.95,0.5],[0.5,0.5]]); a1=shade(e,U0,W,H)[3]; a2=shade(e,Uk,W,H)[3]
print("T6 k1=0.2 : bord x_source %.1f -> %.1f ; centre inchangé %.1f -> %.1f"%(a1[0,0],a2[0,0],a1[1,0],a2[1,0]))
assert a2[0,0]>a1[0,0] and abs(a2[1,0]-a1[1,0])<1e-6
print("TOUS LES TESTS DE REFERENCE OK")
