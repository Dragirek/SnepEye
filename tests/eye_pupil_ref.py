"""Référence Python (OpenCV) du détecteur de pupille / reflet IR de src/eye_pupil.h, avec images d'yeux synthétiques.
Sert à régler et vérifier l'algorithme sans matériel : python3 tests/eye_pupil_ref.py"""
import math, sys, time
import cv2
import numpy as np

CFG = dict(margin=28, dark_pct=0.002, min_area=0.0025, max_area=0.12, min_solidity=0.80, min_aspect=0.50, glint_min=200)


class Detector:
    def __init__(self, cfg=CFG):
        self.c = cfg
        self.have = False
        self.cx = self.cy = 0.0
        self.r = 0.0
        self.misses = 0
        self.kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
        self.kclose = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))

    def _search(self, gray, x0, y0, x1, y1, tracking, R):
        """Cherche la pupille dans le rectangle donné. Retourne (hull, aire, touche_le_bord) ou None."""
        c = self.c
        H, W = gray.shape
        img_area = float(W * H)
        sub = gray[y0:y1, x0:x1]
        blur = cv2.blur(sub, (5, 5))
        # seuil adapté à l'image : un peu au-dessus du gris le plus sombre (la pupille infrarouge est le point le plus sombre)
        hist = cv2.calcHist([blur], [0], None, [256], [0, 256]).ravel()
        cum = np.cumsum(hist)
        dark = int(np.searchsorted(cum, max(8.0, c['dark_pct'] * img_area)))
        T = min(150, dark + c['margin'])
        _, binim = cv2.threshold(blur, T, 255, cv2.THRESH_BINARY_INV)
        binim = cv2.morphologyEx(binim, cv2.MORPH_CLOSE, self.kclose)  # rebouche les trous laissés par les reflets infrarouges (très clairs) dans la pupille
        binim = cv2.morphologyEx(binim, cv2.MORPH_OPEN, self.kernel)   # enlève les cils et les bruits fins
        contours, _ = cv2.findContours(binim, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        best = None; bestScore = 0.0
        for cn in contours:
            area = cv2.contourArea(cn)
            if area < c['min_area'] * img_area or area > c['max_area'] * img_area or len(cn) < 5:
                continue
            hull = cv2.convexHull(cn)
            ha = cv2.contourArea(hull)
            if ha <= 0:
                continue
            sol = area / ha
            if sol < c['min_solidity']:
                continue
            (ex, ey), (ea, eb), _ang = cv2.fitEllipse(hull)
            aspect = min(ea, eb) / max(ea, eb, 1e-6)
            if aspect < c['min_aspect']:
                continue
            score = sol * aspect * math.sqrt(area)
            if tracking:
                m = cv2.moments(hull)
                if m['m00'] > 0:
                    d = math.hypot(m['m10'] / m['m00'] + x0 - self.cx, m['m01'] / m['m00'] + y0 - self.cy)
                    score *= 1.0 - 0.5 * min(1.0, d / R)
            if score > bestScore:
                bx, by, bw, bh = cv2.boundingRect(hull)
                touches = bx <= 0 or by <= 0 or bx + bw >= (x1 - x0) or by + bh >= (y1 - y0)
                bestScore = score; best = (hull, ha, touches)
        return best

    def detect(self, gray):
        c = self.c
        H, W = gray.shape
        full = (0, 0, W, H)
        best = None; x0 = y0 = 0
        if self.have and self.misses < 4:
            # 1) on cherche d'abord autour de la dernière pupille (rapide)
            R = max(40.0, 2.6 * self.r)
            x0 = int(max(0, math.floor(self.cx - R))); x1 = int(min(W, math.ceil(self.cx + R)))
            y0 = int(max(0, math.floor(self.cy - R))); y1 = int(min(H, math.ceil(self.cy + R)))
            best = self._search(gray, x0, y0, x1, y1, True, R)
            if best is not None and best[2] and (x1 - x0 < W or y1 - y0 < H):
                best = None   # tache coupée par le bord de la zone : on refait sur toute l'image
        if best is None:
            x0, y0 = 0, 0
            best = self._search(gray, 0, 0, W, H, False, float(max(W, H)))
        if best is None:
            self.misses += 1
            return None
        hull, ha, _t = best
        m = cv2.moments(hull)
        px = m['m10'] / m['m00'] + x0
        py = m['m01'] / m['m00'] + y0
        rad = math.sqrt(ha / math.pi)
        self.have = True; self.cx, self.cy, self.r = px, py, rad; self.misses = 0
        res = dict(px=px, py=py, r=rad, area=ha, glint=False, gx=0.0, gy=0.0)
        # reflet infrarouge (petit point très clair près de la pupille)
        gw = int(1.7 * rad + 6)
        gx0 = int(max(0, px - gw)); gx1 = int(min(W, px + gw + 1)); gy0 = int(max(0, py - gw)); gy1 = int(min(H, py + gw + 1))
        win = gray[gy0:gy1, gx0:gx1]
        mx = int(win.max())
        if mx >= c['glint_min']:
            thr = max(c['glint_min'] - 1, mx - 20)
            _, gb = cv2.threshold(win, thr, 255, cv2.THRESH_BINARY)
            n, lab, stats, cents = cv2.connectedComponentsWithStats(gb, connectivity=8)
            bd = 1e9; bg = None
            for i in range(1, n):
                a = stats[i, cv2.CC_STAT_AREA]
                if a < 1 or a > 90:
                    continue
                gcx = cents[i][0] + gx0; gcy = cents[i][1] + gy0
                d = math.hypot(gcx - px, gcy - py)
                if d < bd and d <= 1.5 * rad + 4:
                    bd = d; bg = (i, gcx, gcy)
            if bg is not None:
                i, gcx, gcy = bg
                # centre pondéré par la luminosité (précision sous le pixel)
                xs = max(0, int(gcx) - 3); xe = min(W, int(gcx) + 4); ys = max(0, int(gcy) - 3); ye = min(H, int(gcy) + 4)
                p = gray[ys:ye, xs:xe].astype(np.float32) - (thr - 25)
                p[p < 0] = 0
                s = float(p.sum())
                if s > 0:
                    yy, xx = np.mgrid[ys:ye, xs:xe]
                    gcx = float((p * xx).sum() / s); gcy = float((p * yy).sum() / s)
                res['glint'] = True; res['gx'] = gcx; res['gy'] = gcy
        return res


def synth_eye(ix, iy, rng, open_frac=1.0, pupil_r=16, glint=True, noise=3.0, lashes=True, dark=18):
    W, H = 320, 240
    img = np.full((H, W), 165, np.float32)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    img += (xx - W / 2) * 0.05 + (yy - H / 2) * 0.04          # éclairage inégal
    # sourcil (sombre, large) et ombre
    brow = ((xx - 150) / 110.0) ** 2 + ((yy - 28) / 12.0) ** 2 < 1
    img[brow] = 60
    # ouverture de l'oeil
    ocx, ocy, oa, ob = 160, 125, 100, 48 * open_frac
    eye = ((xx - ocx) / oa) ** 2 + ((yy - ocy) / max(ob, 1.0)) ** 2 < 1
    img[eye] = 190                                              # sclérotique
    iris = ((xx - ix) ** 2 + (yy - iy) ** 2 < 44 ** 2) & eye
    img[iris] = 95
    pup = ((xx - ix) ** 2 + (yy - iy) ** 2 < pupil_r ** 2) & eye
    img[pup] = dark
    # paupière supérieure sombre + cils
    ring = (((xx - ocx) / (oa + 6)) ** 2 + ((yy - ocy) / (max(ob, 1.0) + 7)) ** 2 < 1) & ~eye
    img[ring & (yy < ocy)] = 50
    if lashes:
        for k in range(24):
            x = int(ocx - oa + k * (2 * oa / 23.0))
            ytop = int(ocy - ob * math.sqrt(max(0.0, 1 - ((x - ocx) / oa) ** 2)))
            cv2.line(img, (x, ytop), (x + 3, ytop - 12), 35, 2)
    if glint and open_frac > 0.5:
        for dx in (-7, 6):
            gx = ix + dx * 0.9 + (ix - 160) * 0.05; gy = iy + 5 + (iy - 125) * 0.05
            if eye[int(round(gy)), int(round(gx))]:
                cv2.circle(img, (int(round(gx)), int(round(gy))), 2, 255, -1)
    img = cv2.GaussianBlur(img, (0, 0), 1.0)
    img += rng.normal(0, noise, img.shape).astype(np.float32)
    return np.clip(img, 0, 255).astype(np.uint8)


def run():
    rng = np.random.default_rng(3)
    det = Detector()
    errs, gerrs, miss, nglint, tot = [], [], 0, 0, 0
    t_all = 0.0
    for i in range(400):
        ix = rng.uniform(125, 195); iy = rng.uniform(105, 145)
        img = synth_eye(ix, iy, rng, pupil_r=rng.uniform(12, 20))
        t0 = time.perf_counter()
        r = det.detect(img)
        t_all += time.perf_counter() - t0
        tot += 1
        if r is None:
            miss += 1; continue
        errs.append(math.hypot(r['px'] - ix, r['py'] - iy))
        if r['glint']:
            nglint += 1
    errs = np.array(errs)
    print("suivi continu  : trouve %d/%d, erreur pupille moyenne %.2f px, 95e centile %.2f px, pire %.2f px" % (tot - miss, tot, errs.mean(), np.percentile(errs, 95), errs.max()))
    print("reflet trouve  : %d/%d" % (nglint, tot - miss))
    print("temps moyen    : %.2f ms par image (Python, 1 oeil)" % (1000 * t_all / tot))
    assert miss <= 4 and errs.mean() < 0.7 and np.percentile(errs, 95) < 1.5, "pupille : precision insuffisante"
    assert nglint >= 0.9 * (tot - miss), "reflet : trop peu detecte"

    # sans suivi (image seule, détecteur neuf à chaque fois) : recherche sur toute l'image
    ok = 0
    for i in range(60):
        ix = rng.uniform(125, 195); iy = rng.uniform(105, 145)
        r = Detector().detect(synth_eye(ix, iy, rng))
        if r is not None and math.hypot(r['px'] - ix, r['py'] - iy) < 2.0:
            ok += 1
    print("image isolee   : %d/60 bien placees (< 2 px)" % ok)
    assert ok >= 57

    # précision du reflet
    det = Detector(); ge = []
    for i in range(100):
        ix = rng.uniform(130, 190); iy = rng.uniform(108, 142)
        img = synth_eye(ix, iy, rng, noise=2.0)
        r = det.detect(img)
        if r and r['glint']:
            # vérité : le reflet de droite est à ix + 6*0.9 + ... ; on teste seulement qu'il est près de la pupille
            ge.append(math.hypot(r['gx'] - r['px'], r['gy'] - r['py']))
    print("reflet : distance moyenne a la pupille %.1f px (attendu < 14)" % np.mean(ge))
    assert np.mean(ge) < 14

    # clignement : plus la paupière se ferme, plus l'aire visible de la pupille baisse (ou elle disparait) : c'est ce que BlinkState mesure
    det = Detector()
    r_open = det.detect(synth_eye(160, 125, rng))
    assert r_open is not None
    ref = r_open['area']
    seen = []
    for f in (0.6, 0.4, 0.25, 0.12, 0.05):
        r = det.detect(synth_eye(160, 125, rng, open_frac=f))
        seen.append(0.0 if r is None else r['area'] / ref)
    print("clignement : aire visible relative pour une paupiere ouverte a 60/40/25/12/5 %% : %s" % ["%.2f" % s for s in seen])
    assert seen[-1] < 0.35 and seen[-2] < 0.5 and seen == sorted(seen, reverse=True) or seen[-1] == 0.0
    r_back = None
    for _ in range(4):
        r_back = det.detect(synth_eye(160, 125, rng))
        if r_back is not None:
            break
    assert r_back is not None and math.hypot(r_back['px'] - 160, r_back['py'] - 125) < 1.5
    print("reouverture : pupille retrouvee")

    # robustesse : bruit fort, pupille plus claire (mauvais contraste), sans cils
    for noise, dark, lashes in ((8.0, 18, True), (3.0, 55, True), (3.0, 18, False)):
        det = Detector(); e = []; m = 0
        for i in range(80):
            ix = rng.uniform(125, 195); iy = rng.uniform(105, 145)
            r = det.detect(synth_eye(ix, iy, rng, noise=noise, dark=dark, lashes=lashes))
            if r is None: m += 1
            else: e.append(math.hypot(r['px'] - ix, r['py'] - iy))
        print("bruit %.0f, pupille %d, cils %s : perdue %d/80, erreur moyenne %.2f px" % (noise, dark, lashes, m, np.mean(e) if e else -1))
        assert m <= 8 and np.mean(e) < 1.5
    print("TEST PUPILLE (reference Python) OK")


if __name__ == "__main__":
    run()
