#pragma once
// Mouth tracking : mathématiques pures (sans OpenCV ni Windows) : étalonnage par expressions, conversion en valeurs 0..1 / -1..1, paquet UDP.
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <sstream>
#include <string>
#include <vector>

namespace mouth {

// Ce que le détecteur d'image mesure : la cavité sombre de la bouche (intérieur de la bouche ouverte, ou ligne des lèvres).
// Toutes les valeurs sont relatives à l'image (0..1), donc indépendantes de la résolution.
struct Feature {
  bool found = false;
  double area = 0;            // aire / aire de l'image
  double width = 0, height = 0;   // largeur / largeur de l'image, hauteur / hauteur de l'image
  double cx = 0.5, cy = 0.5;      // centre
};

inline double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }

// Étalonnage : 4 expressions. "neutre" et "grande ouverte" suffisent pour la mâchoire ; "large" (sourire) et "serrée" (bisou) calibrent la largeur.
struct Cal {
  bool hasNeutral = false, hasOpen = false, hasWide = false, hasNarrow = false;
  Feature neutral, open, wide, narrow;
  bool Valid() const { return hasNeutral && hasOpen; }
};

struct Out {
  bool valid = false;
  float jawOpen = 0;   // 0 = fermée .. 1 = grande ouverte
  float wide = 0;      // -1 = serrée (bisou) .. 0 = neutre .. +1 = large (sourire)
  float side = 0;      // -1 = mâchoire à gauche .. +1 = à droite (vue de la personne)
  float vert = 0;      // déplacement vertical de la bouche (-1 .. 1)
};

inline Out Map(const Cal& c, const Feature& f) {
  Out o;
  if (!c.Valid()) return o;
  const Feature& n = c.neutral;
  const Feature& p = c.open;
  const double dh = std::max(1e-3, p.height - n.height), da = std::max(1e-4, p.area - n.area);
  const double th = (f.height - n.height) / dh, ta = (f.area - n.area) / da;
  o.jawOpen = (float)Clamp(0.6 * th + 0.4 * ta, 0.0, 1.0);
  if (f.found) {
    const double base = std::max(0.05, n.width);
    const double upR = c.hasWide ? std::max(1e-3, c.wide.width - n.width) : 0.30 * base;
    const double dnR = c.hasNarrow ? std::max(1e-3, n.width - c.narrow.width) : 0.30 * base;
    const double dw = f.width - n.width;
    o.wide = (float)Clamp(dw >= 0 ? dw / upR : dw / dnR, -1.0, 1.0);
    o.side = (float)Clamp((f.cx - n.cx) / 0.12, -1.0, 1.0);
    o.vert = (float)Clamp((f.cy - n.cy) / 0.12, -1.0, 1.0);
  }
  o.valid = true;   // une bouche fermée sans cavité visible est une mesure valide (jawOpen = 0)
  return o;
}

inline Feature MedianFeature(const std::vector<Feature>& v) {
  Feature r;
  if (v.empty()) return r;
  auto med = [&](double Feature::*m) {
    std::vector<double> t;
    t.reserve(v.size());
    for (const auto& f : v) t.push_back(f.*m);
    std::sort(t.begin(), t.end());
    return t[t.size() / 2];
  };
  size_t found = 0;
  for (const auto& f : v) if (f.found) found++;
  r.found = found * 2 >= v.size();
  r.area = med(&Feature::area);
  r.width = med(&Feature::width);
  r.height = med(&Feature::height);
  r.cx = med(&Feature::cx);
  r.cy = med(&Feature::cy);
  return r;
}

// ---------------------------------------------------------------- sauvegarde (texte, une ligne par expression)
inline std::string FeatureToString(const Feature& f) {
  char b[160];
  std::snprintf(b, sizeof(b), "%d %.6f %.6f %.6f %.6f %.6f", f.found ? 1 : 0, f.area, f.width, f.height, f.cx, f.cy);
  return b;
}
inline bool FeatureFromString(const std::string& s, Feature* f) {
  std::istringstream in(s);
  int fd;
  Feature t;
  if (!(in >> fd >> t.area >> t.width >> t.height >> t.cx >> t.cy)) return false;
  t.found = fd != 0;
  *f = t;
  return true;
}

// ---------------------------------------------------------------- paquet UDP vers Unity (36 octets, petit-boutiste) : port 9886
constexpr uint32_t kMagic = 0x3148544Du;   // "MTH1"
constexpr int kPacketSize = 36;
enum Flags : uint8_t { kValid = 1, kCalibrated = 2, kCalibrating = 4 };

struct Packet {
  uint8_t flags = 0;
  uint16_t seq = 0;
  float jawOpen = 0, wide = 0, side = 0, vert = 0, rawArea = 0, rawWidth = 0;
  uint32_t micros = 0;
};

inline void Encode(const Packet& g, uint8_t* out) {
  uint32_t magic = kMagic;
  std::memcpy(out, &magic, 4);
  out[4] = 1;
  out[5] = g.flags;
  std::memcpy(out + 6, &g.seq, 2);
  std::memcpy(out + 8, &g.jawOpen, 4);
  std::memcpy(out + 12, &g.wide, 4);
  std::memcpy(out + 16, &g.side, 4);
  std::memcpy(out + 20, &g.vert, 4);
  std::memcpy(out + 24, &g.rawArea, 4);
  std::memcpy(out + 28, &g.rawWidth, 4);
  std::memcpy(out + 32, &g.micros, 4);
}

}  // namespace mouth
