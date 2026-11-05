// calib_view.h : tout ce que la page « Calibration » d'ar6dof_app a besoin de savoir, SANS aucune dépendance à Windows (testable sous Linux) :
//  - lecture de calib_status.json / calib_info.json écrits par ar6dof_probe (voir calib_session.h, StatusJson, et main.cpp, WriteCalibInfo)
//  - les 16 poses à montrer (textes français / anglais, position de la mire « fantôme »)
//  - projection d'une mire en perspective (pour dessiner les conseils)
//  - choix de l'étape affichée, géométrie de l'aperçu, messages
// Les textes sont des chaînes larges (la fenêtre est en Unicode) ; les accents sont permis.
#pragma once
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

namespace calview {

// ------------------------------------------------------------------ petit lecteur JSON (les fichiers sont écrits par nous, à plat, sans surprise)
inline const char* FindKey(const std::string& s, const char* key, size_t from = 0) {
  const std::string pat = std::string("\"") + key + "\":";
  const size_t p = s.find(pat, from);
  if (p == std::string::npos) return nullptr;
  const char* q = s.c_str() + p + pat.size();
  while (*q == ' ') q++;
  return q;
}
inline double JNum(const std::string& s, const char* key, double def = 0, size_t from = 0) {
  const char* q = FindKey(s, key, from);
  if (!q) return def;
  char* e = nullptr;
  const double v = std::strtod(q, &e);
  return (e == q || !std::isfinite(v)) ? def : v;
}
inline bool JBool(const std::string& s, const char* key, bool def = false, size_t from = 0) {
  const char* q = FindKey(s, key, from);
  if (!q) return def;
  if (!std::strncmp(q, "true", 4)) return true;
  if (!std::strncmp(q, "false", 5)) return false;
  return def;
}
inline std::string JUnescape(const char* q, const char** endOut = nullptr) {   // q pointe sur le guillemet ouvrant
  std::string r;
  if (*q != '"') return r;
  q++;
  while (*q && *q != '"') {
    if (*q == '\\' && q[1]) {
      q++;
      r += (*q == 'n') ? '\n' : (*q == 't') ? '\t' : *q;
    } else {
      r += *q;
    }
    q++;
  }
  if (endOut) *endOut = *q == '"' ? q + 1 : q;
  return r;
}
inline std::string JStr(const std::string& s, const char* key, const std::string& def = "", size_t from = 0) {
  const char* q = FindKey(s, key, from);
  if (!q || *q != '"') return def;
  return JUnescape(q);
}
inline std::vector<float> JNumArr(const std::string& s, const char* key, size_t from = 0) {
  std::vector<float> v;
  const char* q = FindKey(s, key, from);
  if (!q || *q != '[') return v;
  q++;
  while (*q && *q != ']') {
    char* e = nullptr;
    const double x = std::strtod(q, &e);
    if (e == q) break;
    v.push_back((float)x);
    q = e;
    while (*q == ',' || *q == ' ') q++;
  }
  return v;
}
inline std::vector<std::string> JStrArr(const std::string& s, const char* key, size_t from = 0) {
  std::vector<std::string> v;
  const char* q = FindKey(s, key, from);
  if (!q || *q != '[') return v;
  q++;
  while (*q && *q != ']') {
    if (*q == '"') {
      const char* e = q;
      v.push_back(JUnescape(q, &e));
      q = e;
    } else {
      q++;
    }
  }
  return v;
}

// ------------------------------------------------------------------ état de la calibration (calib_status.json)
struct Result {
  bool ok = false;
  double rmsL = 0, rmsR = 0, rmsS = 0, epi = 0, epiMax = 0, baselineMm = 0, hfov = 0, coverage = 0, roundTrip = 0, recon = 0, heldOut = 0, scaleCorr = 1, baselineHint = 0;
  int used = 0, total = 0, score = 0;
  std::string model;
  std::vector<std::string> warnings;
};

struct Status {
  std::string state = "idle", msg = "idle", stage, error;
  int goal = -1, goalsDone = 0, goalCount = 16, views = 0, target = 18, acceptSeq = 0, imgW = 0, imgH = 0, cols = 0, rows = 0;
  double progress = 0, stillness = 0, squareMm = 0, cprogress = 0;
  bool foundL = false, foundR = false, swapEyes = false, applied = false;
  std::vector<float> corners;   // x y x y... normalisés 0..1 dans l'image de l'œil « gauche » de la sonde
  int verifySeq = 0;
  double verifyEstMm = 0, verifyRatio = 0;
  Result result;
  bool valid = false;
};

inline bool ParseStatus(const std::string& t, Status& s) {
  if (t.find("\"state\":") == std::string::npos) return false;
  Status n;
  n.state = JStr(t, "state", "idle");
  n.msg = JStr(t, "msg", "idle");
  n.stage = JStr(t, "stage");
  n.error = JStr(t, "error");
  n.goal = (int)JNum(t, "goal", -1);
  n.goalsDone = (int)JNum(t, "goalsDone", 0);
  n.goalCount = (int)JNum(t, "goalCount", 16);
  n.views = (int)JNum(t, "views", 0);
  n.target = (int)JNum(t, "target", 18);
  n.acceptSeq = (int)JNum(t, "acceptSeq", 0);
  n.imgW = (int)JNum(t, "imgW", 0);
  n.imgH = (int)JNum(t, "imgH", 0);
  n.cols = (int)JNum(t, "cols", 0);
  n.rows = (int)JNum(t, "rows", 0);
  n.progress = JNum(t, "progress", 0);
  n.stillness = JNum(t, "stillness", 0);
  n.squareMm = JNum(t, "squareMm", 0);
  n.cprogress = JNum(t, "cprogress", 0);
  n.foundL = JBool(t, "foundL");
  n.foundR = JBool(t, "foundR");
  n.swapEyes = JBool(t, "swapEyes");
  n.applied = JBool(t, "applied");
  n.corners = JNumArr(t, "corners");
  const size_t pv = t.find("\"verify\":");
  if (pv != std::string::npos) {
    n.verifySeq = (int)JNum(t, "seq", 0, pv);
    n.verifyEstMm = JNum(t, "estMm", 0, pv);
    n.verifyRatio = JNum(t, "ratio", 0, pv);
  }
  const size_t pr = t.find("\"result\":");
  if (pr != std::string::npos) {
    Result& r = n.result;
    r.ok = JBool(t, "ok", false, pr);
    r.rmsL = JNum(t, "rmsL", 0, pr);
    r.rmsR = JNum(t, "rmsR", 0, pr);
    r.rmsS = JNum(t, "rmsS", 0, pr);
    r.epi = JNum(t, "epi", 0, pr);
    r.epiMax = JNum(t, "epiMax", 0, pr);
    r.baselineMm = JNum(t, "baselineMm", 0, pr);
    r.hfov = JNum(t, "hfov", 0, pr);
    r.coverage = JNum(t, "coverage", 0, pr);
    r.roundTrip = JNum(t, "roundTrip", 0, pr);
    r.recon = JNum(t, "recon", 0, pr);
    r.heldOut = JNum(t, "heldOut", 0, pr);
    r.scaleCorr = JNum(t, "scaleCorr", 1, pr);
    r.baselineHint = JNum(t, "baselineHint", 0, pr);
    r.used = (int)JNum(t, "used", 0, pr);
    r.total = (int)JNum(t, "total", 0, pr);
    r.score = (int)JNum(t, "score", 0, pr);
    r.model = JStr(t, "model", "", pr);
    r.warnings = JStrArr(t, "warnings", pr);
  }
  n.valid = true;
  s = n;
  return true;
}

// ------------------------------------------------------------------ ce que le suivi utilise (calib_info.json)
struct Info {
  std::string source, model, created, error, zoomSource = "none";
  int calibW = 0, calibH = 0, runW = 0, runH = 0, views = 0, fps = 0, runFps = 0, zoomN = 0;
  double scale = 1, baselineMm = 0, rms = 0, epi = 0, squareMm = 0, scaleCorr = 1, hfov = 0, zoom = 1, zoomMotion = 0, zoomSpread = 0, zoomResidual = 0;
  bool mismatch = false, zoomAuto = true, zoomConfirmed = false;
  bool valid = false;
};

inline bool ParseInfo(const std::string& t, Info& i) {
  if (t.find("\"source\":") == std::string::npos) return false;
  Info n;
  n.source = JStr(t, "source");
  n.model = JStr(t, "model");
  n.created = JStr(t, "created");
  n.error = JStr(t, "error");
  n.calibW = (int)JNum(t, "calibW");
  n.calibH = (int)JNum(t, "calibH");
  n.runW = (int)JNum(t, "runW");
  n.runH = (int)JNum(t, "runH");
  n.views = (int)JNum(t, "views");
  n.fps = (int)JNum(t, "fps");
  n.runFps = (int)JNum(t, "runFps");
  n.scale = JNum(t, "scale", 1);
  n.baselineMm = JNum(t, "baselineMm");
  n.rms = JNum(t, "rms");
  n.epi = JNum(t, "epi");
  n.squareMm = JNum(t, "squareMm");
  n.scaleCorr = JNum(t, "scaleCorr", 1);
  n.hfov = JNum(t, "hfov");
  n.mismatch = JBool(t, "mismatch");
  n.zoom = JNum(t, "zoom", 1);
  n.zoomSource = JStr(t, "zoomSource", "none");
  n.zoomAuto = JBool(t, "zoomAuto", true);
  n.zoomConfirmed = JBool(t, "zoomConfirmed");
  n.zoomN = (int)JNum(t, "zoomN");
  n.zoomMotion = JNum(t, "zoomMotion");
  n.zoomSpread = JNum(t, "zoomSpread");
  n.zoomResidual = JNum(t, "zoomResidual");
  n.valid = true;
  i = n;
  return true;
}

// ------------------------------------------------------------------ textes
struct Bi { const wchar_t* fr; const wchar_t* en; };
inline const wchar_t* Pick(const Bi& b, bool en) { return en ? b.en : b.fr; }

inline std::wstring Num(double v, int dec) {
  char b[48];
  std::snprintf(b, sizeof(b), "%.*f", dec, v);
  std::wstring w;
  for (const char* p = b; *p; p++) w += (wchar_t)*p;
  return w;
}

// Les 16 poses, dans l'ordre de l'énumération Goal de calib_session.h. px, py : position du centre (-1..1, py positif = vers le haut) ; size : largeur relative ;
// yaw : bord droit plus proche si positif ; pitch : bord du haut plus loin si positif ; roll : rotation dans le plan.
struct GoalInfo {
  Bi name, title, tip;
  double px, py, size, yaw, pitch, roll;
};

inline const GoalInfo* Goals(int* n) {
  static const GoalInfo g[] = {
    {{L"Centre près", L"Center, close"}, {L"Au centre, de près", L"In the center, close"},
     {L"Tiens la mire bien à plat au centre de l'image, assez près pour qu'elle en remplisse plus de la moitié de la largeur.",
      L"Hold the board flat in the center of the image, close enough to fill more than half of its width."}, 0, 0, 0.62, 0, 0, 0},
    {{L"Coin haut G", L"Top-left"}, {L"Dans le coin en haut à gauche", L"In the top-left corner"},
     {L"Place la mire de face dans le coin haut gauche. Les bords de l'image sont là où l'objectif déforme le plus : ils comptent beaucoup.",
      L"Place the board facing the camera in the top-left corner. The image edges are where the lens distorts most: they matter a lot."}, -0.55, 0.55, 0.34, 0, 0, 0},
    {{L"Coin haut D", L"Top-right"}, {L"Dans le coin en haut à droite", L"In the top-right corner"},
     {L"Place la mire de face dans le coin haut droit de l'image, sans que ses coins sortent de l'image.",
      L"Place the board facing the camera in the top-right corner, without any of its corners leaving the image."}, 0.55, 0.55, 0.34, 0, 0, 0},
    {{L"Coin bas D", L"Bottom-right"}, {L"Dans le coin en bas à droite", L"In the bottom-right corner"},
     {L"Place la mire de face dans le coin bas droit de l'image, sans que ses coins sortent de l'image.",
      L"Place the board facing the camera in the bottom-right corner, without any of its corners leaving the image."}, 0.55, -0.55, 0.34, 0, 0, 0},
    {{L"Coin bas G", L"Bottom-left"}, {L"Dans le coin en bas à gauche", L"In the bottom-left corner"},
     {L"Place la mire de face dans le coin bas gauche de l'image, sans que ses coins sortent de l'image.",
      L"Place the board facing the camera in the bottom-left corner, without any of its corners leaving the image."}, -0.55, -0.55, 0.34, 0, 0, 0},
    {{L"Centre loin", L"Center, far"}, {L"Au centre, de loin", L"In the center, far"},
     {L"Recule : la mire doit rester petite au centre (moins du tiers de la largeur). C'est ce qui donne la bonne mesure des distances.",
      L"Step back: the board must stay small in the center (under a third of the width). This is what gives correct distances."}, 0, 0, 0.24, 0, 0, 0},
    {{L"Gauche vers toi", L"Left toward you"}, {L"Côté gauche vers toi", L"Left side toward you"},
     {L"Tourne la mire autour de la verticale : son côté gauche (vu de la caméra) plus près de toi que le droit, de 30 à 45 degrés.",
      L"Turn the board around the vertical axis: its left side (seen from the camera) closer to you than the right, by 30 to 45 degrees."}, 0, 0, 0.46, -42, 0, 0},
    {{L"Droite vers toi", L"Right toward you"}, {L"Côté droit vers toi", L"Right side toward you"},
     {L"Tourne la mire autour de la verticale : son côté droit (vu de la caméra) plus près de toi que le gauche, de 30 à 45 degrés.",
      L"Turn the board around the vertical axis: its right side (seen from the camera) closer to you than the left, by 30 to 45 degrees."}, 0, 0, 0.46, 42, 0, 0},
    {{L"Haut vers toi", L"Top toward you"}, {L"Haut penché vers toi", L"Top tilted toward you"},
     {L"Penche la mire : le haut plus près de toi que le bas, de 30 à 45 degrés. Reste de face dans l'autre sens.",
      L"Tilt the board: the top closer to you than the bottom, by 30 to 45 degrees. Stay facing the camera the other way."}, 0, 0, 0.46, 0, -38, 0},
    {{L"Bas vers toi", L"Bottom toward you"}, {L"Bas penché vers toi", L"Bottom tilted toward you"},
     {L"Penche la mire : le bas plus près de toi que le haut, de 30 à 45 degrés. Reste de face dans l'autre sens.",
      L"Tilt the board: the bottom closer to you than the top, by 30 to 45 degrees. Stay facing the camera the other way."}, 0, 0, 0.46, 0, 38, 0},
    {{L"Très près", L"Very close"}, {L"Très près", L"Very close"},
     {L"Approche la mire jusqu'à ce qu'elle remplisse presque toute la largeur de l'image, en gardant tous ses coins visibles.",
      L"Bring the board close until it fills almost the whole width of the image, keeping all its corners visible."}, 0, 0, 0.88, 0, 0, 0},
    {{L"En diagonale", L"Diagonal"}, {L"Tournée à 45 degrés", L"Rotated 45 degrees"},
     {L"Fais tourner la mire dans son plan, comme un losange : environ 45 degrés, au milieu de l'image.",
      L"Rotate the board in its own plane, like a diamond: about 45 degrees, in the middle of the image."}, 0, 0, 0.42, 0, 0, 45},
    {{L"Bord gauche", L"Left edge"}, {L"Contre le bord gauche", L"Against the left edge"},
     {L"Pousse la mire contre le bord gauche de l'image, sans que ses coins sortent de l'image.",
      L"Push the board against the left edge of the image, without any corner leaving the image."}, -0.6, 0, 0.34, 0, 0, 0},
    {{L"Bord droit", L"Right edge"}, {L"Contre le bord droit", L"Against the right edge"},
     {L"Pousse la mire contre le bord droit de l'image, sans que ses coins sortent de l'image.",
      L"Push the board against the right edge of the image, without any corner leaving the image."}, 0.6, 0, 0.34, 0, 0, 0},
    {{L"Bord haut", L"Top edge"}, {L"Contre le bord du haut", L"Against the top edge"},
     {L"Pousse la mire contre le haut de l'image, sans que ses coins sortent de l'image.",
      L"Push the board against the top of the image, without any corner leaving the image."}, 0, 0.6, 0.34, 0, 0, 0},
    {{L"Bord bas", L"Bottom edge"}, {L"Contre le bord du bas", L"Against the bottom edge"},
     {L"Pousse la mire contre le bas de l'image, sans que ses coins sortent de l'image.",
      L"Push the board against the bottom of the image, without any corner leaving the image."}, 0, -0.6, 0.34, 0, 0, 0},
  };
  if (n) *n = (int)(sizeof(g) / sizeof(g[0]));
  return g;
}

// Nombre de carreaux (largeur x hauteur) proposés ; les coins intérieurs sont (n-1, m-1), de parités différentes comme le demande la détection.
struct BoardPreset { int sx, sy; };
inline const BoardPreset* Presets(int* n) {
  static const BoardPreset p[] = {{10, 7}, {9, 6}, {11, 8}, {8, 5}};
  if (n) *n = 4;
  return p;
}

inline Bi MessageText(const std::string& code) {
  if (code == "show") return {L"Montre la mire à la caméra : tout le damier doit être visible, sans coin coupé.", L"Show the board to the camera: the whole grid must be visible, with no corner cut off."};
  if (code == "showR") return {L"L'autre caméra ne voit pas la mire : centre-la un peu plus ou recule.", L"The other camera cannot see the board: center it a little more or step back."};
  if (code == "still") return {L"Bien. Ne bouge plus… (une seconde)", L"Good. Hold still… (one second)"};
  if (code == "move") return {L"Trop proche d'une pose déjà prise : change de place, de distance ou d'angle (voir le conseil).", L"Too close to a pose already taken: change place, distance or angle (see the tip)."};
  if (code == "ok") return {L"Vue enregistrée !", L"View saved!"};
  if (code == "edge") return {L"La mire touche le bord de l'image : rentre-la un peu.", L"The board touches the image edge: bring it in a little."};
  if (code == "small") return {L"Trop loin : approche la mire.", L"Too far: bring the board closer."};
  if (code == "swapped") return {L"Les deux caméras semblent inversées : clique sur Inverser G / D, puis recommence cette pose.", L"The two cameras seem swapped: click Swap L / R, then redo this pose."};
  if (code == "order") return {L"Les deux caméras ne voient pas la mire dans le même sens : ne la retourne pas, garde le même côté devant.", L"The two cameras do not see the board the same way round: do not flip it, keep the same side in front."};
  if (code == "parity") return {L"Grille impossible : le nombre de carreaux en largeur et en hauteur doivent être de parités différentes (par ex. 10 × 7). Annule et change de grille.", L"Impossible grid: the number of squares across and down must have different parities (e.g. 10 × 7). Cancel and change the grid."};
  return {L"Montre la mire à la caméra.", L"Show the board to the camera."};
}

inline Bi WarningText(const std::string& code) {
  if (code == "few_views") return {L"Peu de vues utilisables : refais avec plus de poses variées.", L"Few usable views: redo with more varied poses."};
  if (code == "cov_low") return {L"L'image n'est pas assez couverte : mets la mire dans les coins et contre les bords.", L"The image is not covered enough: put the board in the corners and against the edges."};
  if (code == "rms_high") return {L"Erreur de reprojection élevée : mire pas plane, floue, ou taille de carreau mal mesurée.", L"High reprojection error: board not flat, blurry, or square size measured wrongly."};
  if (code == "epi_high") return {L"Les deux caméras ne sont pas bien alignées après calibration : vérifie qu'elles ne bougent pas l'une par rapport à l'autre.", L"The two cameras are not well aligned after calibration: check that they do not move relative to each other."};
  if (code == "roundtrip") return {L"La correction de la distorsion est peu fiable sur les bords : montre la mire plus près des bords.", L"Distortion correction is unreliable near the edges: show the board closer to the edges."};
  if (code == "swapped") return {L"Les caméras gauche et droite sont probablement inversées : clique sur Inverser G / D pendant la capture, puis refais.", L"The left and right cameras are probably swapped: click Swap L / R during capture, then redo."};
  if (code == "pp_far") return {L"Le centre optique est très décalé : la caméra est peut-être recadrée (mode différent) ou l'objectif mal monté.", L"The optical center is far off: the camera may be cropped (different mode) or the lens badly mounted."};
  if (code == "parity") return {L"Grille de parité invalide : le résultat peut être retourné.", L"Invalid grid parity: the result may be flipped."};
  if (code == "base_far") return {L"La distance entre les caméras diffère de plus de 12 % de ta mesure : vérifie la taille d'un carreau.", L"The distance between the cameras differs from your measurement by more than 12%: check the square size."};
  return {L"", L""};
}

inline Bi StageText(const std::string& s) {
  if (s == "focale de depart") return {L"estimation de la focale", L"estimating the focal length"};
  if (s == "choix du modele de distorsion") return {L"choix du modèle de distorsion", L"choosing the distortion model"};
  if (s == "calibrage des deux cameras") return {L"calibrage de chaque caméra", L"calibrating each camera"};
  if (s == "calibrage stereo") return {L"calibrage stéréo", L"stereo calibration"};
  if (s == "controle de la qualite") return {L"contrôle de la qualité", L"quality check"};
  if (s == "termine") return {L"terminé", L"done"};
  return {L"préparation", L"preparing"};
}

inline Bi Verdict(int score) {
  if (score >= 85) return {L"excellente", L"excellent"};
  if (score >= 70) return {L"bonne", L"good"};
  if (score >= 50) return {L"moyenne : à refaire si possible", L"fair: redo if possible"};
  return {L"mauvaise : à refaire", L"poor: redo"};
}

// ------------------------------------------------------------------ étape affichée
enum Step { kPrep = 0, kCapture = 1, kCompute = 2, kResult = 3, kVerify = 4 };

inline Step DecideStep(bool probeRunning, const Status& s, bool wantVerify) {
  if (!probeRunning) return kPrep;
  if (s.state == "capture") return kCapture;
  if (s.state == "compute") return kCompute;
  if (s.state == "done") return wantVerify ? kVerify : kResult;
  if (s.state == "error") return kResult;
  return kPrep;
}

inline int PopCount(int v) {
  int n = 0;
  unsigned u = (unsigned)v;
  while (u) { n += (int)(u & 1u); u >>= 1; }
  return n;
}

// ------------------------------------------------------------------ mire en perspective
struct Pose { double px = 0, py = 0, size = 0.5, yaw = 0, pitch = 0, roll = 0; };

// Point (x, y) du plan de la mire (-1..1 chacun) -> pixel dans le rectangle (rx, ry, rw, rh), y vers le bas.
// Convention : lacet positif = bord droit plus proche ; tangage positif = bord du haut plus loin.
inline void ProjectBoard(const Pose& p, int squaresX, int squaresY, double x, double y, double rx, double ry, double rw, double rh, double& ox, double& oy) {
  const double kDeg = 3.14159265358979323846 / 180.0;
  const double aspect = (double)squaresY / (squaresX > 0 ? squaresX : 1);
  const double bx = x, by = y * aspect;
  const double cr = std::cos(p.roll * kDeg), sr = std::sin(p.roll * kDeg);
  const double qx = bx * cr - by * sr, qy = bx * sr + by * cr;
  const double cy = std::cos(p.yaw * kDeg), sy = std::sin(p.yaw * kDeg);
  const double cp = std::cos(p.pitch * kDeg), sp = std::sin(p.pitch * kDeg);
  const double x1 = qx * cy, z1 = -qx * sy;
  const double y2 = qy * cp - z1 * sp, z2 = qy * sp + z1 * cp;
  const double d = 3.2;   // distance de la caméra, en demi-largeurs de mire
  const double k = d / std::fmax(0.5, d + z2);
  const double u = p.size * 0.5 * rw;
  ox = rx + rw * 0.5 + p.px * 0.5 * rw + x1 * k * u;
  oy = ry + rh * 0.5 - (p.py * 0.5 * rh + y2 * k * u);
}

// ------------------------------------------------------------------ aperçu partagé (800 x 300, image stéréo réduite avec bandes sombres si le format diffère)
struct EyeRect { double x, y, w, h; };

// Rectangle de l'œil « gauche » de la sonde dans l'aperçu. swap : les yeux sont inversés (l'œil « gauche » est alors la moitié droite de l'image brute).
inline EyeRect PreviewEye(int eyeW, int eyeH, bool swap, int bufW = 800, int bufH = 300) {
  if (eyeW <= 0 || eyeH <= 0) { eyeW = 4; eyeH = 3; }
  const double fw = 2.0 * eyeW, fh = eyeH;
  const double s = std::fmin((double)bufW / fw, (double)bufH / fh);
  const double nw = fw * s, nh = fh * s;
  const double ox = (bufW - (int)nw) / 2, oy = (bufH - (int)nh) / 2;
  EyeRect r;
  r.w = nw * 0.5;
  r.h = nh;
  r.x = ox + (swap ? r.w : 0.0);
  r.y = oy;
  return r;
}

// Zoom du mode, en texte court : « ×1,00 » avec sa provenance.
inline Bi ZoomSourceText(const std::string& src, bool confirmed) {
  if (src == "calib") return {L"mode de la calibration", L"calibration mode"};
  if (src == "manual") return {L"saisi à la main", L"set by hand"};
  if (src == "auto") return confirmed ? Bi{L"appris avec le gyro, vérifié", L"learned with the gyro, verified"} : Bi{L"appris avec le gyro", L"learned with the gyro"};
  return {L"pas encore mesuré : bouge la tête pour que l'IMU le mesure", L"not measured yet: move your head so the IMU can measure it"};
}

}  // namespace calview
