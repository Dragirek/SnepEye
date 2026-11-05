// Calibration independante du mode : ResolveCalib (reduit / recadre / format different), ZoomStore, et DelayEstimator::EstimateZoom
// (zoom du mode appris en comparant la rotation vue par la camera a celle du gyro).
//   g++ -std=c++17 -O2 -I src -I <opencv>/include/opencv4 tests/calib_mode_test.cpp <libs opencv> -lz -lpthread -ldl -o calib_mode_test
#include <cstdio>
#include <cstdlib>
#include <random>

#include "../src/calib.h"
#include "../src/delay_estimator.h"

static int fails = 0;
#define CHECK(c, ...) do { if (!(c)) { std::printf("ECHEC ligne %d : ", __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); fails++; } } while (0)

static StereoCalib Make() {
  StereoCalib c;
  c.size = cv::Size(1600, 1200);
  c.K1 = (cv::Mat_<double>(3, 3) << 700, 0, 810, 0, 702, 590, 0, 0, 1);
  c.K2 = (cv::Mat_<double>(3, 3) << 701, 0, 795, 0, 701, 605, 0, 0, 1);
  c.D1 = (cv::Mat_<double>(1, 5) << -0.2, 0.05, 0, 0, 0);
  c.D2 = c.D1.clone();
  c.R = cv::Mat::eye(3, 3, CV_64F);
  c.T = (cv::Mat_<double>(3, 1) << -0.06, 0, 0);
  c.created = "2026-11-05";
  return c;
}

static Mat3 RotZ(double a) { Mat3 R; R.m[0][0] = std::cos(a); R.m[0][1] = -std::sin(a); R.m[0][2] = 0; R.m[1][0] = std::sin(a); R.m[1][1] = std::cos(a); R.m[1][2] = 0; R.m[2][0] = 0; R.m[2][1] = 0; R.m[2][2] = 1; return R; }

int main() {
  StereoCalib c = Make();
  ModeRel rel;
  // 1. reduit de moitie, zoom 1 : identique a ScaleK
  StereoCalib a = ResolveCalib(c, cv::Size(800, 600), 1.0, &rel);
  cv::Mat k = ScaleK(c.K1, 0.5, 0.5);
  CHECK(std::abs(a.K1.at<double>(0, 0) - 350.0) < 1e-9 && std::abs(a.K1.at<double>(0, 2) - k.at<double>(0, 2)) < 1e-9 && std::abs(a.K1.at<double>(1, 2) - k.at<double>(1, 2)) < 1e-9,
        "reduction : f=%.3f cx=%.3f (attendu 350 / %.3f)", a.K1.at<double>(0, 0), a.K1.at<double>(0, 2), k.at<double>(0, 2));
  CHECK(a.size == cv::Size(800, 600) && !rel.sameSize && rel.sameAspect, "taille / format");
  CHECK(cv::norm(a.T - c.T) < 1e-12 && cv::norm(a.D1 - c.D1) < 1e-12, "T et distorsion ne changent pas");
  // 2. mode recadre (zoom 2) : focale en pixels inchangee, centre optique decale du centre de l'image dans le mode recadre
  StereoCalib b = ResolveCalib(c, cv::Size(800, 600), 2.0);
  CHECK(std::abs(b.K1.at<double>(0, 0) - 700.0) < 1e-9, "recadrage : f=%.3f", b.K1.at<double>(0, 0));
  // decalage du centre optique par rapport au centre de l'image : (810-799.5)=10.5 px dans la calibration -> 10.5 px (focale inchangee)
  CHECK(std::abs(b.K1.at<double>(0, 2) - (399.5 + 10.5)) < 1e-9, "recadrage : cx=%.3f", b.K1.at<double>(0, 2));
  // 3. meme taille : rien ne change
  StereoCalib d = ResolveCalib(c, c.size, 1.0, &rel);
  CHECK(rel.sameSize && cv::norm(d.K1 - c.K1) < 1e-12, "meme taille");
  // 4. format different (16:9 depuis 4:3) : base sur la hauteur, ne plante pas
  StereoCalib e = ResolveCalib(c, cv::Size(960, 540), 1.0, &rel);
  CHECK(!rel.sameAspect && std::abs(e.K1.at<double>(0, 0) - 700.0 * 540 / 1200) < 1e-9, "format different : f=%.3f", e.K1.at<double>(0, 0));
  // 5. la rectification marche avec la calibration resolue (pas d'avertissement d'echelle, f de rectification coherent)
  Rectifier r;
  std::string err;
  CHECK(MakeRectifier(a, cv::Size(800, 600), r, err), "rectifieur : %s", err.c_str());
  CHECK(r.warnings.empty() || r.warnings[0].find("mise a l'echelle") == std::string::npos, "pas d'avertissement d'echelle");
  CHECK(std::abs(r.baseline_mm - 60.0) < 1e-6, "base %.3f", r.baseline_mm);

  // 6. ZoomStore : aller-retour, invalidation par l'identifiant de calibration
  const std::string path = "/tmp/mode_zoom_test.yml";
  ZoomStore zs;
  zs.calibId = CalibId(c);
  zs.Set({"800x600@120", "auto", 1.98, 3});
  zs.Set({"800x600@60", "manual", 1.0, 0});
  zs.Set({"800x600@120", "auto", 2.01, 4});
  CHECK(zs.Save(path), "sauvegarde");
  ZoomStore z2;
  CHECK(z2.Load(path, CalibId(c)), "relecture");
  CHECK(z2.e.size() == 2 && z2.Find("800x600@120") && std::abs(z2.Find("800x600@120")->zoom - 2.01) < 1e-9 && z2.Find("800x600@120")->source == "auto", "contenu");
  ZoomStore z3;
  StereoCalib other = c;
  other.created = "2026-11-06";
  CHECK(!z3.Load(path, CalibId(other)) && z3.e.empty(), "autre calibration : zooms ignores");
  z2.Erase("800x600@120");
  CHECK(!z2.Find("800x600@120") && z2.Find("800x600@60"), "Erase");

  // 7. EstimateZoom : la camera voit z fois la rotation du gyro (z = 1, 2, 0.5, 1.37), avec bruit et un delai de 30 ms
  const Mat3 Rcb = []{ Mat3 I; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) I.m[i][j] = i == j ? 1.0 : 0.0; return I; }();
  std::mt19937 rng(7);
  std::normal_distribution<double> noise(0.0, 0.0006);
  for (double zt : {1.0, 2.0, 0.5, 1.37}) {
    DelayEstimator de;
    std::deque<GyroSample> gy;
    const double dt = 1.0 / 200.0, delay = 0.03, cam = 1.0 / 60.0;
    // vitesse angulaire autour de l'axe z du repere camera (= corps ici) : balayage sinusoidal +-90 deg/s
    auto omega = [](double t) { return 1.6 * std::sin(2.0 * 3.14159265 * 0.45 * t); };
    for (double t = 0; t < 6.0; t += dt) { GyroSample g; g.t = t; g.w = Vec3{0, 0, omega(t)}; gy.push_back(g); }
    double tPrev = 0.5;
    for (double t = 0.5 + cam; t < 5.5; t += cam) {
      // image arrivee a t, prise a t - delay : la camera a tourne de l'integrale du gyro sur [tPrev - delay, t - delay], vue z fois plus grande
      const double wv = 2.0 * 3.14159265 * 0.45;
      const double ang = -1.6 / wv * (std::cos(wv * (t - delay)) - std::cos(wv * (tPrev - delay)));   // integrale exacte
      Mat3 dR = RotZ(zt * ang + noise(rng));
      de.AddStep(tPrev, t, dR);
      tPrev = t;
    }
    double z = 0, sp = 0, mo = 0;
    int n = 0;
    bool ok = de.EstimateZoom(gy, Rcb, delay, z, sp, n, mo);
    CHECK(ok, "z=%.2f : estimation refusee (n=%d, rotation %.2f rad)", zt, n, mo);
    CHECK(std::abs(z / zt - 1.0) < 0.03, "z vrai %.3f estime %.3f (n=%d, dispersion %.3f)", zt, z, n, sp);
    std::printf("zoom vrai %.2f -> estime %.3f  (n=%d, rotation %.1f rad, dispersion %.3f)\n", zt, z, n, mo, sp);
  }
  // trop peu de rotation : refuse
  {
    DelayEstimator de;
    std::deque<GyroSample> gy;
    for (double t = 0; t < 3; t += 0.005) { GyroSample g; g.t = t; g.w = Vec3{0, 0, 0.002}; gy.push_back(g); }
    for (double t = 0.1; t < 2.5; t += 1.0 / 60) de.AddStep(t - 1.0 / 60, t, RotZ(0.00003));
    double z, sp, mo; int n;
    CHECK(!de.EstimateZoom(gy, Rcb, 0.02, z, sp, n, mo), "sans rotation : doit refuser");
  }
  if (fails == 0) std::printf("\nTOUS LES TESTS DE MODE OK\n");
  return fails ? 1 : 0;
}
