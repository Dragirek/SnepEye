#pragma once
// Détection du sol à partir des points 3D suivis par le tracker.
// Le monde G est aligné sur la gravité (Z haut) : le sol est un plan horizontal
// Z = const sous la tête. On cherche le plus bas groupe dense de points.
#include <algorithm>
#include <cmath>
#include <vector>

struct FloorEstimate { bool ok = false; double z = 0; int count = 0; };

// zs : hauteurs Z des points 3D (repère G). headZ : hauteur actuelle de la tête.
// Le sol doit se trouver entre minBelow et maxBelow mètres sous la tête (assis ~1.2 m, debout ~1.7 m).
inline FloorEstimate EstimateFloor(const std::vector<double>& zs, double headZ,
                                   double minBelow = 0.8, double maxBelow = 2.3) {
  FloorEstimate r;
  std::vector<double> c;
  for (double z : zs)
    if (z < headZ - 0.4 && z > headZ - maxBelow - 0.5) c.push_back(z);
  if (c.size() < 30) return r;
  std::sort(c.begin(), c.end());
  const double win = 0.12;  // épaisseur du plan : 12 cm
  const size_t thr = std::max<size_t>(20, (size_t)(0.10 * c.size()));
  size_t j = 0;
  for (size_t i = 0; i < c.size(); i++) {
    if (j < i) j = i;
    while (j < c.size() && c[j] - c[i] <= win) ++j;
    size_t cnt = j - i;
    if (cnt >= thr) {  // le plus bas groupe dense (les points isolés en dessous sont ignorés)
      double med = c[i + cnt / 2];
      double below = headZ - med;
      if (below >= minBelow && below <= maxBelow) { r.ok = true; r.z = med; r.count = (int)cnt; }
      return r;
    }
  }
  return r;
}

// Médiane glissante des dernières estimations : stabilise le sol dans le temps.
class FloorFilter {
 public:
  void Add(const FloorEstimate& e) {
    if (!e.ok) return;
    h_.push_back(e.z);
    if (h_.size() > 25) h_.erase(h_.begin());
  }
  bool Valid() const { return h_.size() >= 5; }
  double Z() const {
    std::vector<double> t = h_;
    std::sort(t.begin(), t.end());
    return t.empty() ? 0.0 : t[t.size() / 2];
  }
  void Reset() { h_.clear(); }
 private:
  std::vector<double> h_;
};
