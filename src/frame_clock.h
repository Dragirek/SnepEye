#pragma once
// Instants de prise de vue des images, sans gigue.
// La caméra produit les images à cadence fixe ; l'instant où on les reçoit varie, car le décodage JPEG prend un temps
// variable. On ajuste une droite sur les instants d'arrivée (t = a + b*n), puis on la décale sur les arrivées les
// MOINS retardées (une image ne peut pas arriver avant d'avoir été prise) : on obtient une horloge régulière.
#include <algorithm>
#include <cstdint>
#include <deque>

class FrameClock {
 public:
  // n : numéro de l'image (croissant), t : instant de réception (secondes). Retourne l'instant lissé.
  double Update(uint64_t n, double t) {
    if (pts_.empty()) { n0_ = n; t0_ = t; }
    pts_.push_back({(double)(n - n0_), t - t0_});
    if (pts_.size() > kWindow) pts_.pop_front();
    const size_t m = pts_.size();
    double x = (double)(n - n0_);
    if (m < 12) return t;  // pas encore assez d'images pour lisser
    double sx = 0, sy = 0, sxx = 0, sxy = 0;
    for (const auto& p : pts_) { sx += p.x; sy += p.y; sxx += p.x * p.x; sxy += p.x * p.y; }
    double den = m * sxx - sx * sx;
    double b = den > 1e-9 ? (m * sxy - sx * sy) / den : 0.0;
    double a = (sy - b * sx) / m;
    double minRes = 1e30;
    for (const auto& p : pts_) minRes = std::min(minRes, p.y - (a + b * p.x));
    return t0_ + a + b * x + minRes;
  }
  void Reset() { pts_.clear(); }

 private:
  struct P { double x, y; };
  static constexpr size_t kWindow = 120;
  std::deque<P> pts_;
  uint64_t n0_ = 0;
  double t0_ = 0;
};
