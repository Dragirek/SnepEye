"""Prototype du tracker stéréo + IMU, validé sur une scène simulée (pièce texturée).
Le monde = repère de la 1re caméra (convention OpenCV : x droite, y bas, z avant)."""
import numpy as np, cv2, sys, time

W, H = 800, 600
F, CX, CY, B = 315.45, 510.6, 231.5, 0.0509   # calibration réelle, échelle 0.5
K = np.array([[F,0,CX],[0,F,CY],[0,0,1.]])

# ------------------------------------------------------------------ simulation
def make_tex(seed, n=1024):
    r = np.random.default_rng(seed)
    t = (r.random((n, n))*255).astype(np.float32)
    t = cv2.GaussianBlur(t, (0,0), 2.0)
    t = cv2.normalize(t, None, 30, 220, cv2.NORM_MINMAX)
    for _ in range(120):
        x, y = int(r.integers(0, n)), int(r.integers(0, n))
        c = int(r.integers(0, 255))
        if r.random() < .5: cv2.rectangle(t, (x,y), (x+int(r.integers(10,60)), y+int(r.integers(10,60))), c, -1)
        else: cv2.circle(t, (x,y), int(r.integers(6,30)), c, -1)
    return cv2.GaussianBlur(t, (0,0), 1.0)

# (axe, valeur, (axe_u, min,max), (axe_v, min,max))
PLANES = [
    (2, 4.0, (0,-3,3), (1,-1.5,1.5)),   # mur du fond
    (0,-3.0, (2,-1,4), (1,-1.5,1.5)),   # mur gauche
    (0, 3.0, (2,-1,4), (1,-1.5,1.5)),   # mur droit
    (1, 1.5, (0,-3,3), (2,-1,4)),       # sol
    (1,-1.5, (0,-3,3), (2,-1,4)),       # plafond
    (2, 2.0, (0,-.7,.7), (1,.2,1.5)),   # objet posé au sol, plus près
]
TEX = [make_tex(10+i) for i in range(len(PLANES))]
_u, _v = np.meshgrid(np.arange(W), np.arange(H))
RAYS_C = np.stack([(_u-CX)/F, (_v-CY)/F, np.ones_like(_u, float)], -1)   # H,W,3

def render(R_wc, c):
    d = RAYS_C @ R_wc.T
    ts = []
    for (a, val, (ua,u0,u1), (va,v0,v1)) in PLANES:
        with np.errstate(divide='ignore', invalid='ignore'):
            t = (val - c[a]) / d[...,a]
        hit_u = c[ua] + t*d[...,ua]; hit_v = c[va] + t*d[...,va]
        ok = (t > 0.05) & (hit_u>=u0)&(hit_u<=u1)&(hit_v>=v0)&(hit_v<=v1)
        ts.append(np.where(ok, t, np.inf))
    ts = np.stack(ts); idx = ts.argmin(0); tmin = ts.min(0)
    img = np.full((H, W), 90, np.float32)
    for k, (a, val, (ua,u0,u1), (va,v0,v1)) in enumerate(PLANES):
        sel = (idx==k) & np.isfinite(tmin)
        if not sel.any(): continue
        hu = c[ua] + tmin*d[...,ua]; hv = c[va] + tmin*d[...,va]
        mu = ((hu-u0)/(u1-u0)*1023).astype(np.float32); mv = ((hv-v0)/(v1-v0)*1023).astype(np.float32)
        s = cv2.remap(TEX[k], mu, mv, cv2.INTER_LINEAR)
        img[sel] = s[sel]
    img += np.random.default_rng(0).normal(0, 2.0, img.shape).astype(np.float32)
    return np.clip(img, 0, 255).astype(np.uint8)

def Rx(a): c,s=np.cos(a),np.sin(a); return np.array([[1,0,0],[0,c,-s],[0,s,c]])
def Ry(a): c,s=np.cos(a),np.sin(a); return np.array([[c,0,s],[0,1,0],[-s,0,c]])
def Rz(a): c,s=np.cos(a),np.sin(a); return np.array([[c,-s,0],[s,c,0],[0,0,1]])

# ------------------------------------------------------------------ tracker
class StereoVO:
    def __init__(self, K, B, max_pts=350, depth_max=7.0):
        self.K, self.B, self.f = K, B, K[0,0]
        self.max_pts, self.depth_max = max_pts, depth_max
        self.R = np.eye(3); self.t = np.zeros(3)       # T_wc
        self.v = np.zeros(3)
        self.prev = None; self.p2 = np.zeros((0,1,2),np.float32); self.p3 = np.zeros((0,3))
        self.n_inl = 0; self.state = "INIT"

    def _stereo_triangulate(self, L, R, pts):
        """pts (N,1,2) dans L -> (kept_idx, points 3D repère caméra gauche)"""
        if len(pts) == 0: return np.zeros(0,int), np.zeros((0,3))
        lk = dict(winSize=(21,21), maxLevel=3, criteria=(cv2.TERM_CRITERIA_EPS|cv2.TERM_CRITERIA_COUNT, 30, 0.01))
        pr, st, _ = cv2.calcOpticalFlowPyrLK(L, R, pts, None, **lk)
        pb, st2, _ = cv2.calcOpticalFlowPyrLK(R, L, pr, None, **lk)
        d = pts[:,0,0] - pr[:,0,0]; dy = np.abs(pts[:,0,1]-pr[:,0,1])
        back = np.linalg.norm(pb[:,0]-pts[:,0], axis=1)
        z = self.f*self.B/np.maximum(d, 1e-6)
        ok = (st[:,0]==1)&(st2[:,0]==1)&(dy<1.0)&(back<1.0)&(d>0.8)&(z<self.depth_max)&(z>0.25)
        idx = np.where(ok)[0]
        X = np.stack([(pts[idx,0,0]-CX)*z[idx]/self.f, (pts[idx,0,1]-CY)*z[idx]/self.f, z[idx]], 1)
        return idx, X

    def _add_points(self, L, R):
        mask = np.full(L.shape, 255, np.uint8)
        for p in self.p2[:,0]: cv2.circle(mask, (int(p[0]),int(p[1])), 14, 0, -1)
        need = self.max_pts - len(self.p2)
        if need <= 10: return
        c = cv2.goodFeaturesToTrack(L, need, 0.01, 10, mask=mask)
        if c is None: return
        idx, X = self._stereo_triangulate(L, R, c.astype(np.float32))
        if len(idx)==0: return
        Xw = X @ self.R.T + self.t
        self.p2 = np.vstack([self.p2, c[idx].astype(np.float32)])
        self.p3 = np.vstack([self.p3, Xw])

    def process(self, L, R, dR_cam=None, dt=1/30):
        """dR_cam : rotation caméra entre l'image précédente et celle-ci (prior IMU), ou None."""
        if self.prev is None:
            self._add_points(L, R); self.prev = L; self.state = "OK"; return
        R_prior = self.R @ (dR_cam if dR_cam is not None else np.eye(3))
        t_prior = self.t + self.v*dt
        # prédiction 2D des points pour guider le KLT (utile en rotation rapide)
        Rcw = R_prior.T; tcw = -Rcw @ t_prior
        pc = self.p3 @ Rcw.T + tcw
        guess = (self.K @ pc.T).T; guess = (guess[:,:2]/np.maximum(guess[:,2:3],1e-6)).astype(np.float32)[:,None,:]
        lk = dict(winSize=(21,21), maxLevel=3, criteria=(cv2.TERM_CRITERIA_EPS|cv2.TERM_CRITERIA_COUNT, 30, 0.01))
        inb = (pc[:,2]>0.1)&(guess[:,0,0]>0)&(guess[:,0,0]<W)&(guess[:,0,1]>0)&(guess[:,0,1]<H)
        guess = np.where(inb[:,None,None], guess, self.p2)
        nxt, st, _ = cv2.calcOpticalFlowPyrLK(self.prev, L, self.p2, guess.copy(), flags=cv2.OPTFLOW_USE_INITIAL_FLOW, **lk)
        bk, st2, _ = cv2.calcOpticalFlowPyrLK(L, self.prev, nxt, None, **lk)
        err = np.linalg.norm(bk[:,0]-self.p2[:,0], axis=1)
        ok = (st[:,0]==1)&(st2[:,0]==1)&(err<1.0)&(nxt[:,0,0]>0)&(nxt[:,0,0]<W)&(nxt[:,0,1]>0)&(nxt[:,0,1]<H)
        self.p2, self.p3 = nxt[ok], self.p3[ok]
        if len(self.p2) < 12:
            self.state = "LOST"; self.prev = L; self.p2 = np.zeros((0,1,2),np.float32); self.p3 = np.zeros((0,3))
            self._add_points(L, R); return
        rvec,_ = cv2.Rodrigues(Rcw); tvec = tcw.reshape(3,1)
        okp, rvec, tvec, inl = cv2.solvePnPRansac(self.p3, self.p2, self.K, None, rvec, tvec, True,
                                                   iterationsCount=120, reprojectionError=2.0, confidence=0.995,
                                                   flags=cv2.SOLVEPNP_ITERATIVE)
        if not okp or inl is None or len(inl) < 12:
            self.state = "LOST"; self.prev = L; return
        inl = inl[:,0]
        rvec, tvec = cv2.solvePnPRefineLM(self.p3[inl], self.p2[inl], self.K, None, rvec, tvec)
        Rcw,_ = cv2.Rodrigues(rvec); Rnew = Rcw.T; tnew = (-Rcw.T @ tvec).ravel()
        v_new = (tnew - self.t)/dt
        self.v = 0.5*self.v + 0.5*v_new
        self.R, self.t = Rnew, tnew
        keep = np.zeros(len(self.p2), bool); keep[inl] = True
        self.p2, self.p3 = self.p2[keep], self.p3[keep]
        self.n_inl = len(inl); self.state = "OK"
        self.prev = L
        if len(self.p2) < self.max_pts*0.8: self._add_points(L, R)

# ------------------------------------------------------------------ test
def truth(t):
    yaw = np.radians(35)*np.sin(2*np.pi*t/3.0); pitch = np.radians(8)*np.sin(2*np.pi*t/1.5)
    R = Ry(yaw) @ Rx(pitch)
    p = np.array([0.20*np.sin(2*np.pi*t/3.0), 0.05*np.sin(2*np.pi*t/2.0), 0.20*t])
    return R, p

def run(seconds=3.0, fps=30, use_imu=True, yaw_gain=1.0, imu_noise_deg=0.3, bias_deg=0.15, seed=0, verbose=True):
    rng = np.random.default_rng(seed)
    vo = StereoVO(K, B); errs_t=[]; errs_r=[]; lost=0
    n = int(seconds*fps); Rprev,_ = truth(0)
    t0=time.time()
    for i in range(n+1):
        t = i/fps
        R, p = truth(t)
        # accélère la rotation si demandé (test rotation rapide)
        L = render(R, p); Rr = render(R, p + R @ np.array([B,0,0]))
        dR = None
        if use_imu and i>0:
            true = Rprev.T @ R
            pert = cv2.Rodrigues(rng.normal(0, np.radians(imu_noise_deg), 3) + np.radians(bias_deg)*np.array([1,-1,.5]))[0]
            dR = true @ pert
        vo.process(L, Rr, dR, 1/fps)
        Rprev = R
        if vo.state=="LOST": lost+=1
        if i>0:
            errs_t.append(np.linalg.norm(vo.t-p)*1000)
            errs_r.append(np.degrees(np.linalg.norm(cv2.Rodrigues(vo.R.T@R)[0])))
    if verbose:
        print(f"  imu={use_imu}: frames={n} perdus={lost}  erreur position: moy={np.mean(errs_t):.1f} mm  fin={errs_t[-1]:.1f} mm  max={np.max(errs_t):.1f} mm | rotation: moy={np.mean(errs_r):.2f}°  fin={errs_r[-1]:.2f}°  max={np.max(errs_r):.2f}°   ({(time.time()-t0)/n*1000:.0f} ms/frame sim+vo)")
    return errs_t, errs_r, lost

if __name__ == "__main__":
    print("Scène simulée, trajectoire 3 s (0,6 m avancée, +-35° de lacet) :")
    run(use_imu=True); run(use_imu=False)
