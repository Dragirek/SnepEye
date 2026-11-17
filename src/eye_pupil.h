#pragma once
// Détecteur de pupille et de reflet infrarouge (glint) pour une caméra d'oeil en niveaux de gris (ex. 320x240).
// Algorithme maison, léger (~0,3 ms par image) ; référence testée : tests/eye_pupil_ref.py (même algorithme en Python).
//
//  1. Zone de recherche : autour de la dernière pupille si on la suit, sinon toute l'image (repli automatique si la tache touche le bord).
//  2. Flou 5x5, puis seuil adaptatif : (gris le plus sombre de l'image) + marge. Sous éclairage infrarouge la pupille est le point le plus sombre.
//  3. Fermeture 9x9 (rebouche les trous faits par les reflets) puis ouverture 5x5 (enlève cils et bruit).
//  4. Parmi les taches : taille plausible, bien pleine (solidité), pas trop allongée ; la plus ronde et la plus grosse gagne.
//  5. Centre = centre de gravité de l'enveloppe convexe (précis au sous-pixel, insensible aux reflets et aux cils).
//  6. Reflet : le petit point le plus clair près de la pupille, centre pondéré par la luminosité.
//  7. Affinage du contour (c.refine) : 32 rayons partent du centre ; sur chacun on cherche, au 1/10 de pixel, l'endroit où la luminosité passe à mi-chemin
//     entre l'intérieur de la pupille et l'iris. Une ellipse ajustée sur ces points donne le centre et le rayon, indépendamment du seuil et des
//     paupières qui mordent un peu sur la pupille (rayons aberrants écartés). Gain mesuré : tests/eye_pupil_test.cpp.
#include <algorithm>
#include <cmath>
#include <vector>

#include <opencv2/imgproc.hpp>

namespace eye {

struct PupilConfig {
  int margin = 28;             // gris au-dessus du plus sombre pour le seuil (augmenter si la pupille est mal remplie, diminuer si l'iris est pris)
  double darkPct = 0.002;      // part de l'image considérée comme "le plus sombre"
  double minArea = 0.0025;     // aire mini de la pupille (part de l'image)
  double maxArea = 0.12;       // aire maxi
  double minSolidity = 0.80;
  double minAspect = 0.50;
  int glintMin = 200;          // luminosité mini d'un reflet
  bool refine = true;          // affinage du contour au sous-pixel (étape 7) ; false = centre de l'enveloppe convexe seul
};

struct PupilResult {
  bool found = false;
  double px = 0, py = 0, radius = 0, area = 0;   // pixels de l'image
  bool glint = false;
  double gx = 0, gy = 0;
};

class PupilDetector {
 public:
  explicit PupilDetector(const PupilConfig& c = PupilConfig()) : c_(c) {
    kOpen_ = cv::getStructuringElement(cv::MORPH_ELLIPSE, cv::Size(5, 5));
    kClose_ = cv::getStructuringElement(cv::MORPH_ELLIPSE, cv::Size(9, 9));
  }
  void SetConfig(const PupilConfig& c) { c_ = c; }
  void Reset() { have_ = false; misses_ = 0; }

  // 'gray' : image 8 bits, 1 canal. Retourne la pupille (found = false si introuvable : clignement, oeil fermé...).
  PupilResult Detect(const cv::Mat& gray) {
    PupilResult res;
    if (gray.empty() || gray.type() != CV_8UC1) return res;
    const int W = gray.cols, H = gray.rows;
    Best best;
    int x0 = 0, y0 = 0;
    if (have_ && misses_ < 4) {
      // 1) autour de la dernière pupille (rapide)
      const double R = std::max(40.0, 2.6 * r_);
      x0 = (int)std::max(0.0, std::floor(cx_ - R));
      y0 = (int)std::max(0.0, std::floor(cy_ - R));
      const int x1 = (int)std::min((double)W, std::ceil(cx_ + R));
      const int y1 = (int)std::min((double)H, std::ceil(cy_ + R));
      best = Search(gray, x0, y0, x1, y1, true, R);
      if (best.ok && best.touches && (x1 - x0 < W || y1 - y0 < H)) best.ok = false;   // tache coupée par le bord : on refait sur toute l'image
    }
    if (!best.ok) {
      x0 = 0; y0 = 0;
      best = Search(gray, 0, 0, W, H, false, (double)std::max(W, H));
    }
    if (!best.ok) { misses_++; return res; }

    cv::Moments m = cv::moments(best.hull);
    if (m.m00 <= 0) { misses_++; return res; }
    res.found = true;
    res.px = m.m10 / m.m00 + x0;
    res.py = m.m01 / m.m00 + y0;
    res.area = best.area;
    res.radius = std::sqrt(best.area / CV_PI);
    if (c_.refine) Refine(gray, &res);
    have_ = true; cx_ = res.px; cy_ = res.py; r_ = res.radius; misses_ = 0;
    FindGlint(gray, &res);
    return res;
  }

 private:
  struct Best {
    bool ok = false, touches = false;
    std::vector<cv::Point> hull;
    double area = 0;
  };

  Best Search(const cv::Mat& gray, int x0, int y0, int x1, int y1, bool tracking, double R) {
    Best out;
    const double imgArea = (double)gray.cols * gray.rows;
    cv::Mat sub = gray(cv::Rect(x0, y0, x1 - x0, y1 - y0));
    cv::blur(sub, blur_, cv::Size(5, 5));
    // histogramme -> gris le plus sombre (hors bruit)
    int hist[256] = {0};
    for (int y = 0; y < blur_.rows; y++) {
      const uchar* p = blur_.ptr<uchar>(y);
      for (int x = 0; x < blur_.cols; x++) hist[p[x]]++;
    }
    const double need = std::max(8.0, c_.darkPct * imgArea);
    int dark = 0;
    double cum = 0;
    for (; dark < 255; dark++) { cum += hist[dark]; if (cum >= need) break; }
    const int T = std::min(150, dark + c_.margin);
    cv::threshold(blur_, bin_, T, 255, cv::THRESH_BINARY_INV);
    cv::morphologyEx(bin_, bin_, cv::MORPH_CLOSE, kClose_);   // rebouche les trous laissés par les reflets
    cv::morphologyEx(bin_, bin_, cv::MORPH_OPEN, kOpen_);     // enlève cils et bruit fin
    contours_.clear();
    cv::findContours(bin_, contours_, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

    double bestScore = 0;
    for (const auto& cn : contours_) {
      if (cn.size() < 5) continue;
      const double area = cv::contourArea(cn);
      if (area < c_.minArea * imgArea || area > c_.maxArea * imgArea) continue;
      std::vector<cv::Point> hull;
      cv::convexHull(cn, hull);
      const double ha = cv::contourArea(hull);
      if (ha <= 0) continue;
      const double sol = area / ha;
      if (sol < c_.minSolidity) continue;
      cv::RotatedRect e = cv::fitEllipse(hull.size() >= 5 ? hull : cn);   // fitEllipse exige 5 points
      const double mx = std::max((double)std::max(e.size.width, e.size.height), 1e-6);
      const double aspect = std::min(e.size.width, e.size.height) / mx;
      if (aspect < c_.minAspect) continue;
      double score = sol * aspect * std::sqrt(area);
      if (tracking) {
        cv::Moments m = cv::moments(hull);
        if (m.m00 > 0) {
          const double d = std::hypot(m.m10 / m.m00 + x0 - cx_, m.m01 / m.m00 + y0 - cy_);
          score *= 1.0 - 0.5 * std::min(1.0, d / R);
        }
      }
      if (score > bestScore) {
        const cv::Rect b = cv::boundingRect(hull);
        out.touches = b.x <= 0 || b.y <= 0 || b.x + b.width >= (x1 - x0) || b.y + b.height >= (y1 - y0);
        out.hull = hull;
        out.area = ha;
        out.ok = true;
        bestScore = score;
      }
    }
    return out;
  }


  // Luminosité interpolée (bilinéaire) ; hors image : le bord.
  static double Sample(const cv::Mat& g, double x, double y) {
    x = std::min(std::max(x, 0.0), g.cols - 1.001);
    y = std::min(std::max(y, 0.0), g.rows - 1.001);
    const int ix = (int)x, iy = (int)y;
    const double fx = x - ix, fy = y - iy;
    const uchar* p0 = g.ptr<uchar>(iy);
    const uchar* p1 = g.ptr<uchar>(iy + 1);
    return (p0[ix] * (1 - fx) + p0[ix + 1] * fx) * (1 - fy) + (p1[ix] * (1 - fx) + p1[ix + 1] * fx) * fy;
  }

  // Affinage : voir l'étape 7 en tête de fichier. Ne change rien si le résultat n'est pas cohérent avec la détection de départ.
  void Refine(const cv::Mat& gray, PupilResult* r) {
    const double r0 = r->radius;
    if (r0 < 4.0) return;
    const int pad = (int)std::ceil(2.4 * r0) + 4;
    const int x0 = std::max(0, (int)(r->px - pad)), x1 = std::min(gray.cols, (int)(r->px + pad) + 1);
    const int y0 = std::max(0, (int)(r->py - pad)), y1 = std::min(gray.rows, (int)(r->py + pad) + 1);
    if (x1 - x0 < 12 || y1 - y0 < 12) return;
    cv::blur(gray(cv::Rect(x0, y0, x1 - x0, y1 - y0)), rblur_, cv::Size(3, 3));
    const double cx = r->px - x0, cy = r->py - y0;
    const int N = 32;
    const double s0 = 0.45 * r0, s1 = 2.1 * r0, ds = 0.4;
    const int ns = (int)((s1 - s0) / ds) + 1;
    prof_.assign((size_t)N * ns, 0.0);
    std::vector<double> inner;
    for (int k = 0; k < N; k++) {
      const double a = 2.0 * CV_PI * k / N, ca = std::cos(a), sa = std::sin(a);
      for (int i = 0; i < ns; i++) {
        const double s = s0 + i * ds;
        prof_[(size_t)k * ns + i] = Sample(rblur_, cx + s * ca, cy + s * sa);
        if (s <= 0.75 * r0) inner.push_back(prof_[(size_t)k * ns + i]);
      }
    }
    if (inner.size() < 8) return;
    std::nth_element(inner.begin(), inner.begin() + inner.size() / 2, inner.end());
    const double lin = inner[inner.size() / 2];   // médiane : le reflet qui peut se trouver dans la pupille ne la fausse pas
    std::vector<double> outer;
    for (int k = 0; k < N; k++)
      for (int i = 0; i < ns; i++)
        if (s0 + i * ds >= 1.45 * r0) outer.push_back(prof_[(size_t)k * ns + i]);
    if (outer.empty()) return;
    std::nth_element(outer.begin(), outer.begin() + outer.size() / 2, outer.end());
    const double lout = outer[outer.size() / 2];
    if (lout - lin < 12.0) return;                // pas assez de contraste entre pupille et iris : on garde la détection de départ
    const double mid = 0.5 * (lin + lout);
    std::vector<cv::Point2f> pts;
    std::vector<double> rad;
    for (int k = 0; k < N; k++) {
      const double a = 2.0 * CV_PI * k / N, ca = std::cos(a), sa = std::sin(a);
      for (int i = 1; i < ns; i++) {
        const double a0 = prof_[(size_t)k * ns + i - 1], a1 = prof_[(size_t)k * ns + i];
        if (a0 < mid && a1 >= mid) {   // première montée au-dessus du niveau moyen : le bord de la pupille
          const double f = (mid - a0) / std::max(1e-6, a1 - a0);
          const double s = s0 + (i - 1 + f) * ds;
          pts.emplace_back((float)(cx + s * ca), (float)(cy + s * sa));
          rad.push_back(s);
          break;
        }
      }
    }
    if (pts.size() < 14) return;
    std::vector<double> sr = rad;
    std::nth_element(sr.begin(), sr.begin() + sr.size() / 2, sr.end());
    const double med = sr[sr.size() / 2];
    std::vector<cv::Point2f> good;
    for (size_t i = 0; i < pts.size(); i++)
      if (std::fabs(rad[i] - med) < 0.3 * med) good.push_back(pts[i]);   // paupière, cil, rayon qui sort de la pupille : écartés
    if (good.size() < 12) return;
    const cv::RotatedRect e = cv::fitEllipse(good);
    const double ma = std::max(e.size.width, e.size.height), mi = std::min(e.size.width, e.size.height);
    if (mi < 0.6 * ma) return;
    const double er = 0.25 * (e.size.width + e.size.height);
    if (er < 0.7 * r0 || er > 1.4 * r0) return;
    const double ex = e.center.x + x0, ey = e.center.y + y0;
    if (std::hypot(ex - r->px, ey - r->py) > 0.35 * r0) return;
    r->px = ex;
    r->py = ey;
    r->radius = er;
    r->area = CV_PI * er * er;
  }

  // Reflet infrarouge : petit point très clair dans la zone de la pupille
  void FindGlint(const cv::Mat& gray, PupilResult* r) {
    const int W = gray.cols, H = gray.rows;
    const int gw = (int)(1.7 * r->radius + 6);
    const int gx0 = (int)std::max(0.0, r->px - gw), gx1 = (int)std::min((double)W, r->px + gw + 1);
    const int gy0 = (int)std::max(0.0, r->py - gw), gy1 = (int)std::min((double)H, r->py + gw + 1);
    if (gx1 - gx0 < 3 || gy1 - gy0 < 3) return;
    cv::Mat win = gray(cv::Rect(gx0, gy0, gx1 - gx0, gy1 - gy0));
    double mx = 0;
    cv::minMaxLoc(win, nullptr, &mx);
    if (mx < c_.glintMin) return;
    const int thr = std::max(c_.glintMin - 1, (int)mx - 20);
    cv::threshold(win, gbin_, thr, 255, cv::THRESH_BINARY);
    cv::Mat stats, cents;
    const int n = cv::connectedComponentsWithStats(gbin_, glab_, stats, cents, 8, CV_32S);
    double bd = 1e9, bx = 0, by = 0;
    bool have = false;
    for (int i = 1; i < n; i++) {
      const int a = stats.at<int>(i, cv::CC_STAT_AREA);
      if (a < 1 || a > 90) continue;
      const double gcx = cents.at<double>(i, 0) + gx0, gcy = cents.at<double>(i, 1) + gy0;
      const double d = std::hypot(gcx - r->px, gcy - r->py);
      if (d < bd && d <= 1.5 * r->radius + 4) { bd = d; bx = gcx; by = gcy; have = true; }
    }
    if (!have) return;
    // centre pondéré par la luminosité (précision sous le pixel)
    const int xs = std::max(0, (int)bx - 3), xe = std::min(W, (int)bx + 4);
    const int ys = std::max(0, (int)by - 3), ye = std::min(H, (int)by + 4);
    double sw = 0, sx = 0, sy = 0;
    const int base = thr - 25;
    for (int y = ys; y < ye; y++) {
      const uchar* p = gray.ptr<uchar>(y);
      for (int x = xs; x < xe; x++) {
        const double w = (double)p[x] - base;
        if (w > 0) { sw += w; sx += w * x; sy += w * y; }
      }
    }
    if (sw > 0) { bx = sx / sw; by = sy / sw; }
    r->glint = true; r->gx = bx; r->gy = by;
  }

  PupilConfig c_;
  cv::Mat kOpen_, kClose_, blur_, bin_, gbin_, glab_, rblur_;
  std::vector<double> prof_;
  std::vector<std::vector<cv::Point>> contours_;
  bool have_ = false;
  double cx_ = 0, cy_ = 0, r_ = 0;
  int misses_ = 0;
};

}  // namespace eye
