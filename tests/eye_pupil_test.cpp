// Precision du detecteur de pupille (src/eye_pupil.h) sur des yeux simules : pupille sombre dans un iris, peau claire, reflet IR, bruit, paupiere.
// Compare l'ancien calcul (centre de l'enveloppe convexe) et l'affinage du contour au sous-pixel.
// g++ -std=c++17 -I src -I <opencv>/include/opencv4 tests/eye_pupil_test.cpp -lopencv_imgproc -lopencv_core -lz -lpthread -ldl
#include <cstdio>
#include <cmath>
#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include "eye_pupil.h"

// Pixel (i) de l'image finale = centre du bloc 4x4 : coordonnee grande = 4*x + 1.5. On dessine avec 4 bits sous-pixel.
static void Disc(cv::Mat& m, double x, double y, double r, int v) {
  const int S = 4;
  cv::circle(m, cv::Point((int)std::lround((4 * x + 1.5) * 16), (int)std::lround((4 * y + 1.5) * 16)), (int)std::lround(r * 4 * 16), cv::Scalar(v), -1, cv::LINE_AA, S);
}

static cv::Mat Render(double cx, double cy, double rp, bool eyelid, cv::RNG& rng, double* gxOut, double* gyOut) {
  const int W = 320, H = 240;
  cv::Mat big(H * 4, W * 4, CV_8U, cv::Scalar(150));   // sur-echantillonne x4 pour un contour precis
  Disc(big, cx, cy, rp * 2.8, 85);   // iris
  Disc(big, cx, cy, rp, 18);         // pupille
  const double gx = cx + 0.35 * rp, gy = cy - 0.3 * rp;
  Disc(big, gx, gy, 2.0 / 4, 255);   // reflet
  if (eyelid) cv::rectangle(big, cv::Point(0, 0), cv::Point(W * 4, (int)std::lround((cy - 0.75 * rp) * 4 + 1.5)), cv::Scalar(150), -1);   // paupiere : cache le haut de la pupille
  cv::Mat img;
  cv::resize(big, img, cv::Size(W, H), 0, 0, cv::INTER_AREA);
  cv::GaussianBlur(img, img, cv::Size(0, 0), 1.2);
  cv::Mat noise(img.size(), CV_8S);
  rng.fill(noise, cv::RNG::NORMAL, 0, 3);
  cv::Mat n16, i16;
  img.convertTo(i16, CV_16S);
  noise.convertTo(n16, CV_16S);
  i16 += n16;
  i16.convertTo(img, CV_8U);
  if (gxOut) { *gxOut = gx; *gyOut = gy; }
  return img;
}

static double Rms(bool refine, bool eyelid, int* found) {
  cv::RNG rng(11), pos(3);
  eye::PupilConfig cfg;
  cfg.refine = refine;
  double se = 0;
  int n = 0, ok = 0;
  for (int k = 0; k < 150; k++) {
    eye::PupilDetector det(cfg);
    const double cx = 120 + pos.uniform(0.0, 80.0), cy = 90 + pos.uniform(0.0, 60.0), rp = 13 + pos.uniform(0.0, 5.0);
    cv::Mat img = Render(cx, cy, rp, eyelid, rng, nullptr, nullptr);
    eye::PupilResult r = det.Detect(img);
    n++;
    if (!r.found) continue;
    ok++;
    se += (r.px - cx) * (r.px - cx) + (r.py - cy) * (r.py - cy);
  }
  *found = ok;
  return ok ? std::sqrt(se / ok) : 99.0;
}

int main() {
  int bad = 0, f0, f1;
  double a0 = Rms(false, false, &f0), a1 = Rms(true, false, &f1);
  std::printf("pupille entiere   : erreur RMS %.3f px (ancien) -> %.3f px (affine), trouvee %d / %d\n", a0, a1, f0, f1);
  if (a1 > a0 + 0.02 || a1 > 0.35 || f1 < 140) { std::printf("ECHEC pupille entiere\n"); bad++; }
  double b0 = Rms(false, true, &f0), b1 = Rms(true, true, &f1);
  std::printf("paupiere sur 1/8 : erreur RMS %.3f px (ancien) -> %.3f px (affine), trouvee %d / %d\n", b0, b1, f0, f1);
  if (b1 > b0 + 0.05 || f1 < 140) { std::printf("ECHEC paupiere\n"); bad++; }
  std::printf(bad ? "ECHEC\n" : "OK\n");
  return bad;
}
