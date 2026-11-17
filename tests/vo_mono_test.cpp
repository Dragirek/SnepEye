// Test de StereoVO sur une scene plane simulee (texture a 2 m, camera qui glisse de 30 cm sur le cote) : stereo, puis monocamera.
// Compilation : g++ -std=c++17 -I src -I <opencv>/include/opencv4 tests/vo_mono_test.cpp -lopencv_video -lopencv_calib3d -lopencv_features2d -lopencv_imgproc -lopencv_flann -lopencv_core -lz -lpthread -ldl
#include <cstdio>
#include <cmath>
#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include "tracker.h"

static cv::Mat MakeTexture() {
  cv::RNG rng(5);
  cv::Mat t(900, 1500, CV_8U);
  rng.fill(t, cv::RNG::UNIFORM, 0, 255);
  cv::GaussianBlur(t, t, cv::Size(0, 0), 3.0);
  cv::normalize(t, t, 0, 255, cv::NORM_MINMAX);
  return t;
}

// Vue d'une camera en x (m) sur un plan a Z m : decalage horizontal fx*(x-offset)/Z de la texture
static cv::Mat View(const cv::Mat& tex, double fx, double camX, double Z, cv::Size sz) {
  cv::Mat M = (cv::Mat_<double>(2, 3) << 1, 0, -fx * camX / Z - 400, 0, 1, -150);
  cv::Mat out;
  cv::warpAffine(tex, out, M, sz, cv::INTER_LINEAR, cv::BORDER_REFLECT);
  return out;
}

static double Run(bool mono, double assumed, double* refinedOut) {
  const double fx = 500, Z = 2.0, B = 0.06;
  const cv::Size sz(640, 480);
  cv::Matx33d K(fx, 0, 320, 0, fx, 240, 0, 0, 1);
  cv::Mat tex = MakeTexture();
  StereoVO vo(K, B);
  if (mono) vo.SetMono(true, assumed);
  double x = 0;
  for (int k = 0; k < 61; k++) {
    x = 0.005 * k;
    cv::Mat l = View(tex, fx, x, Z, sz);
    cv::Mat r = mono ? cv::Mat() : View(tex, fx, x + B, Z, sz);
    vo.Process(l, r, nullptr, 1.0 / 30.0);
  }
  if (refinedOut) *refinedOut = vo.nRefined();
  std::printf("%s (profondeur supposee %.2f) : etat %d, points %d, inliers %d, t = (%.3f %.3f %.3f), vrai x = %.3f\n", mono ? "mono  " : "stereo", assumed,
              (int)vo.state, vo.nPoints(), vo.nInliers, vo.t[0], vo.t[1], vo.t[2], x);
  return vo.t[0];
}

int main() {
  int bad = 0;
  double tx = Run(false, 0, nullptr);
  if (std::fabs(tx - 0.30) > 0.03) { std::printf("ECHEC stereo\n"); bad++; }
  double ref = 0;
  tx = Run(true, 2.0, &ref);
  if (std::fabs(tx - 0.30) > 0.05) { std::printf("ECHEC mono (profondeur juste)\n"); bad++; }
  std::printf("  points a profondeur mesuree : %d\n", (int)ref);
  tx = Run(true, 1.5, &ref);
  std::printf("  mono avec profondeur supposee fausse (1.5 au lieu de 2.0) : echelle obtenue %.2f (1.0 = exact)\n", tx / 0.30);
  std::printf(bad ? "ECHEC\n" : "OK\n");
  return bad;
}
