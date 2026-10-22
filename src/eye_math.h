#pragma once
// Eye tracking : mathématiques pures (sans OpenCV ni Windows) : calibration par points, lissage, clignements, paquet UDP.
// Tout est testé par tests/eye_math_test.cpp.
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <sstream>
#include <string>
#include <vector>

namespace eye {

constexpr double kPi = 3.14159265358979323846;

// Ce que le détecteur d'image mesure sur un oeil : centre de la pupille et, si visible, centre du reflet infrarouge (glint), en pixels.
struct Feature {
  double px = 0, py = 0;
  double gx = 0, gy = 0;
  bool glint = false;
};

inline double Median(std::vector<double> v) {
  if (v.empty()) return 0;
  std::sort(v.begin(), v.end());
  size_t n = v.size();
  return (n % 2) ? v[n / 2] : 0.5 * (v[n / 2 - 1] + v[n / 2]);
}

// ---------------------------------------------------------------- correspondance caractéristique -> angle du regard
// Polynôme d'ordre 2 (6 termes) ou, avec peu de points, d'ordre 1 (3 termes), moindres carrés avec une très légère régularisation.
inline int BasisSize(int order) { return order <= 1 ? 3 : 6; }
inline void Basis(int order, double u, double v, double* b) {
  b[0] = 1; b[1] = u; b[2] = v;
  if (order > 1) { b[3] = u * v; b[4] = u * u; b[5] = v * v; }
}

struct Sample {      // un point d'étalonnage : ce que l'oeil a donné quand la personne regardait (yaw, pitch)
  double fx, fy;     // caractéristique (pixels)
  double yaw, pitch; // degrés : yaw > 0 vers la droite, pitch > 0 vers le haut, par rapport à la tête
};

struct Map {
  bool valid = false;
  int order = 2;
  bool useGlint = false;          // vrai : caractéristique = pupille - reflet (insensible au glissement du casque)
  double mx = 0, my = 0, sx = 1, sy = 1;
  double cyaw[6] = {0, 0, 0, 0, 0, 0}, cpitch[6] = {0, 0, 0, 0, 0, 0};
  double offYaw = 0, offPitch = 0;  // correction de biais (recentrage en un point)

  void Feat(const Feature& f, double* x, double* y) const {
    *x = useGlint ? f.px - f.gx : f.px;
    *y = useGlint ? f.py - f.gy : f.py;
  }
  // Faux si la carte demande le reflet et qu'il manque
  bool Apply(const Feature& f, double* yaw, double* pitch) const {
    if (!valid || (useGlint && !f.glint)) return false;
    double x, y;
    Feat(f, &x, &y);
    double b[6];
    Basis(order, (x - mx) / sx, (y - my) / sy, b);
    double yw = 0, pt = 0;
    for (int i = 0; i < BasisSize(order); i++) { yw += cyaw[i] * b[i]; pt += cpitch[i] * b[i]; }
    *yaw = yw + offYaw;
    *pitch = pt + offPitch;
    return true;
  }
};

// Résout A x = b (n <= 6) par élimination de Gauss avec pivot. Faux si la matrice est singulière.
inline bool SolveLinear(double A[6][7], int n, double* x) {
  for (int c = 0; c < n; c++) {
    int p = c;
    for (int r = c + 1; r < n; r++) if (std::fabs(A[r][c]) > std::fabs(A[p][c])) p = r;
    if (std::fabs(A[p][c]) < 1e-12) return false;
    if (p != c) for (int k = 0; k <= n; k++) std::swap(A[p][k], A[c][k]);
    for (int r = c + 1; r < n; r++) {
      double f = A[r][c] / A[c][c];
      for (int k = c; k <= n; k++) A[r][k] -= f * A[c][k];
    }
  }
  for (int r = n - 1; r >= 0; r--) {
    double s = A[r][n];
    for (int k = r + 1; k < n; k++) s -= A[r][k] * x[k];
    x[r] = s / A[r][r];
  }
  return true;
}

// Ajuste la carte sur des points (caractéristiques déjà choisies selon useGlint). rms = erreur moyenne quadratique en degrés sur les points.
inline bool FitMap(const std::vector<Sample>& s, bool useGlint, Map* out, double* rms) {
  int n = (int)s.size();
  if (n < 4) return false;
  Map m;
  m.useGlint = useGlint;
  m.order = n >= 8 ? 2 : 1;
  double minx = 1e30, maxx = -1e30, miny = 1e30, maxy = -1e30;
  for (const auto& p : s) {
    m.mx += p.fx; m.my += p.fy;
    minx = std::min(minx, p.fx); maxx = std::max(maxx, p.fx);
    miny = std::min(miny, p.fy); maxy = std::max(maxy, p.fy);
  }
  m.mx /= n; m.my /= n;
  m.sx = std::max(0.5 * (maxx - minx), 1e-3);
  m.sy = std::max(0.5 * (maxy - miny), 1e-3);
  if (maxx - minx < 1e-3 && maxy - miny < 1e-3) return false;   // la pupille n'a pas bougé : rien à apprendre
  const int k = BasisSize(m.order);
  double A[6][7], B[6][7];
  for (int i = 0; i < k; i++) for (int j = 0; j <= k; j++) A[i][j] = B[i][j] = 0;
  for (const auto& p : s) {
    double b[6];
    Basis(m.order, (p.fx - m.mx) / m.sx, (p.fy - m.my) / m.sy, b);
    for (int i = 0; i < k; i++) {
      for (int j = 0; j < k; j++) { A[i][j] += b[i] * b[j]; B[i][j] += b[i] * b[j]; }
      A[i][k] += b[i] * p.yaw;
      B[i][k] += b[i] * p.pitch;
    }
  }
  const double ridge = 1e-3 * n;
  for (int i = 1; i < k; i++) { A[i][i] += ridge; B[i][i] += ridge; }   // pas sur le terme constant
  if (!SolveLinear(A, k, m.cyaw)) return false;
  if (!SolveLinear(B, k, m.cpitch)) return false;
  m.valid = true;
  double se = 0;
  for (const auto& p : s) {
    Feature f; f.px = p.fx; f.py = p.fy;
    Map t = m; t.useGlint = false;   // les caractéristiques de 's' sont déjà finales
    double yw, pt;
    t.Apply(f, &yw, &pt);
    se += (yw - p.yaw) * (yw - p.yaw) + (pt - p.pitch) * (pt - p.pitch);
  }
  if (rms) *rms = std::sqrt(se / n);
  *out = m;
  return true;
}

inline std::string MapToString(const Map& m) {
  std::ostringstream o;
  o.precision(10);
  o << "v1 " << (m.valid ? 1 : 0) << " " << m.order << " " << (m.useGlint ? 1 : 0) << " " << m.mx << " " << m.my << " " << m.sx << " " << m.sy;
  for (int i = 0; i < 6; i++) o << " " << m.cyaw[i];
  for (int i = 0; i < 6; i++) o << " " << m.cpitch[i];
  o << " " << m.offYaw << " " << m.offPitch;
  return o.str();
}

inline bool MapFromString(const std::string& s, Map* m) {
  std::istringstream in(s);
  std::string tag;
  int valid, order, glint;
  Map r;
  if (!(in >> tag >> valid >> order >> glint >> r.mx >> r.my >> r.sx >> r.sy) || tag != "v1") return false;
  for (int i = 0; i < 6; i++) if (!(in >> r.cyaw[i])) return false;
  for (int i = 0; i < 6; i++) if (!(in >> r.cpitch[i])) return false;
  if (!(in >> r.offYaw >> r.offPitch)) return false;
  if (order < 1 || order > 2 || !(r.sx > 0) || !(r.sy > 0)) return false;
  r.valid = valid != 0; r.order = order; r.useGlint = glint != 0;
  *m = r;
  return true;
}

// ---------------------------------------------------------------- étalonnage : un point = beaucoup de mesures, on garde la médiane
struct PointSamples {
  double yaw = 0, pitch = 0;
  std::vector<Feature> f;
};

// Construit la carte d'un oeil à partir des points mesurés. Le reflet n'est utilisé que s'il est presque toujours visible.
// Retourne faux s'il y a moins de 4 points exploitables.
inline bool SolveEye(const std::vector<PointSamples>& pts, Map* out, double* rms, int* usedPoints) {
  size_t total = 0, withGlint = 0;
  for (const auto& p : pts) for (const auto& f : p.f) { total++; if (f.glint) withGlint++; }
  if (total == 0) return false;
  const bool useGlint = withGlint >= 0.9 * total;
  std::vector<Sample> s;
  for (const auto& p : pts) {
    std::vector<double> xs, ys;
    for (const auto& f : p.f) {
      if (useGlint && !f.glint) continue;
      xs.push_back(useGlint ? f.px - f.gx : f.px);
      ys.push_back(useGlint ? f.py - f.gy : f.py);
    }
    if (xs.size() < 8) continue;   // trop peu de mesures sur ce point (clignement, pupille perdue)
    s.push_back({Median(xs), Median(ys), p.yaw, p.pitch});
  }
  if (usedPoints) *usedPoints = (int)s.size();
  return FitMap(s, useGlint, out, rms);
}

// ---------------------------------------------------------------- lissage adaptatif (One Euro) : stable à l'arrêt, sans retard en saccade
struct OneEuro {
  double minCutoff = 2.0;   // Hz : plus bas = plus stable à l'arrêt
  double beta = 0.05;       // plus haut = plus réactif en mouvement rapide (unités : par degré/seconde)
  double dCutoff = 1.0;
  bool init = false;
  double xPrev = 0, dxPrev = 0, tPrev = 0;

  static double Alpha(double cutoff, double dt) {
    double tau = 1.0 / (2.0 * kPi * cutoff);
    return 1.0 / (1.0 + tau / dt);
  }
  void Reset() { init = false; }
  double Filter(double x, double t) {
    if (!init) { init = true; xPrev = x; dxPrev = 0; tPrev = t; return x; }
    double dt = t - tPrev;
    if (dt < 1e-4) dt = 1e-4;
    if (dt > 0.5) { xPrev = x; dxPrev = 0; tPrev = t; return x; }   // longue interruption : on repart de la mesure
    double dx = (x - xPrev) / dt;
    double ad = Alpha(dCutoff, dt);
    dxPrev = dxPrev + ad * (dx - dxPrev);
    double cutoff = minCutoff + beta * std::fabs(dxPrev);
    double a = Alpha(cutoff, dt);
    xPrev = xPrev + a * (x - xPrev);
    tPrev = t;
    return xPrev;
  }
};

// ---------------------------------------------------------------- clignements
// La pupille disparaît (ou devient très petite) quand la paupière se ferme. 'ref' = plus grande aire récente (décroît lentement).
struct BlinkState {
  int missing = 0;
  double ref = 0;
  double openness = 1.0;
  bool blink = false;
  void Update(bool found, double area) {
    if (found) {
      missing = 0;
      ref = std::max(ref * 0.9998, area);
      double r = ref > 0 ? area / ref : 1.0;
      openness = std::min(1.0, std::max(0.0, (r - 0.35) / 0.5));
    } else {
      missing++;
      if (missing >= 3) openness = 0;   // 1 ou 2 images perdues = bruit, pas un clignement
    }
    blink = missing >= 3 || openness < 0.25;
  }
};

// ---------------------------------------------------------------- paquet de regard (44 octets, petit-boutiste) -> Unity UDP 9883
constexpr uint32_t kGazeMagic = 0x31455945u;   // "EYE1"
constexpr int kGazeSize = 44;
enum GazeFlags : uint8_t { kValidL = 1, kValidR = 2, kBlinkL = 4, kBlinkR = 8, kCalibrated = 16, kCalibrating = 32 };

struct GazePacket {
  uint8_t flags = 0;
  uint16_t seq = 0;
  float yawL = 0, pitchL = 0, yawR = 0, pitchR = 0, yawC = 0, pitchC = 0;   // degrés
  float openL = 1, openR = 1;                                                // 0 fermé .. 1 ouvert
  uint32_t micros = 0;
};

inline void EncodeGaze(const GazePacket& g, uint8_t* out) {
  uint32_t magic = kGazeMagic;
  std::memcpy(out, &magic, 4);
  out[4] = 1;
  out[5] = g.flags;
  std::memcpy(out + 6, &g.seq, 2);
  std::memcpy(out + 8, &g.yawL, 4);
  std::memcpy(out + 12, &g.pitchL, 4);
  std::memcpy(out + 16, &g.yawR, 4);
  std::memcpy(out + 20, &g.pitchR, 4);
  std::memcpy(out + 24, &g.yawC, 4);
  std::memcpy(out + 28, &g.pitchC, 4);
  std::memcpy(out + 32, &g.openL, 4);
  std::memcpy(out + 36, &g.openR, 4);
  std::memcpy(out + 40, &g.micros, 4);
}

inline bool DecodeGaze(const uint8_t* d, int n, GazePacket* g) {
  if (n != kGazeSize) return false;
  uint32_t magic;
  std::memcpy(&magic, d, 4);
  if (magic != kGazeMagic || d[4] != 1) return false;
  g->flags = d[5];
  std::memcpy(&g->seq, d + 6, 2);
  std::memcpy(&g->yawL, d + 8, 4);
  std::memcpy(&g->pitchL, d + 12, 4);
  std::memcpy(&g->yawR, d + 16, 4);
  std::memcpy(&g->pitchR, d + 20, 4);
  std::memcpy(&g->yawC, d + 24, 4);
  std::memcpy(&g->pitchC, d + 28, 4);
  std::memcpy(&g->openL, d + 32, 4);
  std::memcpy(&g->openR, d + 36, 4);
  std::memcpy(&g->micros, d + 40, 4);
  return true;
}

// ---------------------------------------------------------------- commandes texte de Unity (UDP 9884), une par ligne
//   EYE 1 | EYE 0                      démarre / arrête les caméras (arrêtées : aucune charge)
//   CAL_BEGIN                          efface les mesures d'étalonnage en cours
//   CAL_POINT yaw pitch settle collect cible (degrés), attente en ms avant la mesure, durée de la mesure en ms
//   CAL_SOLVE                          calcule et enregistre la carte ; répond CAL_RESULT
//   CAL_CANCEL | CAL_CLEAR             annule l'étalonnage en cours | oublie l'étalonnage enregistré
//   RECENTER                           le regard actuel devient (0, 0) (correction de biais, à faire en regardant droit devant)
//   PING                               répond PONG
inline std::vector<std::string> SplitWords(const std::string& s) {
  std::vector<std::string> w;
  std::istringstream in(s);
  std::string t;
  while (in >> t) w.push_back(t);
  return w;
}

}  // namespace eye
