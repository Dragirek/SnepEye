#pragma once
// Calibration stéréo maison (remplace RTAB-Map) : capture guidée de la mire, calcul avec choix du modèle de distorsion, mesures de qualité,
// vérification de l'échelle à la règle. Sans dépendance Windows (testé sous Linux : tests/calib_session_test.cpp).
//
// Ce qui la rend plus fiable qu'une calibration « à la main » :
//  - elle se fait DANS le mode caméra utilisé pour le suivi (résolution et cadence réelles) : plus de mise à l'échelle d'une calibration
//    faite dans un autre mode, qui fausse les distances si ce mode recadre l'image au lieu de la réduire ;
//  - la détection des coins est sous-pixel (findChessboardCornersSB) et la mire doit être immobile pendant la prise ;
//  - des objectifs de poses (centre, coins, inclinaisons, près, loin, rotation) garantissent la couverture de toute l'image ;
//  - plusieurs modèles de distorsion sont essayés et le meilleur est retenu sur des vues NON utilisées pour l'ajuster (anti sur-ajustement) ;
//  - les vues aberrantes sont retirées ; les deux caméras sont ensuite raffinées ensemble (stereoCalibrate) ;
//  - le résultat est contrôlé : erreur de reprojection, écart vertical après rectification, inversibilité de la distorsion jusqu'aux
//    bords, couverture de l'image, cohérence de l'échelle, puis vérification à la règle (la taille des carreaux saisie fixe l'échelle).
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdio>
#include <cstdlib>
#include <ctime>
#include <functional>
#include <mutex>
#include <numeric>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#include <opencv2/calib3d.hpp>
#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>

#include "calib.h"

namespace ar6cal {

struct Board {
  int cols = 9, rows = 6;     // coins INTÉRIEURS (une mire de 10 x 7 carreaux a 9 x 6 coins)
  double squareMm = 25.0;     // côté d'un carreau, en mm (mesuré : longueur de la grille / nombre de carreaux)
};

inline std::vector<cv::Point3f> BoardPoints(const Board& b) {
  std::vector<cv::Point3f> o;
  const float sq = (float)(b.squareMm / 1000.0);
  for (int r = 0; r < b.rows; r++)
    for (int c = 0; c < b.cols; c++) o.push_back(cv::Point3f(c * sq, r * sq, 0.f));
  return o;
}

// Orientation unique de la grille seulement si le nombre de coins diffère en parité (9 x 6 : oui, 8 x 6 : non).
inline bool BoardParityOk(const Board& b) { return (b.cols % 2) != (b.rows % 2); }

inline void ToGrayCopy(const cv::Mat& src, cv::Mat& dst) {
  if (src.channels() == 3) cv::cvtColor(src, dst, cv::COLOR_BGR2GRAY);
  else if (src.channels() == 4) cv::cvtColor(src, dst, cv::COLOR_BGRA2GRAY);
  else dst = src.clone();
}

inline bool DetectBoard(const cv::Mat& gray, const Board& b, std::vector<cv::Point2f>& out) {
  out.clear();
  try {
    std::vector<cv::Point2f> c;
    bool ok = cv::findChessboardCornersSB(gray, cv::Size(b.cols, b.rows), c, cv::CALIB_CB_NORMALIZE_IMAGE | cv::CALIB_CB_ACCURACY);
    if (!ok || (int)c.size() != b.cols * b.rows) return false;
    out.swap(c);
    return true;
  } catch (const cv::Exception&) {
    return false;
  }
}

// ---- Caractéristiques d'une vue de la mire (pour guider la prise) ---------------------------------------------------------------------
struct Feat {
  double px = 0, py = 0;      // centre de la mire dans l'image gauche, -1..1 (gauche->droite, haut->bas)
  double size = 0;            // largeur de la boîte englobante / largeur de l'image
  double yaw = 0, pitch = 0;  // inclinaison apparente : (hauteur droite - gauche)/somme, (largeur bas - haut)/somme
  double roll = 0;            // angle de la mire dans le plan de l'image, 0..90 degrés
  double minEdge = 0;         // distance du coin le plus proche d'un bord de l'image (px)
};

inline Feat ComputeFeat(const std::vector<cv::Point2f>& c, const Board& b, cv::Size img) {
  Feat f;
  float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
  for (const auto& p : c) { x0 = std::min(x0, p.x); x1 = std::max(x1, p.x); y0 = std::min(y0, p.y); y1 = std::max(y1, p.y); }
  f.px = ((x0 + x1) * 0.5 / img.width) * 2.0 - 1.0;
  f.py = ((y0 + y1) * 0.5 / img.height) * 2.0 - 1.0;
  f.size = (x1 - x0) / (double)img.width;
  f.minEdge = std::min(std::min((double)x0, (double)y0), std::min(img.width - 1.0 - x1, img.height - 1.0 - y1));
  // les 4 coins extérieurs, reconnus par leur position dans l'image (indépendant de l'ordre de la grille)
  int iTL = 0, iTR = 0, iBL = 0, iBR = 0;
  for (size_t i = 0; i < c.size(); i++) {
    if (c[i].x + c[i].y < c[iTL].x + c[iTL].y) iTL = (int)i;
    if (c[i].x + c[i].y > c[iBR].x + c[iBR].y) iBR = (int)i;
    if (c[i].x - c[i].y > c[iTR].x - c[iTR].y) iTR = (int)i;
    if (c[i].x - c[i].y < c[iBL].x - c[iBL].y) iBL = (int)i;
  }
  auto dist = [&](int a, int d) { return std::hypot((double)c[a].x - c[d].x, (double)c[a].y - c[d].y); };
  double top = dist(iTL, iTR), bottom = dist(iBL, iBR), left = dist(iTL, iBL), right = dist(iTR, iBR);
  if (left + right > 1e-6) f.yaw = (right - left) / (right + left);
  if (top + bottom > 1e-6) f.pitch = (bottom - top) / (bottom + top);
  // angle d'une rangée de la grille (coin 0 -> coin cols-1) par rapport à l'horizontale, replié sur 0..90
  const cv::Point2f d = c[b.cols - 1] - c[0];
  double a = std::fabs(std::atan2((double)d.y, (double)d.x)) * 180.0 / CV_PI;   // 0..180
  if (a > 90.0) a = 180.0 - a;
  f.roll = a;
  return f;
}

// ---- Objectifs de poses ----------------------------------------------------------------------------------------------------------------
constexpr int kGoalCount = 16;
// Ordre = ordre des conseils donnés à l'utilisateur (le premier objectif non atteint est proposé).
enum Goal {
  kGoalCenterNear = 0, kGoalCornerTL, kGoalCornerTR, kGoalCornerBR, kGoalCornerBL, kGoalCenterFar, kGoalYawLeft, kGoalYawRight,
  kGoalPitchUp, kGoalPitchDown, kGoalVeryNear, kGoalRoll, kGoalLeft, kGoalRight, kGoalTop, kGoalBottom
};

inline unsigned GoalsHit(const Feat& f) {
  unsigned m = 0;
  auto set = [&](int g, bool v) { if (v) m |= 1u << g; };
  const bool mid = std::fabs(f.px) < 0.35 && std::fabs(f.py) < 0.35;
  set(kGoalCenterNear, mid && f.size > 0.5);
  set(kGoalCenterFar, mid && f.size < 0.32);
  set(kGoalCornerTL, f.px < -0.3 && f.py < -0.3);
  set(kGoalCornerTR, f.px > 0.3 && f.py < -0.3);
  set(kGoalCornerBL, f.px < -0.3 && f.py > 0.3);
  set(kGoalCornerBR, f.px > 0.3 && f.py > 0.3);
  set(kGoalLeft, f.px < -0.35);
  set(kGoalRight, f.px > 0.35);
  set(kGoalTop, f.py < -0.35);
  set(kGoalBottom, f.py > 0.35);
  set(kGoalYawLeft, f.yaw < -0.08);
  set(kGoalYawRight, f.yaw > 0.08);
  set(kGoalPitchUp, f.pitch < -0.08);
  set(kGoalPitchDown, f.pitch > 0.08);
  set(kGoalVeryNear, f.size > 0.75);
  set(kGoalRoll, f.roll > 20.0 && f.roll < 70.0);
  return m;
}

inline int PopCount(unsigned v) { int n = 0; while (v) { n += (int)(v & 1u); v >>= 1; } return n; }

inline int NextGoal(unsigned done) {
  for (int g = 0; g < kGoalCount; g++) if (!(done & (1u << g))) return g;
  return -1;
}

// Distance entre deux vues (0 = identiques) : sert à ne pas accepter dix fois la même pose.
inline double FeatDistance(const Feat& a, const Feat& b) {
  return std::fabs(a.px - b.px) + std::fabs(a.py - b.py) + 1.5 * std::fabs(std::log(std::max(1e-3, a.size) / std::max(1e-3, b.size))) +
         4.0 * std::fabs(a.yaw - b.yaw) + 4.0 * std::fabs(a.pitch - b.pitch) + std::fabs(a.roll - b.roll) / 60.0;
}

struct View {
  std::vector<cv::Point2f> L, R;
  Feat f;
};

// ---- Calcul ------------------------------------------------------------------------------------------------------------------------------
struct SolveResult {
  bool ok = false;
  std::string err;
  StereoCalib calib;
  double rmsL = 0, rmsR = 0, rmsS = 0;   // erreurs de reprojection (px)
  double epi = 0, epiMax = 0;            // écart vertical gauche/droite après rectification (px)
  double baselineMm = 0, hfovDeg = 0;    // base entre les objectifs, champ de vision horizontal (image rectifiée)
  double coverage = 0;                   // part de l'image (grille 8 x 6) touchée par au moins un coin
  double roundTripPx = 0;                // erreur max d'inversion de la distorsion sur toute l'image
  double recon = 1;                      // taille d'un carreau reconstruite en 3D / taille saisie (cohérence interne, vaut ~1)
  double heldOut = 0;                    // erreur sur les vues non utilisées pour le choix du modèle (px)
  int used = 0, total = 0, score = 0;
  std::string model;
  std::vector<std::string> warnings;     // codes : few_views cov_low rms_high epi_high pp_far roundtrip swapped parity base_far
};

struct Cand { const char* name; int flags; int rank; int fallbackFor; };   // fallbackFor : variante « centre fixe », essayée seulement si le modèle libre indiqué échoue
inline const std::vector<Cand>& Candidates() {
  static const std::vector<Cand> c = {
      {"simple (k1,k2)", cv::CALIB_FIX_K3 | cv::CALIB_ZERO_TANGENT_DIST, 0, -1},
      {"standard (5)", 0, 1, -1},
      {"standard (5), centre fixe", cv::CALIB_FIX_PRINCIPAL_POINT, 2, 1},
      {"rationnel (8)", cv::CALIB_RATIONAL_MODEL, 3, -1},
      {"rationnel (8), centre fixe", cv::CALIB_RATIONAL_MODEL | cv::CALIB_FIX_PRINCIPAL_POINT, 4, 3},
  };
  return c;
}

inline cv::TermCriteria CalibCrit() { return cv::TermCriteria(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 100, 1e-8); }
// Choix du modèle : on compare des modèles, il n'est pas utile de pousser chaque ajustement jusqu'au bout (le modèle rationnel est lent à converger).
inline cv::TermCriteria CalibCritFast() { return cv::TermCriteria(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 30, 1e-6); }

inline cv::Mat GuessK(cv::Size size, double f) {
  return (cv::Mat_<double>(3, 3) << f, 0, (size.width - 1) * 0.5, 0, f, (size.height - 1) * 0.5, 0, 0, 1);
}

typedef std::vector<std::vector<cv::Point3f>> ObjSet;
typedef std::vector<std::vector<cv::Point2f>> ImgSet;

// Calibre UNE caméra. 'K' contient l'estimation initiale de la focale (centre = milieu de l'image). Renvoie l'erreur RMS (inf si le calcul échoue).
inline double CalibMono(const ObjSet& obj, const ImgSet& img, cv::Size size, int flags, const cv::Mat& K0, cv::Mat& K, cv::Mat& D,
                        std::vector<double>* perView, bool fast = false) {
  try {
    K = K0.clone();
    D = cv::Mat::zeros((flags & cv::CALIB_RATIONAL_MODEL) ? 8 : 5, 1, CV_64F);
    std::vector<cv::Mat> rv, tv;
    cv::Mat sdI, sdE, pve;
    double rms = cv::calibrateCamera(obj, img, size, K, D, rv, tv, sdI, sdE, pve,
                                     flags | cv::CALIB_USE_INTRINSIC_GUESS | cv::CALIB_FIX_ASPECT_RATIO, fast ? CalibCritFast() : CalibCrit());
    if (perView) {
      perView->clear();
      for (int i = 0; i < pve.rows; i++) perView->push_back(pve.at<double>(i, 0));
    }
    if (!(rms == rms) || rms > 1e6) return 1e9;
    return rms;
  } catch (const cv::Exception&) {
    return 1e9;
  }
}

// Erreur de reprojection (px) de vues NON utilisées pour ajuster (K, D) : seule la pose de chaque vue est recalculée.
inline double HeldOutRms(const ObjSet& obj, const ImgSet& img, const cv::Mat& K, const cv::Mat& D) {
  double se = 0;
  size_t n = 0;
  for (size_t i = 0; i < img.size(); i++) {
    try {
      cv::Mat rv, tv;
      if (!cv::solvePnP(obj[i], img[i], K, D, rv, tv, false, cv::SOLVEPNP_ITERATIVE)) return 1e9;
      std::vector<cv::Point2f> pr;
      cv::projectPoints(obj[i], rv, tv, K, D, pr);
      for (size_t k = 0; k < pr.size(); k++) {
        double dx = pr[k].x - img[i][k].x, dy = pr[k].y - img[i][k].y;
        se += dx * dx + dy * dy;
        n++;
      }
    } catch (const cv::Exception&) {
      return 1e9;
    }
  }
  return n ? std::sqrt(se / (double)n) : 1e9;
}

// La distorsion doit rester INVERSIBLE jusqu'aux bords : on défait puis on refait la distorsion de points répartis sur toute l'image.
// Un modèle trop libre (rationnel mal contraint) peut se replier près des coins ; l'image rectifiée y serait fausse sans qu'on le voie.
inline double RoundTripMaxPx(const cv::Mat& K, const cv::Mat& D, cv::Size size) {
  try {
    std::vector<cv::Point2f> src, und;
    for (int j = 0; j <= 12; j++)
      for (int i = 0; i <= 16; i++) src.push_back(cv::Point2f((float)(i * (size.width - 1) / 16.0), (float)(j * (size.height - 1) / 12.0)));
    cv::undistortPoints(src, und, K, D, cv::noArray(), cv::noArray(), cv::TermCriteria(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 60, 1e-9));
    std::vector<cv::Point3f> obj;
    for (const auto& p : und) obj.push_back(cv::Point3f(p.x, p.y, 1.f));
    std::vector<cv::Point2f> back;
    cv::projectPoints(obj, cv::Vec3d(0, 0, 0), cv::Vec3d(0, 0, 0), K, D, back);
    double mx = 0;
    for (size_t i = 0; i < src.size(); i++) {
      double e = std::hypot((double)back[i].x - src[i].x, (double)back[i].y - src[i].y);
      if (!(e == e)) return 1e9;
      mx = std::max(mx, e);
    }
    return mx;
  } catch (const cv::Exception&) {
    return 1e9;
  }
}

typedef std::function<void(const std::string& stage, double fraction)> Progress;

// Traces de mise au point : seulement si le test est compilé avec -DAR6CAL_DEBUG (aucun effet, aucun coût sinon).
inline void Dbg(const char* fmt, double a = 0, double b = 0, double c = 0, double d = 0) {
#ifdef AR6CAL_DEBUG
  std::fprintf(stderr, fmt, a, b, c, d);
#else
  (void)fmt; (void)a; (void)b; (void)c; (void)d;
#endif
}

inline double Median(std::vector<double> v) {
  if (v.empty()) return 0;
  std::sort(v.begin(), v.end());
  return v[v.size() / 2];
}

// Triangule les coins d'une vue avec la calibration (rectification stéréo) : distance (mm) du plan de la mire à la caméra gauche et côté d'un carreau.
inline bool BoardDistance(const StereoCalib& c, const std::vector<cv::Point2f>& L, const std::vector<cv::Point2f>& R, const Board& b,
                          double& zMm, double& squareMmEst) {
  try {
    cv::Mat R1, R2, P1, P2, Q;
    cv::stereoRectify(c.K1, c.D1, c.K2, c.D2, c.size, c.R, c.T, R1, R2, P1, P2, Q, cv::CALIB_ZERO_DISPARITY, 0, c.size);
    std::vector<cv::Point2f> ul, ur;
    const cv::TermCriteria it(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 60, 1e-9);
    cv::undistortPoints(L, ul, c.K1, c.D1, R1, P1, it);
    cv::undistortPoints(R, ur, c.K2, c.D2, R2, P2, it);
    const double f = P1.at<double>(0, 0), cx = P1.at<double>(0, 2), cy = P1.at<double>(1, 2);
    const double B = -P2.at<double>(0, 3) / f;
    std::vector<cv::Point3d> X(ul.size());
    double zs = 0;
    int n = 0;
    for (size_t i = 0; i < ul.size(); i++) {
      double d = (double)ul[i].x - ur[i].x;
      if (d < 0.05) return false;
      double z = f * B / d;
      X[i] = cv::Point3d((ul[i].x - cx) * z / f, (ul[i].y - cy) * z / f, z);
      zs += z;
      n++;
    }
    zMm = n ? zs / n * 1000.0 : 0;
    // côté d'un carreau : distance entre les deux coins extrêmes de chaque rangée / (cols - 1). Entre voisins, le bruit de profondeur
    // (plusieurs mm à 1 m) gonflerait la mesure de quelques pour cent ; sur 200 mm il ne compte presque plus.
    double ds = 0;
    int m = 0;
    for (int r = 0; r < b.rows; r++) {
      const cv::Point3d a = X[r * b.cols], d2 = X[r * b.cols + b.cols - 1];
      ds += std::sqrt((a.x - d2.x) * (a.x - d2.x) + (a.y - d2.y) * (a.y - d2.y) + (a.z - d2.z) * (a.z - d2.z)) / (b.cols - 1);
      m++;
    }
    squareMmEst = m ? ds / m * 1000.0 : 0;
    return true;
  } catch (const cv::Exception&) {
    return false;
  }
}

inline std::string NowStamp() {
  std::time_t t = std::time(nullptr);
  std::tm tmv{};
#if defined(_WIN32)
  localtime_s(&tmv, &t);
#else
  localtime_r(&t, &tmv);
#endif
  char buf[40];
  std::strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M", &tmv);
  return buf;
}

// Calcule la calibration stéréo à partir des vues retenues. 'size' : résolution PAR OEIL du mode caméra utilisé.
inline void SolveStereo(const std::vector<View>& viewsIn, cv::Size size, const Board& board, int fps, Progress prog, SolveResult& out) {
  out = SolveResult();
  out.total = (int)viewsIn.size();
  const auto t0_ = std::chrono::steady_clock::now();
  auto P = [&](const char* s, double f) { Dbg("[%.1f s] ", std::chrono::duration<double>(std::chrono::steady_clock::now() - t0_).count()); Dbg("%.0f%%\n", f * 100); if (prog) prog(s, std::min(1.0, std::max(0.0, f))); };
  if (viewsIn.size() < 8) { out.err = "trop peu de vues (minimum 8)"; return; }

  const std::vector<cv::Point3f> bp = BoardPoints(board);
  std::vector<View> views = viewsIn;

  // ---- 0. focale de départ : calibrage pilote avec un modèle simple, centre fixe
  P("focale de depart", 0.02);
  auto build = [&](const std::vector<View>& vs, bool right, ObjSet& obj, ImgSet& img) {
    obj.clear(); img.clear();
    for (const auto& v : vs) { obj.push_back(bp); img.push_back(right ? v.R : v.L); }
  };
  ObjSet obj; ImgSet imgL, imgR;
  build(views, false, obj, imgL);
  build(views, true, obj, imgR);
  double bestF = 0, bestPilot = 1e18;
  cv::Mat Kp, Dp;
  for (double fr : {0.30, 0.42, 0.55, 0.75}) {
    double r = CalibMono(obj, imgL, size, cv::CALIB_FIX_K3 | cv::CALIB_ZERO_TANGENT_DIST | cv::CALIB_FIX_PRINCIPAL_POINT,
                         GuessK(size, fr * size.width), Kp, Dp, nullptr, true);
    if (r < bestPilot) { bestPilot = r; bestF = Kp.at<double>(0, 0); }
  }
  if (bestPilot > 50 || bestF <= 0) { out.err = "calcul de depart impossible (mire mal detectee ?)"; return; }
  const cv::Mat K0 = GuessK(size, bestF);

  // ---- 1. choix du modèle : erreur sur des vues NON utilisées (validation croisée à 4 groupes), distorsion inversible partout
  const auto& cands = Candidates();
  std::vector<double> score(cands.size(), 1e18), heldOut(cands.size(), 1e9), rtFull(cands.size(), 1e9);
  const int folds = std::min(4, std::max(2, (int)views.size() / 4));
  size_t best = 0;
  bool anyOk = false;
  for (size_t ci = 0; ci < cands.size(); ci++) {
    // Les modèles rationnels (8 coefficients) sont lents et ne servent qu'aux objectifs très déformés : si un modèle simple, inversible
    // jusqu'aux bords, explique déjà les coins à mieux que 0,3 px sur des vues non utilisées, on s'arrête là.
    if (cands[ci].rank >= 3 && anyOk && heldOut[best] < 0.30 && rtFull[best] < 1.0) break;
    // variante « centre optique fixe » : seulement si le modèle libre correspondant a échoué ou n'est pas inversible (couverture insuffisante)
    if (cands[ci].fallbackFor >= 0) {
      const size_t fb = (size_t)cands[ci].fallbackFor;
      if (score[fb] < 1e17 && rtFull[fb] < 1.0) continue;
    }
    double se = 0;
    int ok = 0;
    for (int fo = 0; fo < folds; fo++) {
      ObjSet otr, ote; ImgSet ltr, lte, rtr, rte;
      for (size_t i = 0; i < views.size(); i++) {
        if ((int)(i % folds) == fo) { ote.push_back(bp); lte.push_back(views[i].L); rte.push_back(views[i].R); }
        else { otr.push_back(bp); ltr.push_back(views[i].L); rtr.push_back(views[i].R); }
      }
      cv::Mat K1, D1, K2, D2;
      double a = CalibMono(otr, ltr, size, cands[ci].flags, K0, K1, D1, nullptr, true);
      double b = CalibMono(otr, rtr, size, cands[ci].flags, K0, K2, D2, nullptr, true);
      if (a < 1e8 && b < 1e8) {
        double ha = HeldOutRms(ote, lte, K1, D1), hb = HeldOutRms(ote, rte, K2, D2);
        Dbg("  modele %.0f pli %.0f : held-out %.3f/", (double)ci, (double)fo, ha);
        Dbg("%.3f\n", hb);
        if (ha < 1e8 && hb < 1e8) { se += 0.5 * (ha * ha + hb * hb); ok++; }
      }
      P("choix du modele de distorsion", 0.05 + 0.40 * ((double)ci + (double)(fo + 1) / folds) / (double)cands.size());
    }
    if (ok < folds) continue;
    heldOut[ci] = std::sqrt(se / folds);
    // ajustement sur TOUTES les vues : la distorsion doit rester inversible jusqu'aux bords de l'image (voir RoundTripMaxPx)
    cv::Mat K1, D1, K2, D2;
    double fa = CalibMono(obj, imgL, size, cands[ci].flags, K0, K1, D1, nullptr, true);
    double fb = CalibMono(obj, imgR, size, cands[ci].flags, K0, K2, D2, nullptr, true);
    if (fa > 1e8 || fb > 1e8) continue;
    rtFull[ci] = std::max(RoundTripMaxPx(K1, D1, size), RoundTripMaxPx(K2, D2, size));
    const double pen = rtFull[ci] < 1.0 ? 1.0 : 1.0 + std::min(0.6, 0.15 + rtFull[ci] / 100.0);
    score[ci] = heldOut[ci] * (1.0 + 0.02 * cands[ci].rank) * pen;   // à égalité, le modèle le plus simple (et inversible) l'emporte
    Dbg("  modele %.0f : held-out %.3f, inversion max %.2f px, score %.3f\n", (double)ci, heldOut[ci], rtFull[ci], score[ci]);
    if (!anyOk || score[ci] < score[best]) best = ci;
    anyOk = true;
  }
  if (!anyOk) { out.err = "aucun modele de distorsion stable avec ces vues : refais des vues plus variees"; return; }
  const int flags = cands[best].flags;
  out.model = cands[best].name;
  out.heldOut = heldOut[best];

  // ---- 2. calibrage final de chaque caméra, retrait des vues aberrantes
  cv::Mat K1, D1, K2, D2;
  std::vector<double> eL, eR;
  for (int pass = 0; pass < 3; pass++) {
    P("calibrage des deux cameras", 0.48 + 0.08 * pass);
    build(views, false, obj, imgL);
    build(views, true, obj, imgR);
    double a = CalibMono(obj, imgL, size, flags, K0, K1, D1, &eL);
    double b = CalibMono(obj, imgR, size, flags, K0, K2, D2, &eR);
    if (a > 1e8 || b > 1e8) { out.err = "le calibrage d'une camera a echoue"; return; }
    out.rmsL = a; out.rmsR = b;
    std::vector<double> ve;
    for (size_t i = 0; i < views.size(); i++) ve.push_back(std::max(eL[i], eR[i]));
    const double lim = std::max(2.5 * Median(ve), 0.7);
    std::vector<View> keep;
    for (size_t i = 0; i < views.size(); i++) if (ve[i] <= lim) keep.push_back(views[i]);
    if (keep.size() == views.size() || keep.size() < 8) break;
    views.swap(keep);
  }
  out.used = (int)views.size();

  // ---- 3. calibrage stéréo : positions relatives d'abord (intrinsèques figées), puis tout ensemble
  P("calibrage stereo", 0.74);
  build(views, false, obj, imgL);
  build(views, true, obj, imgR);
  cv::Mat R, T, E, F, pve;
  double rs1 = 1e9;
  try {
    rs1 = cv::stereoCalibrate(obj, imgL, imgR, K1, D1, K2, D2, size, R, T, E, F, pve, cv::CALIB_FIX_INTRINSIC, CalibCrit());
  } catch (const cv::Exception& e) {
    out.err = std::string("calibrage stereo : ") + e.what();
    return;
  }
  cv::Mat K1b = K1.clone(), D1b = D1.clone(), K2b = K2.clone(), D2b = D2.clone(), Rb = R.clone(), Tb = T.clone();
  double rs2 = 1e9;
  try {
    rs2 = cv::stereoCalibrate(obj, imgL, imgR, K1b, D1b, K2b, D2b, size, Rb, Tb, E, F, pve,
                              flags | cv::CALIB_USE_INTRINSIC_GUESS | cv::CALIB_FIX_ASPECT_RATIO, CalibCrit());
  } catch (const cv::Exception&) {
    rs2 = 1e9;
  }
  P("calibrage stereo", 0.88);
  if (rs2 <= rs1 && std::max(RoundTripMaxPx(K1b, D1b, size), RoundTripMaxPx(K2b, D2b, size)) <= std::max(1.0, RoundTripMaxPx(K1, D1, size) * 1.2)) {
    K1 = K1b; D1 = D1b; K2 = K2b; D2 = D2b; R = Rb; T = Tb;
    out.rmsS = rs2;
  } else {
    out.rmsS = rs1;
  }

  // ---- 4. contrôles du résultat
  StereoCalib c;
  c.size = size;
  c.K1 = K1; c.D1 = D1.reshape(1, 1).clone(); c.K2 = K2; c.D2 = D2.reshape(1, 1).clone();
  c.R = R; c.T = T.reshape(1, 3).clone();
  c.source = "ar6dof"; c.model = out.model; c.created = NowStamp(); c.fps = fps; c.views = out.used;
  c.rmsL = out.rmsL; c.rmsR = out.rmsR; c.rmsStereo = out.rmsS; c.squareMm = board.squareMm;
  c.files = NativeCalibName();
  out.baselineMm = cv::norm(c.T) * 1000.0;

  P("controle de la qualite", 0.93);
  out.roundTripPx = std::max(RoundTripMaxPx(c.K1, c.D1, size), RoundTripMaxPx(c.K2, c.D2, size));
  try {
    cv::Mat R1, R2, P1, P2, Q;
    cv::stereoRectify(c.K1, c.D1, c.K2, c.D2, size, c.R, c.T, R1, R2, P1, P2, Q, cv::CALIB_ZERO_DISPARITY, 0, size);
    out.hfovDeg = 2.0 * std::atan(size.width * 0.5 / P1.at<double>(0, 0)) * 180.0 / CV_PI;
    const cv::TermCriteria it(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 60, 1e-9);
    double se = 0, mx = 0;
    size_t n = 0;
    std::vector<double> ratios;
    for (const auto& v : views) {
      std::vector<cv::Point2f> ul, ur;
      cv::undistortPoints(v.L, ul, c.K1, c.D1, R1, P1, it);
      cv::undistortPoints(v.R, ur, c.K2, c.D2, R2, P2, it);
      for (size_t i = 0; i < ul.size(); i++) {
        double dy = std::fabs((double)ul[i].y - ur[i].y);
        se += dy;
        mx = std::max(mx, dy);
        n++;
      }
      double z = 0, sq = 0;
      if (BoardDistance(c, v.L, v.R, board, z, sq) && sq > 0) ratios.push_back(sq / board.squareMm);
    }
    out.epi = n ? se / (double)n : 0;
    out.epiMax = mx;
    out.recon = ratios.empty() ? 1.0 : Median(ratios);
  } catch (const cv::Exception& e) {
    out.err = std::string("controle de la rectification : ") + e.what();
    return;
  }
  c.epiPx = out.epi;

  // couverture : grille 8 x 6 de l'image gauche
  {
    std::vector<char> cell(48, 0);
    for (const auto& v : views)
      for (const auto& p : v.L) {
        int gx = std::min(7, std::max(0, (int)(p.x / size.width * 8.0))), gy = std::min(5, std::max(0, (int)(p.y / size.height * 6.0)));
        cell[gy * 8 + gx] = 1;
      }
    out.coverage = std::accumulate(cell.begin(), cell.end(), 0) / 48.0;
  }

  const double rmsAvg = 0.5 * (out.rmsL + out.rmsR);
  if (out.used < 12) out.warnings.push_back("few_views");
  if (out.coverage < 0.7) out.warnings.push_back("cov_low");
  if (rmsAvg > 0.7) out.warnings.push_back("rms_high");
  if (out.epi > 0.6) out.warnings.push_back("epi_high");
  if (out.roundTripPx > 0.5) out.warnings.push_back("roundtrip");
  if (c.T.at<double>(0, 0) > 0) out.warnings.push_back("swapped");
  for (const cv::Mat* K : {&c.K1, &c.K2}) {
    double ox = std::fabs((*K).at<double>(0, 2) - (size.width - 1) * 0.5) / size.width;
    double oy = std::fabs((*K).at<double>(1, 2) - (size.height - 1) * 0.5) / size.height;
    if (ox > 0.08 || oy > 0.08) { out.warnings.push_back("pp_far"); break; }
  }
  if (!BoardParityOk(board)) out.warnings.push_back("parity");

  auto clamp01 = [](double v) { return std::min(1.0, std::max(0.0, v)); };
  double sc = 100.0 - 35.0 * clamp01((rmsAvg - 0.25) / 0.75) - 25.0 * clamp01((out.epi - 0.2) / 0.8) -
              15.0 * clamp01((0.85 - out.coverage) / 0.4) - 10.0 * clamp01((16.0 - out.used) / 8.0) - 15.0 * clamp01(out.roundTripPx / 1.0);
  out.score = (int)std::lround(std::min(100.0, std::max(0.0, sc)));
  out.calib = c;
  out.ok = true;
  P("termine", 1.0);
}

// ---- Session de capture (thread de détection + thread de calcul) -------------------------------------------------------------------------
// Un nombre non fini (nan, inf) rendrait le JSON illisible pour Unity : on écrit 0.
inline double Fin(double v) { return std::isfinite(v) ? v : 0.0; }

inline std::string JEsc(const std::string& s) {
  std::string o;
  for (char ch : s) {
    if (ch == '"' || ch == '\\') { o += '\\'; o += ch; }
    else if ((unsigned char)ch < 0x20) o += ' ';
    else o += ch;
  }
  return o;
}

class Session {
 public:
  enum State { kIdle = 0, kCapture, kCompute, kDone, kError };

  ~Session() { Shutdown(); }

  // Démarre une capture. 'target' : nombre de vues visé (la barre de progression).
  void Start(const Board& b, int fps, int target = 18, double baselineHintMm = 0) {
    Shutdown();
    std::lock_guard<std::mutex> l(m_);
    board_ = b; fps_ = fps; target_ = std::max(10, std::min(30, target)); baselineHint_ = baselineHintMm;
    views_.clear(); goalsDone_ = 0; acceptSeq_ = 0; msg_ = BoardParityOk(b) ? "show" : "parity";
    live_.clear(); foundL_ = foundR_ = false; stillness_ = 0; swappedCount_ = 0; stillCount_ = 0; havePrev_ = false;
    img_ = cv::Size(); result_ = SolveResult(); err_.clear(); stage_.clear(); cprogress_ = 0; verifySeq_ = 0; verifyEst_ = verifyRatio_ = 0;
    lastL_.clear(); lastR_.clear(); lastDetT_ = -1e9; applied_ = false;
    state_ = kCapture;
    stop_ = false; havePend_ = false; busy_ = false; computeReq_ = false; lastFeed_ = -1e9;
    worker_ = std::thread([this] { Loop(); });
  }

  void Cancel() { Shutdown(); std::lock_guard<std::mutex> l(m_); state_ = kIdle; msg_ = "idle"; }

  void RequestCompute() {
    std::lock_guard<std::mutex> l(m_);
    if (state_ == kCapture && (int)views_.size() >= 8) { computeReq_ = true; cv_.notify_one(); }
  }

  State state() const { std::lock_guard<std::mutex> l(m_); return state_; }
  bool WantsFrames() const { std::lock_guard<std::mutex> l(m_); return state_ == kCapture || state_ == kDone; }

  // À appeler à chaque image (rapide : au plus une copie toutes les 120 ms, et seulement si le thread de détection est libre).
  void Feed(const cv::Mat& left, const cv::Mat& right, double now) {
    if (busy_ || now - lastFeed_ < 0.12) return;
    {
      std::lock_guard<std::mutex> l(m_);
      if (state_ != kCapture && state_ != kDone) return;
    }
    lastFeed_ = now;
    cv::Mat gl, gr;
    ToGrayCopy(left, gl);
    ToGrayCopy(right, gr);
    {
      std::lock_guard<std::mutex> l(m_);
      pl_ = gl; pr_ = gr; pendT_ = now; havePend_ = true; busy_ = true;
    }
    cv_.notify_one();
  }

  SolveResult Result() const { std::lock_guard<std::mutex> l(m_); return result_; }

  // Applique la correction d'échelle (rapport mesuré / estimé de la vérification à la règle) au résultat.
  void ApplyScale(double ratio) {
    std::lock_guard<std::mutex> l(m_);
    if (!result_.ok || ratio < 0.3 || ratio > 3.0) return;
    result_.calib.T = result_.calib.T * ratio;
    result_.calib.scaleCorr *= ratio;
    result_.baselineMm = cv::norm(result_.calib.T) * 1000.0;
    verifyRatio_ = 1.0;
  }

  void MarkApplied() { std::lock_guard<std::mutex> l(m_); applied_ = true; version_++; }

  // Les yeux sont-ils echanges dans la sonde ? (Unity en a besoin pour savoir quelle moitie de l'image brute est la camera « gauche ».)
  void SetSwapped(bool s) { if (swapEyes_.load() != s) { swapEyes_ = s; version_++; } }

  // Vérification à la règle : distance (mm) mesurée entre la vitre des objectifs et la mire (tenue de face). Utilise la dernière détection.
  bool Verify(double measuredMm, std::string& err) {
    std::lock_guard<std::mutex> l(m_);
    if (state_ != kDone || !result_.ok) { err = "pas de calibration a verifier"; return false; }
    if (lastL_.empty() || lastR_.empty() || now_() - lastDetT_ > 1.5) { err = "la mire n'est pas visible dans les deux cameras"; return false; }
    double z = 0, sq = 0;
    if (!BoardDistance(result_.calib, lastL_, lastR_, board_, z, sq) || z <= 0) { err = "mesure impossible (mire trop de travers ?)"; return false; }
    verifyEst_ = z;
    verifyRatio_ = measuredMm / z;
    verifySeq_++;
    return true;
  }

  // Calibration prête à être enregistrée ?
  bool HasResult() const { std::lock_guard<std::mutex> l(m_); return state_ == kDone && result_.ok; }

  // État complet en JSON (lu par Unity).
  std::string StatusJson() const {
    std::lock_guard<std::mutex> l(m_);
    static const char* names[] = {"idle", "capture", "compute", "done", "error"};
    std::ostringstream o;
    o.setf(std::ios::fixed);
    o.precision(3);
    const int gd = PopCount(goalsDone_);
    double prog = 0;
    if (state_ == kCapture) prog = 0.6 * gd / (double)kGoalCount + 0.4 * std::min(1.0, views_.size() / (double)target_);
    else if (state_ == kCompute || state_ == kDone) prog = 1.0;
    o << "{\"state\":\"" << names[state_] << "\",\"msg\":\"" << JEsc(msg_) << "\",\"goal\":" << NextGoal(goalsDone_)
      << ",\"goalsDone\":" << goalsDone_ << ",\"goalCount\":" << kGoalCount << ",\"views\":" << views_.size() << ",\"target\":" << target_
      << ",\"progress\":" << prog << ",\"foundL\":" << (foundL_ ? "true" : "false") << ",\"foundR\":" << (foundR_ ? "true" : "false")
      << ",\"stillness\":" << Fin(stillness_) << ",\"acceptSeq\":" << acceptSeq_ << ",\"imgW\":" << img_.width << ",\"imgH\":" << img_.height << ",\"swapEyes\":" << (swapEyes_.load() ? "true" : "false")
      << ",\"cols\":" << board_.cols << ",\"rows\":" << board_.rows << ",\"squareMm\":" << board_.squareMm
      << ",\"stage\":\"" << JEsc(stage_) << "\",\"cprogress\":" << cprogress_ << ",\"error\":\"" << JEsc(err_) << "\",\"applied\":"
      << (applied_ ? "true" : "false") << ",\"corners\":[";
    for (size_t i = 0; i < live_.size(); i++) o << (i ? "," : "") << live_[i].x << "," << live_[i].y;
    o << "],\"verify\":{\"seq\":" << verifySeq_ << ",\"estMm\":" << Fin(verifyEst_) << ",\"ratio\":" << Fin(verifyRatio_) << "}";
    o.precision(4);
    o << ",\"result\":{\"ok\":" << (result_.ok ? "true" : "false") << ",\"rmsL\":" << Fin(result_.rmsL) << ",\"rmsR\":" << Fin(result_.rmsR)
      << ",\"rmsS\":" << Fin(result_.rmsS) << ",\"epi\":" << Fin(result_.epi) << ",\"epiMax\":" << Fin(result_.epiMax)
      << ",\"baselineMm\":" << Fin(result_.baselineMm) << ",\"hfov\":" << Fin(result_.hfovDeg) << ",\"coverage\":" << Fin(result_.coverage)
      << ",\"roundTrip\":" << Fin(result_.roundTripPx) << ",\"recon\":" << Fin(result_.recon) << ",\"heldOut\":" << Fin(result_.heldOut)
      << ",\"used\":" << result_.used << ",\"total\":" << result_.total << ",\"score\":" << result_.score << ",\"model\":\""
      << JEsc(result_.model) << "\",\"scaleCorr\":" << Fin(result_.calib.scaleCorr) << ",\"baselineHint\":" << Fin(baselineHint_) << ",\"warnings\":[";
    for (size_t i = 0; i < result_.warnings.size(); i++) o << (i ? "," : "") << "\"" << JEsc(result_.warnings[i]) << "\"";
    o << "]}}";
    return o.str();
  }

  // Compteur qui change à chaque modification visible (permet de n'écrire le fichier d'état que quand il faut).
  unsigned Version() const { return version_.load(); }

 private:
  static double now_() {
    using namespace std::chrono;
    return duration<double>(steady_clock::now().time_since_epoch()).count();
  }

  void Shutdown() {
    {
      std::lock_guard<std::mutex> l(m_);
      stop_ = true;
    }
    cv_.notify_all();
    if (worker_.joinable()) worker_.join();
  }

  void Bump() { version_++; }

  void Loop() {
    for (;;) {
      cv::Mat gl, gr;
      double t = 0;
      bool doCompute = false;
      {
        std::unique_lock<std::mutex> l(m_);
        cv_.wait(l, [&] { return stop_ || havePend_ || computeReq_; });
        if (stop_) return;
        if (computeReq_) { computeReq_ = false; doCompute = true; }
        else { gl = pl_; gr = pr_; t = pendT_; havePend_ = false; }
      }
      if (doCompute) { RunSolve(); }
      else { ProcessPair(gl, gr, t); }
      {
        std::lock_guard<std::mutex> l(m_);
        busy_ = false;
      }
    }
  }

  void SetMsg(const char* m) {
    std::lock_guard<std::mutex> l(m_);
    if (msg_ != m) { msg_ = m; Bump(); }
  }

  void ProcessPair(const cv::Mat& gl, const cv::Mat& gr, double now) {
    Board b;
    State st;
    {
      std::lock_guard<std::mutex> l(m_);
      b = board_; st = state_;
      if (st != kCapture && st != kDone) return;
      if (img_.width == 0) img_ = gl.size();
      if (gl.size() != img_ || gr.size() != img_) return;
    }
    std::vector<cv::Point2f> cl, cr;
    const bool fl = DetectBoard(gl, b, cl);
    const bool fr = fl && DetectBoard(gr, b, cr);
    {
      std::lock_guard<std::mutex> l(m_);
      foundL_ = fl; foundR_ = fr;
      live_.clear();
      if (fl) for (const auto& p : cl) live_.push_back(cv::Point2f((float)(p.x / img_.width), (float)(p.y / img_.height)));
      if (fl && fr) { lastL_ = cl; lastR_ = cr; lastDetT_ = now_(); }
      Bump();
    }
    if (st == kDone) return;   // vérification à la règle : on suit seulement la mire
    if (!fl) { SetMsg(BoardParityOk(b) ? "show" : "parity"); havePrev_ = false; stillCount_ = 0; return; }
    if (!fr) { SetMsg("showR"); havePrev_ = false; stillCount_ = 0; return; }

    // cohérence gauche/droite : même sens de lecture de la grille, et disparité positive (la mire est plus à gauche dans l'image droite)
    const cv::Point2f dl = cl[b.cols - 1] - cl[0], dr = cr[b.cols - 1] - cr[0];
    if (dl.x * dr.x + dl.y * dr.y <= 0) { SetMsg("order"); return; }
    double disp = 0;
    for (size_t i = 0; i < cl.size(); i++) disp += (double)cl[i].x - cr[i].x;
    disp /= (double)cl.size();
    if (disp < 0) {
      if (++swappedCount_ >= 3) SetMsg("swapped");
      return;
    }
    swappedCount_ = 0;

    const Feat f = ComputeFeat(cl, b, img_);
    if (f.minEdge < 6.0) { SetMsg("edge"); return; }
    if (f.size < 0.12) { SetMsg("small"); return; }

    // immobile ? (déplacement moyen des coins depuis la détection précédente, en pixels d'une image de 800 px de large)
    double move = 1e9;
    if (havePrev_ && prevL_.size() == cl.size() && now - prevT_ < 1.0) {
      double s = 0;
      for (size_t i = 0; i < cl.size(); i++) s += std::hypot((double)cl[i].x - prevL_[i].x, (double)cl[i].y - prevL_[i].y);
      move = s / (double)cl.size() * 800.0 / img_.width;
    }
    prevL_ = cl; prevT_ = now; havePrev_ = true;
    {
      std::lock_guard<std::mutex> l(m_);
      stillness_ = move > 1e8 ? 0.0 : std::max(0.0, std::min(1.0, 1.0 - move / 6.0));
    }
    if (move > 2.0) { stillCount_ = 0; SetMsg("still"); return; }
    if (++stillCount_ < 2) { SetMsg("still"); return; }

    // nouveauté : un objectif encore non atteint, ou une pose assez différente de toutes les vues déjà prises
    std::lock_guard<std::mutex> l(m_);
    double dmin = 1e9;
    for (const auto& v : views_) dmin = std::min(dmin, FeatDistance(v.f, f));
    const unsigned hit = GoalsHit(f);
    const bool newGoal = (hit & ~goalsDone_) != 0;
    if ((newGoal && dmin < 0.08) || (!newGoal && dmin < 0.30)) { msg_ = "move"; Bump(); return; }
    View v;
    v.L = cl; v.R = cr; v.f = f;
    views_.push_back(v);
    goalsDone_ |= hit;
    acceptSeq_++;
    stillCount_ = 0;
    msg_ = "ok";
    Bump();
    const int gd = PopCount(goalsDone_);
    if (((int)views_.size() >= 14 && gd >= 13) || (int)views_.size() >= 26) computeReq_ = true;
  }

  void RunSolve() {
    std::vector<View> vs;
    Board b;
    cv::Size sz;
    int fps;
    {
      std::lock_guard<std::mutex> l(m_);
      state_ = kCompute; vs = views_; b = board_; sz = img_; fps = fps_;
      stage_ = "preparation"; cprogress_ = 0; msg_ = "compute";
      Bump();
    }
    SolveResult r;
    SolveStereo(vs, sz, b, fps, [this](const std::string& s, double f) {
      std::lock_guard<std::mutex> l(m_);
      stage_ = s; cprogress_ = f; Bump();
    }, r);
    std::lock_guard<std::mutex> l(m_);
    if (baselineHint_ > 1.0 && r.ok && std::fabs(r.baselineMm / baselineHint_ - 1.0) > 0.12) r.warnings.push_back("base_far");
    result_ = r;
    if (r.ok) { state_ = kDone; msg_ = "done"; err_.clear(); }
    else { state_ = kError; msg_ = "error"; err_ = r.err; }
    Bump();
  }

  mutable std::mutex m_;
  std::condition_variable cv_;
  std::thread worker_;
  std::atomic<bool> busy_{false};
  std::atomic<unsigned> version_{0};
  std::atomic<bool> swapEyes_{false};
  double lastFeed_ = -1e9;
  // protégés par m_
  State state_ = kIdle;
  bool stop_ = false, havePend_ = false, computeReq_ = false, applied_ = false;
  cv::Mat pl_, pr_;
  double pendT_ = 0;
  Board board_;
  int fps_ = 0, target_ = 18;
  double baselineHint_ = 0;
  std::vector<View> views_;
  unsigned goalsDone_ = 0;
  unsigned acceptSeq_ = 0, verifySeq_ = 0;
  std::string msg_ = "idle", stage_, err_;
  std::vector<cv::Point2f> live_, lastL_, lastR_;
  double lastDetT_ = -1e9;
  bool foundL_ = false, foundR_ = false;
  double stillness_ = 0, cprogress_ = 0, verifyEst_ = 0, verifyRatio_ = 0;
  cv::Size img_;
  SolveResult result_;
  // utilisés seulement par le thread de détection
  std::vector<cv::Point2f> prevL_;
  double prevT_ = 0;
  bool havePrev_ = false;
  int stillCount_ = 0, swappedCount_ = 0;
};

}  // namespace ar6cal
