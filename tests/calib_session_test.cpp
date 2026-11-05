// Test de la calibration maison (src/calib_session.h) avec une camera stereo SIMULEE : verite connue (focale, distorsion, base de 60 mm).
//   g++ -std=c++17 -O2 -I src tests/calib_session_test.cpp $(OPENCV libs) -o calib_session_test
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <chrono>
#include <cstdlib>
#include <random>

#include "../src/calib_session.h"

static int g_fail = 0;
#define CHECK(c, ...) do { if (!(c)) { g_fail++; std::printf("ECHEC ligne %d : ", __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

struct Rig {
  cv::Size size{800, 600};
  cv::Mat K1, D1, K2, D2, R, T;   // verite : x_droite = R x_gauche + T
};

static Rig MakeRig(bool strong) {
  Rig g;
  g.K1 = (cv::Mat_<double>(3, 3) << 400, 0, 402, 0, 400, 297, 0, 0, 1);
  g.K2 = (cv::Mat_<double>(3, 3) << 401, 0, 395, 0, 401, 303, 0, 0, 1);
  if (strong) {
    g.D1 = (cv::Mat_<double>(8, 1) << -0.19, 0.42, 0.0003, -0.0002, -0.1, 0.0, 0.0, 0.0);
    g.D2 = (cv::Mat_<double>(8, 1) << -0.17, 0.38, 0.0001, 0.0002, -0.08, 0.0, 0.0, 0.0);
  } else {
    g.D1 = (cv::Mat_<double>(5, 1) << -0.08, 0.03, 0.0003, -0.0002, 0.0);
    g.D2 = (cv::Mat_<double>(5, 1) << -0.07, 0.02, 0.0001, 0.0002, 0.0);
  }
  cv::Mat rv = (cv::Mat_<double>(3, 1) << 0.004, -0.006, 0.008);
  cv::Rodrigues(rv, g.R);
  g.T = (cv::Mat_<double>(3, 1) << -0.060, 0.0004, 0.0003);
  return g;
}

// vues simulees : la mire (cols x rows coins) a une pose dans le repere de la camera gauche
static bool Project(const Rig& g, const ar6cal::Board& b, const cv::Vec3d& rv, const cv::Vec3d& tv, std::mt19937& rng, double noise,
                    std::vector<cv::Point2f>& L, std::vector<cv::Point2f>& R) {
  auto obj = ar6cal::BoardPoints(b);
  // centre la mire sur son milieu
  cv::Point3f mid(0.5f * (b.cols - 1) * (float)(b.squareMm / 1000.0), 0.5f * (b.rows - 1) * (float)(b.squareMm / 1000.0), 0.f);
  for (auto& p : obj) p -= mid;
  cv::projectPoints(obj, rv, tv, g.K1, g.D1, L);
  // pose dans la camera droite : x_r = R (Rb x + t) + T
  cv::Mat Rb;
  cv::Rodrigues(rv, Rb);
  cv::Mat Rr = g.R * Rb;
  cv::Mat tr = g.R * cv::Mat(tv) + g.T;
  cv::Mat rr;
  cv::Rodrigues(Rr, rr);
  cv::projectPoints(obj, rr, tr, g.K2, g.D2, R);
  std::normal_distribution<double> n(0.0, noise);
  for (auto& p : L) { p.x += (float)n(rng); p.y += (float)n(rng); }
  for (auto& p : R) { p.x += (float)n(rng); p.y += (float)n(rng); }
  for (size_t i = 0; i < L.size(); i++)
    if (L[i].x < 5 || L[i].y < 5 || L[i].x > g.size.width - 6 || L[i].y > g.size.height - 6 || R[i].x < 5 || R[i].y < 5 ||
        R[i].x > g.size.width - 6 || R[i].y > g.size.height - 6) return false;
  return true;
}

static std::vector<ar6cal::View> MakeViews(const Rig& g, const ar6cal::Board& b, int n, double noise, unsigned seed) {
  std::mt19937 rng(seed);
  std::uniform_real_distribution<double> ux(-0.28, 0.28), uy(-0.2, 0.2), uz(0.45, 1.1), ua(-0.5, 0.5);
  std::vector<ar6cal::View> out;
  int guard = 0;
  while ((int)out.size() < n && guard++ < 20000) {
    cv::Vec3d rv(ua(rng), ua(rng), ua(rng) * 1.5), tv(ux(rng), uy(rng), uz(rng));
    // la mire fait face a la camera (rotation autour de x/y limitee) ; on tourne un peu en lacet / tangage
    std::vector<cv::Point2f> L, R;
    if (!Project(g, b, rv, tv, rng, noise, L, R)) continue;
    ar6cal::View v;
    v.L = L; v.R = R; v.f = ar6cal::ComputeFeat(L, b, g.size);
    out.push_back(v);
  }
  return out;
}

// Objectif tres deforme comme celui de la camera ELP (mesures RTAB-Map reduites a 800 x 600) : modele rationnel, centre optique decale.
static Rig MakeWideRig() {
  Rig g;
  g.K1 = (cv::Mat_<double>(3, 3) << 301.5, 0, 479.1, 0, 299.9, 231.8, 0, 0, 1);
  g.K2 = (cv::Mat_<double>(3, 3) << 301.3, 0, 474.2, 0, 299.6, 262.0, 0, 0, 1);
  g.D1 = (cv::Mat_<double>(8, 1) << -0.1869, 0.5308, -0.000286, 0.000132, 0.02637, -0.1814, 0.5142, 0.05489);
  g.D2 = (cv::Mat_<double>(8, 1) << -0.1643, 0.6685, 0.0000014, -0.0000071, 0.04698, -0.1518, 0.6358, 0.08755);
  cv::Mat rv = (cv::Mat_<double>(3, 1) << 0.0005, 0.0078, -0.0089);
  cv::Rodrigues(rv, g.R);
  g.T = (cv::Mat_<double>(3, 1) << -0.0509, 0.00015, 0.00035);
  return g;
}

static std::vector<ar6cal::View> MakeWideViews(const Rig& g, const ar6cal::Board& b, int n, double noise, unsigned seed) {
  // comme les objectifs de la session guidee : la mire est placee partout dans l'image, jusqu'aux bords et aux coins
  std::mt19937 rng(seed);
  std::uniform_real_distribution<double> pu(-0.9, 0.9), pv(-0.9, 0.9), uz(0.28, 0.9), ua(-0.5, 0.5);
  std::vector<ar6cal::View> out;
  int guard = 0;
  while ((int)out.size() < n && guard++ < 400000) {
    double z = uz(rng);
    double u = g.K1.at<double>(0, 2) + pu(rng) * g.size.width * 0.5, v = g.K1.at<double>(1, 2) + pv(rng) * g.size.height * 0.5;
    cv::Vec3d tv((u - g.K1.at<double>(0, 2)) / g.K1.at<double>(0, 0) * z, (v - g.K1.at<double>(1, 2)) / g.K1.at<double>(1, 1) * z, z);
    cv::Vec3d rv(ua(rng), ua(rng), ua(rng) * 1.3);
    std::vector<cv::Point2f> L, R;
    if (!Project(g, b, rv, tv, rng, noise, L, R)) continue;
    ar6cal::View vw;
    vw.L = L; vw.R = R; vw.f = ar6cal::ComputeFeat(L, b, g.size);
    out.push_back(vw);
  }
  return out;
}

static void TestWide() {
  Rig g = MakeWideRig();
  ar6cal::Board b;
  auto views = MakeWideViews(g, b, 22, 0.12, 11);
  CHECK((int)views.size() == 22, "vues simulees (objectif large) : %d", (int)views.size());
  ar6cal::SolveResult r;
  auto t0 = std::chrono::steady_clock::now();
  ar6cal::SolveStereo(views, g.size, b, 120, [](const std::string& s, double f) { std::printf("   ... %s %.0f%%\n", s.c_str(), f * 100); }, r);
  double dt = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
  CHECK(r.ok, "calcul : %s", r.err.c_str());
  if (!r.ok) return;
  std::printf("[large] %.1f s modele=%s rmsL=%.3f rmsR=%.3f rmsS=%.3f epi=%.3f/%.3f base=%.2f mm f=%.1f/%.1f c=(%.1f,%.1f) hfov=%.1f cov=%.2f rt=%.3f recon=%.4f score=%d vues=%d/%d heldOut=%.3f\n",
              dt, r.model.c_str(), r.rmsL, r.rmsR, r.rmsS, r.epi, r.epiMax, r.baselineMm, r.calib.K1.at<double>(0, 0), r.calib.K2.at<double>(0, 0),
              r.calib.K1.at<double>(0, 2), r.calib.K1.at<double>(1, 2), r.hfovDeg, r.coverage, r.roundTripPx, r.recon, r.score, r.used, r.total, r.heldOut);
  for (auto& w : r.warnings) std::printf("   avertissement : %s\n", w.c_str());
  CHECK(std::fabs(r.baselineMm - 50.9) < 1.0, "base %.2f mm (vrai 50.9)", r.baselineMm);
  CHECK(r.epi < 0.3, "ecart vertical %.3f", r.epi);
}

static void TestSolve(bool strong) {
  Rig g = MakeRig(strong);
  ar6cal::Board b; b.cols = 9; b.rows = 6; b.squareMm = 25.0;
  auto views = MakeViews(g, b, 24, 0.12, strong ? 7 : 9);
  CHECK((int)views.size() == 24, "vues simulees : %d", (int)views.size());
  ar6cal::SolveResult r;
  ar6cal::SolveStereo(views, g.size, b, 120, nullptr, r);
  CHECK(r.ok, "calcul : %s", r.err.c_str());
  if (!r.ok) return;
  std::printf("[%s] modele=%s rmsL=%.3f rmsR=%.3f rmsS=%.3f epi=%.3f/%.3f base=%.2f mm f=%.1f/%.1f hfov=%.1f cov=%.2f rt=%.3f recon=%.4f score=%d vues=%d/%d heldOut=%.3f\n",
              strong ? "forte" : "faible", r.model.c_str(), r.rmsL, r.rmsR, r.rmsS, r.epi, r.epiMax, r.baselineMm, r.calib.K1.at<double>(0, 0),
              r.calib.K2.at<double>(0, 0), r.hfovDeg, r.coverage, r.roundTripPx, r.recon, r.score, r.used, r.total, r.heldOut);
  for (auto& w : r.warnings) std::printf("   avertissement : %s\n", w.c_str());
  CHECK(std::fabs(r.baselineMm - 60.0) < 0.6, "base %.2f mm (vrai 60)", r.baselineMm);
  CHECK(std::fabs(r.calib.K1.at<double>(0, 0) / 400.0 - 1.0) < 0.012, "focale gauche %.1f (vrai 400)", r.calib.K1.at<double>(0, 0));
  CHECK(std::fabs(r.calib.K2.at<double>(0, 0) / 401.0 - 1.0) < 0.012, "focale droite %.1f (vrai 401)", r.calib.K2.at<double>(0, 0));
  CHECK(r.rmsL < 0.2 && r.rmsR < 0.2 && r.rmsS < 0.2, "erreur de reprojection trop grande");
  CHECK(r.epi < 0.25, "ecart vertical %.3f", r.epi);
  CHECK(r.roundTripPx < 0.2, "distorsion non inversible : %.3f px", r.roundTripPx);
  CHECK(std::fabs(r.recon - 1.0) < 0.01, "echelle interne %.4f", r.recon);
  CHECK(r.score >= 80, "score %d", r.score);
  CHECK(r.calib.T.at<double>(0, 0) < 0, "signe de T");

  // mauvaise taille de carreau saisie (30 mm au lieu de 25) : toute la geometrie metrique est x1,2 -> la base aussi (rien d'autre ne change)
  ar6cal::Board b2 = b; b2.squareMm = 30.0;
  ar6cal::SolveResult r2;
  ar6cal::SolveStereo(views, g.size, b2, 120, nullptr, r2);
  CHECK(r2.ok && std::fabs(r2.baselineMm / r.baselineMm - 1.2) < 0.01, "la base doit suivre la taille de carreau : %.2f vs %.2f", r2.baselineMm, r.baselineMm);

  // verification a la regle : la mire a 700 mm (verite), distance estimee avec la taille de carreau JUSTE puis fausse
  std::mt19937 rng(3);
  std::vector<cv::Point2f> L, R;
  bool ok = Project(g, b, cv::Vec3d(0.05, -0.03, 0.02), cv::Vec3d(0.0, 0.0, 0.7), rng, 0.05, L, R);
  CHECK(ok, "vue de verification");
  double z = 0, sq = 0;
  CHECK(ar6cal::BoardDistance(r.calib, L, R, b, z, sq), "BoardDistance");
  CHECK(std::fabs(z / 700.0 - 1.0) < 0.01, "distance estimee %.1f mm (vrai 700)", z);
  CHECK(ar6cal::BoardDistance(r2.calib, L, R, b2, z, sq) && std::fabs(z / 840.0 - 1.0) < 0.02, "avec carreaux faux : %.1f mm (attendu ~840)", z);
}

// ---- rendu d'une mire (images 8 bits) pour tester la detection et la session -------------------------------------------------------------
static cv::Mat BoardTexture(const ar6cal::Board& b, int px) {
  const int sx = b.cols + 1, sy = b.rows + 1;   // carreaux
  const int m = px;                             // marge blanche d'un carreau
  cv::Mat t(sy * px + 2 * m, sx * px + 2 * m, CV_8U, cv::Scalar(255));
  for (int j = 0; j < sy; j++)
    for (int i = 0; i < sx; i++)
      if ((i + j) % 2 == 0) cv::rectangle(t, cv::Rect(m + i * px, m + j * px, px, px), cv::Scalar(0), cv::FILLED);
  return t;
}

// image sans distorsion de la mire vue par une camera de matrice K, pose (rv, tv) dans le repere de CETTE camera
static cv::Mat RenderBoard(const cv::Mat& tex, int px, const ar6cal::Board& b, const cv::Mat& K, cv::Size size, const cv::Vec3d& rv, const cv::Vec3d& tv) {
  const float sqm = (float)(b.squareMm / 1000.0);
  const int m = px;
  // coins de la texture -> metres dans le repere de la mire centree sur le milieu des coins interieurs
  auto toM = [&](float u, float v) {
    float x = (u - m) / px * sqm - 0.5f * (b.cols - 1) * sqm - sqm;   // le premier coin interieur est a 1 carreau du bord
    float y = (v - m) / px * sqm - 0.5f * (b.rows - 1) * sqm - sqm;
    return cv::Point3f(x, y, 0.f);
  };
  std::vector<cv::Point2f> src = {{0, 0}, {(float)tex.cols, 0}, {(float)tex.cols, (float)tex.rows}, {0, (float)tex.rows}};
  std::vector<cv::Point3f> o3;
  for (auto& p : src) o3.push_back(toM(p.x, p.y));
  std::vector<cv::Point2f> dst;
  cv::projectPoints(o3, rv, tv, K, cv::Mat::zeros(5, 1, CV_64F), dst);
  cv::Mat H = cv::getPerspectiveTransform(src, dst), out;
  cv::warpPerspective(tex, out, H, size, cv::INTER_LINEAR, cv::BORDER_CONSTANT, cv::Scalar(128));
  return out;
}

static void TestDetectionAndSession() {
  Rig g = MakeRig(false);
  g.D1 = cv::Mat::zeros(5, 1, CV_64F);
  g.D2 = cv::Mat::zeros(5, 1, CV_64F);   // images sans distorsion : seul le flux de capture est teste ici
  ar6cal::Board b; b.cols = 9; b.rows = 6; b.squareMm = 25.0;
  const int px = 40;
  cv::Mat tex = BoardTexture(b, px);

  // detection simple
  cv::Vec3d rv(0.1, -0.05, 0.0), tv(0.0, 0.0, 0.6);
  cv::Mat im = RenderBoard(tex, px, b, g.K1, g.size, rv, tv);
  std::vector<cv::Point2f> c;
  CHECK(ar6cal::DetectBoard(im, b, c), "detection de la mire rendue");
  CHECK((int)c.size() == 54, "54 coins");
  auto f = ar6cal::ComputeFeat(c, b, g.size);
  std::printf("[detection] centre (%.2f, %.2f) taille %.2f yaw %.3f pitch %.3f roll %.1f bord %.0f px\n", f.px, f.py, f.size, f.yaw, f.pitch, f.roll, f.minEdge);

  // session complete : on presente une serie de poses (chacune 4 images fixes), on attend la detection a chaque pas
  ar6cal::Session s;
  s.Start(b, 120, 18, 60.0);
  struct P { double x, y, z, ax, ay, az; };
  const P poses[] = {
      {0, 0, 0.45, 0, 0, 0}, {-0.25, -0.18, 0.7, 0, 0, 0}, {0.25, -0.18, 0.7, 0, 0, 0}, {0.25, 0.18, 0.7, 0, 0, 0}, {-0.25, 0.18, 0.7, 0, 0, 0},
      {0, 0, 1.1, 0, 0, 0}, {0, 0, 0.7, 0, -0.5, 0}, {0, 0, 0.7, 0, 0.5, 0}, {0, 0, 0.7, -0.5, 0, 0}, {0, 0, 0.7, 0.5, 0, 0},
      {0, 0, 0.38, 0, 0, 0}, {0, 0, 0.7, 0, 0, 0.7}, {-0.3, 0, 0.8, 0.2, 0.3, 0}, {0.3, 0, 0.8, -0.2, -0.3, 0}, {0, -0.2, 0.6, 0.3, 0, 0.3},
      {0, 0.2, 0.6, -0.3, 0, -0.3}, {-0.2, 0, 0.55, 0, 0.4, 0.2}, {0.2, 0, 0.55, 0, -0.4, -0.2}, {0.1, 0.1, 0.9, 0.25, 0.25, 0.4}, {-0.1, -0.1, 0.9, -0.25, -0.25, -0.4},
  };
  double t = 0;
  int fed = 0;
  for (const auto& p : poses) {
    cv::Vec3d r(p.ax, p.ay, p.az), tt(p.x, p.y, p.z);
    cv::Mat L = RenderBoard(tex, px, b, g.K1, g.size, r, tt);
    // camera droite : pose = R_rel * pose + T (rotation relative nulle ici, base de 60 mm) ; K2 identique pour simplifier
    cv::Mat Rb;
    cv::Rodrigues(r, Rb);
    cv::Mat tr = cv::Mat(tt) + (cv::Mat_<double>(3, 1) << -0.06, 0, 0);
    cv::Vec3d trv(tr.at<double>(0), tr.at<double>(1), tr.at<double>(2));
    cv::Mat Rm = RenderBoard(tex, px, b, g.K1, g.size, r, trv);
    for (int rep = 0; rep < 5; rep++) {
      t += 0.2;
      unsigned v0 = s.Version();
      s.Feed(L, Rm, t);
      for (int w = 0; w < 100 && s.Version() == v0; w++) std::this_thread::sleep_for(std::chrono::milliseconds(20));
      std::this_thread::sleep_for(std::chrono::milliseconds(30));
      fed++;
    }
  }
  for (int w = 0; w < 600 && s.state() == ar6cal::Session::kCapture; w++) {
    if (w == 5) s.RequestCompute();
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
  }
  for (int w = 0; w < 1200 && s.state() == ar6cal::Session::kCompute; w++) std::this_thread::sleep_for(std::chrono::milliseconds(50));
  std::string js = s.StatusJson();
  if (const char* dump = std::getenv("AR6_DUMP_STATUS")) { std::ofstream(dump) << js; }   // lu par calib_view_test
  std::printf("[session] etat=%d, JSON %zu octets : %.300s\n", (int)s.state(), js.size(), js.c_str());
  CHECK(s.state() == ar6cal::Session::kDone, "la session doit finir en 'done' (etat %d)", (int)s.state());
  if (s.state() == ar6cal::Session::kDone) {
    auto r = s.Result();
    std::printf("[session] vues %d/%d base %.2f mm score %d rms %.3f/%.3f epi %.3f modele %s\n", r.used, r.total, r.baselineMm, r.score, r.rmsL, r.rmsR, r.epi, r.model.c_str());
    CHECK(r.total >= 10, "assez de vues acceptees : %d", r.total);
    CHECK(std::fabs(r.baselineMm - 60.0) < 1.5, "base %.2f mm", r.baselineMm);

    // verification a la regle : mire a 0,8 m
    cv::Vec3d rr(0.05, 0.02, 0.0), t1(0, 0, 0.8), t2(-0.06, 0, 0.8);
    cv::Mat L = RenderBoard(tex, px, b, g.K1, g.size, rr, t1), Rm = RenderBoard(tex, px, b, g.K1, g.size, rr, t2);
    for (int k = 0; k < 3; k++) {
      unsigned v0 = s.Version();
      t += 0.3;
      s.Feed(L, Rm, t);
      for (int w = 0; w < 100 && s.Version() == v0; w++) std::this_thread::sleep_for(std::chrono::milliseconds(20));
      std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }
    std::string err;
    CHECK(s.Verify(800.0, err), "verification : %s", err.c_str());
    std::string js2 = s.StatusJson();
    auto pos = js2.find("\"verify\"");
    std::printf("[verif] %s\n", js2.substr(pos, 80).c_str());
  }
  s.Cancel();
}

static void TestNativeFile() {
  Rig g = MakeRig(true);
  ar6cal::Board b;
  auto views = MakeViews(g, b, 16, 0.1, 5);
  ar6cal::SolveResult r;
  ar6cal::SolveStereo(views, g.size, b, 120, nullptr, r);
  CHECK(r.ok, "calcul : %s", r.err.c_str());
  if (!r.ok) return;
  std::string err;
  const std::string path = "/tmp/ar6dof_calib_test.yml";
  CHECK(SaveNativeCalib(r.calib, path, err), "ecriture : %s", err.c_str());
  StereoCalib c2;
  CHECK(LoadNativeCalib(path, c2, err), "lecture : %s", err.c_str());
  CHECK(c2.size == g.size && std::fabs(c2.K1.at<double>(0, 0) - r.calib.K1.at<double>(0, 0)) < 1e-9, "K identique");
  CHECK(c2.D1.total() == r.calib.D1.total() && c2.T.rows == 3 && std::fabs(cv::norm(c2.T) - cv::norm(r.calib.T)) < 1e-12, "D / T identiques");
  CHECK(c2.source == "ar6dof" && c2.views == r.calib.views && c2.fps == 120, "meta");
  Rectifier rect;
  CHECK(MakeRectifier(c2, g.size, rect, err), "rectification : %s", err.c_str());
  std::printf("[natif] relu : base %.2f mm f %.1f hfov %.1f modele '%s' date %s\n", rect.baseline_mm, rect.f, rect.hfov_deg, c2.model.c_str(), c2.created.c_str());
  // le dossier de calibration du depot (RTAB-Map) doit se charger et se convertir
  StereoCalib rt;
  if (LoadRtabmapStereo("calibration", rt, err)) {
    std::printf("[rtab] %dx%d base %.2f mm\n", rt.size.width, rt.size.height, cv::norm(rt.T) * 1000.0);
    Rectifier rr;
    CHECK(MakeRectifier(rt, cv::Size(800, 600), rr, err), "rtab rectification");
    for (auto& w : rr.warnings) std::printf("   avertissement : %s\n", w.c_str());
  }
}

int main() {
  TestWide();
  TestSolve(false);
  TestSolve(true);
  TestDetectionAndSession();
  TestNativeFile();
  std::printf(g_fail ? "\n%d ECHEC(S)\n" : "\nTOUS LES TESTS DE CALIBRATION OK\n", g_fail);
  return g_fail ? 1 : 0;
}
