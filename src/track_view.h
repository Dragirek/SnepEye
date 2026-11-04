#pragma once
// Vue du suivi pour l'application (aperçu 800x300, source « Suivi ») : la trajectoire vue de dessus (grille, chemin, position, flèche de cap),
// la hauteur dans le temps et l'état du suivi (points, calcul, sol). Rien n'est calculé tant que l'application ne la demande pas : main.cpp
// ne fait que recopier quelques nombres par image (Update), le dessin n'est fait que par Draw() quand l'aperçu est demandé.
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <deque>
#include <mutex>
#include <string>

#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>

#include "orientation.h"

namespace ar6trk {

struct State {
  std::mutex m;
  bool active = false;     // le suivi tourne
  int vo = 0;              // 0 initialisation, 1 suivi correct, 2 perdu
  Vec3 pos, fwd{1, 0, 0};
  int points = 0, inliers = 0;
  bool floorValid = false;
  double floorZ = 0, ms = 0;
  std::deque<Vec3> path;
  std::deque<float> height;
  double lastHeightT = 0;
};

inline State& S() { static State s; return s; }

inline double NowS() {
  using namespace std::chrono;
  return duration<double>(steady_clock::now().time_since_epoch()).count();
}

// À appeler quand le suivi (re)démarre : efface le chemin.
inline void Reset() {
  State& s = S();
  std::lock_guard<std::mutex> l(s.m);
  s.path.clear();
  s.height.clear();
  s.active = true;
  s.vo = 0;
}

inline void Stop() {
  State& s = S();
  std::lock_guard<std::mutex> l(s.m);
  s.active = false;
}

// Une fois par image de la caméra. R : casque -> monde (X avant) ; voState : 0 init, 1 correct, 2 perdu.
inline void Update(int voState, const Vec3& p, const Mat3& R, int pts, int inl, bool floorValid, double floorZ, double ms) {
  State& s = S();
  std::lock_guard<std::mutex> l(s.m);
  s.active = true;
  s.vo = voState;
  s.pos = p;
  s.fwd = R * Vec3{1, 0, 0};
  s.points = pts;
  s.inliers = inl;
  s.floorValid = floorValid;
  s.floorZ = floorZ;
  s.ms = ms;
  if (voState == 1) {
    if (s.path.empty() || Norm(p - s.path.back()) >= 0.02) {
      s.path.push_back(p);
      if (s.path.size() > 900) s.path.pop_front();
    }
  }
  const double t = NowS();
  if (t - s.lastHeightT >= 0.1) {
    s.lastHeightT = t;
    s.height.push_back((float)(floorValid ? p.z - floorZ : p.z));
    if (s.height.size() > 360) s.height.pop_front();
  }
}

inline cv::Mat Draw() {
  cv::Mat img(300, 800, CV_8UC3, cv::Scalar(26, 26, 26));
  State& s = S();
  std::lock_guard<std::mutex> l(s.m);
  const cv::Scalar white(235, 235, 235), grey(160, 160, 160), dim(60, 60, 60), accent(40, 150, 245), green(90, 210, 90), red(70, 70, 235);
  char b[160];

  // ---- carte vue de dessus (x vers la droite, y vers le haut), échelle automatique
  const cv::Rect map(10, 10, 380, 280);
  cv::rectangle(img, map, cv::Scalar(20, 20, 20), cv::FILLED);
  double minx = -0.6, maxx = 0.6, miny = -0.6, maxy = 0.6;
  for (const auto& p : s.path) { minx = std::min(minx, p.x); maxx = std::max(maxx, p.x); miny = std::min(miny, p.y); maxy = std::max(maxy, p.y); }
  minx = std::min(minx, s.pos.x); maxx = std::max(maxx, s.pos.x); miny = std::min(miny, s.pos.y); maxy = std::max(maxy, s.pos.y);
  const double cxw = (minx + maxx) * 0.5, cyw = (miny + maxy) * 0.5;
  const double sc = std::min((map.width - 30.0) / (maxx - minx), (map.height - 30.0) / (maxy - miny));   // pixels par mètre
  const cv::Point c0(map.x + map.width / 2, map.y + map.height / 2);
  auto toPx = [&](double x, double y) { return cv::Point(c0.x + (int)std::lround((x - cxw) * sc), c0.y - (int)std::lround((y - cyw) * sc)); };
  double step = 5.0;
  const double steps[] = {0.1, 0.25, 0.5, 1.0, 2.0, 5.0};
  for (double st : steps) if (st * sc >= 28.0) { step = st; break; }
  const double gx0 = std::floor((cxw - map.width / 2 / sc) / step) * step, gx1 = cxw + map.width / 2 / sc;
  const double gy0 = std::floor((cyw - map.height / 2 / sc) / step) * step, gy1 = cyw + map.height / 2 / sc;
  for (double x = gx0; x <= gx1; x += step) {
    const cv::Point a = toPx(x, gy0 - step), c = toPx(x, gy1 + step);
    cv::line(img, cv::Point(a.x, map.y), cv::Point(c.x, map.y + map.height - 1), std::fabs(x) < 1e-6 ? cv::Scalar(90, 90, 90) : dim, 1);
  }
  for (double y = gy0; y <= gy1; y += step) {
    const cv::Point a = toPx(gx0 - step, y), c = toPx(gx1 + step, y);
    cv::line(img, cv::Point(map.x, a.y), cv::Point(map.x + map.width - 1, c.y), std::fabs(y) < 1e-6 ? cv::Scalar(90, 90, 90) : dim, 1);
  }
  // le chemin : de plus en plus clair vers le présent
  const size_t n = s.path.size();
  for (size_t i = 1; i < n; i++) {
    const int k = 70 + (int)(150.0 * i / n);
    const cv::Point a = toPx(s.path[i - 1].x, s.path[i - 1].y), c = toPx(s.path[i].x, s.path[i].y);
    if (map.contains(a) && map.contains(c)) cv::line(img, a, c, cv::Scalar(k * 0.4, k * 0.8, k), 2, cv::LINE_AA);
  }
  cv::circle(img, toPx(0, 0), 4, cv::Scalar(140, 140, 140), 1, cv::LINE_AA);   // départ
  if (s.active) {
    const cv::Point cur = toPx(s.pos.x, s.pos.y);
    double fx = s.fwd.x, fy = s.fwd.y;
    const double fn = std::sqrt(fx * fx + fy * fy);
    if (fn > 0.05) { fx /= fn; fy /= fn; cv::arrowedLine(img, cur, cv::Point(cur.x + (int)(fx * 34), cur.y - (int)(fy * 34)), accent, 2, cv::LINE_AA, 0, 0.35); }
    cv::circle(img, cur, 6, s.vo == 2 ? red : green, cv::FILLED, cv::LINE_AA);
  }
  cv::rectangle(img, map, cv::Scalar(70, 70, 70), 1);
  std::snprintf(b, sizeof(b), "grille %.2g m", step);
  cv::putText(img, b, cv::Point(map.x + 6, map.y + map.height - 8), cv::FONT_HERSHEY_SIMPLEX, 0.4, grey, 1, cv::LINE_AA);

  // ---- texte
  int y = 36;
  const int x0 = 415;
  if (!s.active) {
    cv::putText(img, "Suivi arrete", cv::Point(x0, y), cv::FONT_HERSHEY_SIMPLEX, 0.7, grey, 1, cv::LINE_AA);
    return img;
  }
  cv::putText(img, s.vo == 1 ? "SUIVI : OK" : s.vo == 2 ? "SUIVI : PERDU (bouge lentement, plus de lumiere ?)" : "SUIVI : initialisation", cv::Point(x0, y),
              cv::FONT_HERSHEY_SIMPLEX, s.vo == 2 ? 0.5 : 0.7, s.vo == 1 ? green : s.vo == 2 ? red : accent, 1, cv::LINE_AA);
  y += 30;
  std::snprintf(b, sizeof(b), "x %+.2f   y %+.2f   z %+.2f m", s.pos.x, s.pos.y, s.pos.z);
  cv::putText(img, b, cv::Point(x0, y), cv::FONT_HERSHEY_SIMPLEX, 0.55, white, 1, cv::LINE_AA);
  y += 24;
  std::snprintf(b, sizeof(b), "points suivis %d (retenus %d)   calcul %.1f ms", s.points, s.inliers, s.ms);
  cv::putText(img, b, cv::Point(x0, y), cv::FONT_HERSHEY_SIMPLEX, 0.5, white, 1, cv::LINE_AA);
  y += 24;
  if (s.floorValid) std::snprintf(b, sizeof(b), "sol detecte : tes yeux sont a %.2f m", s.pos.z - s.floorZ);
  else std::snprintf(b, sizeof(b), "sol non detecte (bouge un peu)");
  cv::putText(img, b, cv::Point(x0, y), cv::FONT_HERSHEY_SIMPLEX, 0.5, s.floorValid ? white : grey, 1, cv::LINE_AA);

  // ---- hauteur dans le temps (36 s)
  const cv::Rect gr(415, 160, 370, 120);
  cv::rectangle(img, gr, cv::Scalar(20, 20, 20), cv::FILLED);
  cv::rectangle(img, gr, cv::Scalar(70, 70, 70), 1);
  cv::putText(img, "hauteur (36 s)", cv::Point(gr.x + 6, gr.y + 14), cv::FONT_HERSHEY_SIMPLEX, 0.4, grey, 1, cv::LINE_AA);
  if (s.height.size() > 2) {
    float lo = 1e9f, hi = -1e9f;
    for (float v : s.height) { lo = std::min(lo, v); hi = std::max(hi, v); }
    if (hi - lo < 0.5f) { const float mid = (hi + lo) * 0.5f; lo = mid - 0.25f; hi = mid + 0.25f; }
    const size_t m = s.height.size();
    cv::Point prev;
    for (size_t i = 0; i < m; i++) {
      const cv::Point p(gr.x + 4 + (int)((gr.width - 8) * (double)i / 359.0) + (int)((gr.width - 8) * (359.0 - (double)(m - 1)) / 359.0),
                        gr.y + gr.height - 6 - (int)((gr.height - 24) * (s.height[i] - lo) / (hi - lo)));
      if (i) cv::line(img, prev, p, accent, 1, cv::LINE_AA);
      prev = p;
    }
    std::snprintf(b, sizeof(b), "%.2f m", s.height.back());
    cv::putText(img, b, cv::Point(gr.x + gr.width - 70, gr.y + 14), cv::FONT_HERSHEY_SIMPLEX, 0.45, white, 1, cv::LINE_AA);
  }
  return img;
}

}  // namespace ar6trk
