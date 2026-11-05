#!/usr/bin/env python3
"""Reference of Ar6dofStack.cs (accumulation of several camera frames before the scan or a photo): align by translation (coarse search on a 1/4 image,
fine search + sub-pixel parabola on the full image), reject frames that do not match, then shift-and-add into a grid 's' times finer.
Run: python3 tests/stack_ref.py"""
import numpy as np

def gray(rgb):  # rgb float array h,w,3
    return (rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114)

def box4(g):
    h, w = g.shape
    h4, w4 = h // 4, w // 4
    return g[:h4 * 4, :w4 * 4].reshape(h4, 4, w4, 4).mean(axis=(1, 3))

def sharpness(g):
    h, w = g.shape
    s = g[::2, ::2]
    lap = s[1:-1, 1:-1] * 4 - s[:-2, 1:-1] - s[2:, 1:-1] - s[1:-1, :-2] - s[1:-1, 2:]
    return float((lap * lap).mean())

def ssd_grid(ref, fr, x0, x1, y0, y1, stride, rx0, rx1, ry0, ry1):
    """SSD(dx,dy) = sum (ref(x,y) - fr(x+dx,y+dy))^2 over x in [x0,x1) y in [y0,y1) step stride, for dx in [rx0,rx1], dy in [ry0,ry1]."""
    out = np.zeros((ry1 - ry0 + 1, rx1 - rx0 + 1))
    a = ref[y0:y1:stride, x0:x1:stride]
    for dy in range(ry0, ry1 + 1):
        for dx in range(rx0, rx1 + 1):
            b = fr[y0 + dy:y1 + dy:stride, x0 + dx:x1 + dx:stride]
            d = a - b
            out[dy - ry0, dx - rx0] = float((d * d).mean())
    return out

def parabola(sm, c, p):
    d = sm - 2 * c + p
    if d <= 1e-9: return 0.0
    o = 0.5 * (sm - p) / d
    return max(-0.5, min(0.5, o))

def estimate(ref, fr, R=8):
    h, w = ref.shape
    r4, f4 = box4(ref), box4(fr)
    h4, w4 = r4.shape
    m = R + 2
    S = ssd_grid(r4, f4, m, w4 - m, m, h4 - m, 2, -R, R, -R, R)
    j, i = np.unravel_index(np.argmin(S), S.shape)
    cdx, cdy = (i - R) * 4, (j - R) * 4
    marg = 4 * R + 8
    F = 3
    S2 = ssd_grid(ref, fr, marg, w - marg, marg, h - marg, 3, cdx - F, cdx + F, cdy - F, cdy + F)
    j, i = np.unravel_index(np.argmin(S2), S2.shape)
    best = S2[j, i]
    dx, dy = cdx - F + i, cdy - F + j
    # sub-pixel : SSD on EVERY pixel of the crop around the best integer shift, then a parabola on each axis
    for _ in range(3):   # the integer found on the coarse sampling can be off by one : re-centre while the parabola hits its limit
        S3 = ssd_grid(ref, fr, marg, w - marg, marg, h - marg, 1, dx - 1, dx + 1, dy - 1, dy + 1)
        sx = parabola(S3[1, 0], S3[1, 1], S3[1, 2])
        sy = parabola(S3[0, 1], S3[1, 1], S3[2, 1])
        mx = 1 if sx >= 0.499 else (-1 if sx <= -0.499 else 0)
        my = 1 if sy >= 0.499 else (-1 if sy <= -0.499 else 0)
        if mx == 0 and my == 0: break
        dx += mx; dy += my
    return dx + sx, dy + sy, float(S3[1, 1])

def fuse(frames, s=2):
    """frames: list of float arrays h,w,C. Returns (fused (h*s,w*s,C), info)."""
    gs = [gray(f) if f.shape[2] == 3 else f[..., 0] for f in frames]
    k = int(np.argmax([sharpness(g) for g in gs]))
    est = []
    for i, g in enumerate(gs):
        if i == k: est.append((0.0, 0.0, 0.0)); continue
        est.append(estimate(gs[k], g))
    errs = sorted(e[2] for i, e in enumerate(est) if i != k)
    med = errs[len(errs) // 2] if errs else 0.0
    keep = [i == k or est[i][2] <= max(2.0 * med, med + 4.0) for i in range(len(frames))]
    h, w, C = frames[0].shape
    H, W = h * s, w * s
    acc = np.zeros((H, W, C)); wt = np.zeros((H, W))
    for i, f in enumerate(frames):
        if not keep[i]: continue
        dx, dy, _ = est[i]
        ys, xs = np.mgrid[0:h, 0:w]
        # frame(x,y) = ref(x - dx, y - dy) : position of this pixel in the reference frame
        X = (xs - dx + 0.5) * s - 0.5
        Y = (ys - dy + 0.5) * s - 0.5
        x0 = np.floor(X).astype(int); y0 = np.floor(Y).astype(int)
        fx = X - x0; fy = Y - y0
        for ox, oy, wgt in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
            xx = x0 + ox; yy = y0 + oy
            ok = (xx >= 0) & (xx < W) & (yy >= 0) & (yy < H)
            np.add.at(wt, (yy[ok], xx[ok]), wgt[ok])
            for c in range(C):
                np.add.at(acc[..., c], (yy[ok], xx[ok]), (wgt * f[..., c])[ok])
    # holes (not covered) : bilinear-ish upsample of the reference
    ref = frames[k]
    up = np.repeat(np.repeat(ref, s, axis=0), s, axis=1)
    out = np.where(wt[..., None] > 0.25, acc / np.maximum(wt[..., None], 1e-6), up)
    return out, dict(ref=k, est=est, keep=keep)

# ---------------------------------------------------------------- test
def make_scene(h, w, rng):
    """text-like scene: dark bars and dots on a light page, then slightly blurred"""
    img = np.full((h, w), 200.0)
    for _ in range(60):
        x = rng.integers(10, w - 40); y = rng.integers(10, h - 12)
        ww = rng.integers(6, 36); hh = rng.integers(2, 4)
        img[y:y + hh, x:x + ww] = 40
    from scipy.ndimage import gaussian_filter
    return gaussian_filter(img, 0.8)

def shifted(img, dx, dy):
    from scipy.ndimage import shift
    return shift(img, (dy, dx), order=1, mode='nearest')   # out(x,y) = img(x - dx, y - dy)

if __name__ == '__main__':
    rng = np.random.default_rng(1)
    h, w = 240, 320
    base = make_scene(h, w, rng)
    frames = []; truth = []
    for i in range(8):
        dx, dy = rng.uniform(-20, 20, 2)
        f = shifted(base, dx, dy) * 0.12            # dark image like the camera in a dim room
        f = f + rng.normal(0, 3.0, f.shape)          # noise
        frames.append(np.clip(f, 0, 255)[..., None]); truth.append((dx, dy))
    frames = [np.repeat(f, 3, axis=2) for f in frames]
    out, info = fuse(frames, 2)
    k = info['ref']
    print('ref frame', k, 'kept', sum(info['keep']), '/', len(frames))
    bad = 0
    for i, (e, t) in enumerate(zip(info['est'], truth)):
        # expected relation: frame_i(x) = ref(x - (t_i - t_k)) -> dx_est (frame(x+dx)=ref(x)) = -(t_i - t_k) ... here estimate returns d with fr(x+d)=ref(x) -> d = t_i - t_k
        ex, ey = t[0] - truth[k][0], t[1] - truth[k][1]
        err = max(abs(e[0] - ex), abs(e[1] - ey))
        print('frame %d  est (%.2f, %.2f)  true (%.2f, %.2f)  err %.2f' % (i, e[0], e[1], ex, ey, err))
        if i != k and err > 0.6: bad += 1
    print('misaligned', bad)
    ok = bad == 0
    # noise reduction : compare fused (downsampled) vs reference single frame against the noise-free dark reference
    clean = shifted(base, *truth[k]) * 0.12
    single = frames[k][..., 0]
    fused_lr = out[..., 0].reshape(h, 2, w, 2).mean(axis=(1, 3))
    e1 = np.sqrt(((single - clean) ** 2)[20:-20, 20:-20].mean()); e2 = np.sqrt(((fused_lr - clean) ** 2)[20:-20, 20:-20].mean())
    print('RMS error vs clean : single %.2f  fused %.2f' % (e1, e2))
    ok = ok and e2 < e1 * 0.7
    print('ALL OK' if ok else 'FAILED')
    raise SystemExit(0 if ok else 1)
