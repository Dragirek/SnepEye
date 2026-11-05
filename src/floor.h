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

// Carte des hauteurs, accumulée dans le temps. Les points 3D du suivi sont perdus dès qu'ils sortent de l'image : avec une seule
// image (EstimateFloor) il faut que le sol soit visible CE JOUR-LA avec assez de points, ce qui n'arrive presque jamais quand
// on regarde droit devant, dans une pièce sombre ou avec un sol uni. Ici chaque estimation ajoute les hauteurs vues dans un
// histogramme (cases de 4 cm, oubli lent : demi-vie ~70 s), et le sol est le plus bas groupe dense de cet histogramme :
// il suffit de baisser la tête une fois pour que le sol soit trouvé, puis il reste.
class FloorMap {
 public:
  static constexpr double kMin = -8.0, kMax = 8.0, kBin = 0.04;
  FloorMap() : h_((size_t)((kMax - kMin) / kBin) + 1, 0.0) {}
  void Reset() { std::fill(h_.begin(), h_.end(), 0.0); total_ = 0; sticky_ = false; }

  // zs : hauteurs Z (repère G) des points 3D suivis ; headZ : hauteur de la tête. A appeler quelques fois par seconde.
  FloorEstimate Update(const std::vector<double>& zs, double headZ, double minBelow = 0.8, double maxBelow = 2.4) {
    for (double& v : h_) v *= 0.995;
    total_ *= 0.995;
    for (double z : zs) {
      if (z >= headZ - 0.4 || z <= headZ - maxBelow - 0.3) continue;  // seulement ce qui est assez bas pour etre le sol
      int b = Bin(z);
      if (b >= 0) { h_[(size_t)b] += 1.0; total_ += 1.0; }
    }
    FloorEstimate r;
    if (total_ < 30.0) return r;
    const double need = std::max(25.0, 0.06 * total_);
    const int n = (int)h_.size();
    for (int i = 0; i + 2 < n; i++) {
      double m = h_[(size_t)i] + h_[(size_t)i + 1] + h_[(size_t)i + 2];  // fenetre de 12 cm
      if (m < need) continue;
      // le plus bas groupe dense : on se cale sur le pic de la fenetre (barycentre des 3 cases)
      double zc = kMin + (i + 0.5) * kBin, w = 0.0, acc = 0.0;
      for (int k = 0; k < 3; k++) { double c = kMin + (i + k + 0.5) * kBin; w += h_[(size_t)i + k]; acc += c * h_[(size_t)i + k]; }
      zc = acc / std::max(1e-9, w);
      double below = headZ - zc;
      // une fois le sol trouve, on le garde meme si la tete descend (accroupi) ; sinon il doit etre plausible
      double lo = sticky_ ? 0.25 : minBelow;
      if (below >= lo && below <= maxBelow + 0.3) { r.ok = true; r.z = zc; r.count = (int)m; sticky_ = true; }
      return r;
    }
    return r;
  }

 private:
  static int Bin(double z) {
    if (z < kMin || z >= kMax) return -1;
    return (int)((z - kMin) / kBin);
  }
  std::vector<double> h_;
  double total_ = 0;
  bool sticky_ = false;
};

// Une estimation de sol par groupe de hauteurs, filtrée sur la durée.
