import numpy as np, cv2, time
import vo_sim as S

def make_truth(speed, yaw_deg=35, amp=0.20, fwd=0.20):
    def truth(t):
        t = t*speed
        yaw = np.radians(yaw_deg)*np.sin(2*np.pi*t/3.0); pitch = np.radians(8)*np.sin(2*np.pi*t/1.5)
        R = S.Ry(yaw) @ S.Rx(pitch)
        p = np.array([amp*np.sin(2*np.pi*t/3.0), 0.05*np.sin(2*np.pi*t/2.0), fwd*(1-np.cos(2*np.pi*t/6.0))*0.5*3])
        return R, p
    return truth

def run(speed, seconds, use_imu, label, fps=30, imu_noise=0.3, bias=0.15, blur=False):
    S.truth = make_truth(speed)
    rng = np.random.default_rng(0)
    vo = S.StereoVO(S.K, S.B); et=[]; er=[]; lost=0; tv=[]
    n = int(seconds*fps); Rprev,_ = S.truth(0); maxrate=0
    for i in range(n+1):
        R, p = S.truth(i/fps)
        L = S.render(R, p); Rr = S.render(R, p + R @ np.array([S.B,0,0]))
        if blur:  # flou de bouger grossier proportionnel à la vitesse angulaire
            k = 1 + 2*int(min(4, np.degrees(np.linalg.norm(cv2.Rodrigues(Rprev.T@R)[0]))/2)); 
            if k>1: L = cv2.blur(L,(k,1)); Rr = cv2.blur(Rr,(k,1))
        dR = None
        if i>0:
            true = Rprev.T @ R
            maxrate = max(maxrate, np.degrees(np.linalg.norm(cv2.Rodrigues(true)[0]))*fps)
            if use_imu:
                pert = cv2.Rodrigues(rng.normal(0, np.radians(imu_noise), 3) + np.radians(bias)*np.array([1,-1,.5]))[0]
                dR = true @ pert
        t0=time.perf_counter(); vo.process(L, Rr, dR, 1/fps); tv.append((time.perf_counter()-t0)*1000)
        Rprev = R
        if vo.state=="LOST": lost+=1
        if i>0:
            et.append(np.linalg.norm(vo.t-p)*1000)
            er.append(np.degrees(np.linalg.norm(cv2.Rodrigues(vo.R.T@R)[0])))
    print(f"{label:34s} pic rotation {maxrate:5.0f}°/s | perdus {lost:2d}/{n} | pos: moy {np.mean(et):6.1f} fin {et[-1]:6.1f} max {np.max(et):6.1f} mm | rot: moy {np.mean(er):.2f}° max {np.max(er):.2f}° | vo {np.median(tv):.0f} ms/img (python)")


import sys
if __name__=="__main__":
    for c in sys.argv[1]:
        {"a":lambda: run(1.0,1.3,True ,"normal, avec prior IMU"),
         "b":lambda: run(3.0,1.3,True ,"3x plus rapide, avec prior IMU"),
         "c":lambda: run(3.0,1.3,False,"3x plus rapide, SANS IMU"),
         "d":lambda: run(5.0,1.3,True ,"5x plus rapide, avec prior IMU"),
         "e":lambda: run(5.0,1.3,False,"5x plus rapide, SANS IMU"),
         "f":lambda: run(3.0,1.3,True ,"3x rapide + flou, avec IMU",blur=True),
         "g":lambda: run(3.0,1.3,False,"3x rapide + flou, SANS IMU",blur=True),
         "h":lambda: run(1.0,12,True,"trajectoire 12 s a 10 fps (derive)",fps=10)}[c]()
