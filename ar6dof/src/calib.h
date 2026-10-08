#pragma once
// Calibration stéréo faite AILLEURS (RTAB-Map : *_left.yaml, *_right.yaml,
// *_pose.yaml). Ce fichier charge, met à l'échelle, rectifie et contrôle.
// Pour supporter un autre format plus tard (Kalibr, etc.), il suffit d'ajouter
// une fonction qui remplit StereoCalib.
#include <algorithm>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <string>
#include <vector>

#include <opencv2/calib3d.hpp>
#include <opencv2/core.hpp>
#include <opencv2/features2d.hpp>
#include <opencv2/imgproc.hpp>

struct StereoCalib {
  cv::Size size;           // résolution PAR OEIL à laquelle la calibration a été faite
  cv::Mat K1, D1, K2, D2;  // intrinsèques / distorsion gauche et droite (64F)
  cv::Mat R, T;            // transformation caméra gauche -> droite (OpenCV stereoCalibrate)
  std::string files;
};

struct Rectifier {
  cv::Size eye;
  cv::Mat mapL1, mapL2, mapR1, mapR2;
  double f = 0, baseline_mm = 0, hfov_deg = 0;
  std::vector<std::string> warnings;
};

// Les fichiers RTAB-Map stockent les matrices en {rows, cols, data:[...]}
inline cv::Mat ReadYamlMat(const cv::FileNode& n) {
  if (n.empty()) return cv::Mat();
  int r = (int)n["rows"], c = (int)n["cols"];
  std::vector<double> d;
  n["data"] >> d;
  if (r <= 0 || c <= 0 || (int)d.size() != r * c) return cv::Mat();
  return cv::Mat(r, c, CV_64F, d.data()).clone();
}

inline bool ReadCamYaml(const std::string& path, cv::Size& size, cv::Mat& K,
                        cv::Mat& D, std::string& err) {
  cv::FileStorage fs(path, cv::FileStorage::READ);
  if (!fs.isOpened()) { err = "impossible de lire " + path; return false; }
  size = cv::Size((int)fs["image_width"], (int)fs["image_height"]);
  K = ReadYamlMat(fs["camera_matrix"]);
  D = ReadYamlMat(fs["distortion_coefficients"]);
  std::string model;
  fs["distortion_model"] >> model;
  if (size.width <= 0 || K.rows != 3 || K.cols != 3 || D.empty()) {
    err = "fichier incomplet ou illisible : " + path;
    return false;
  }
  if (!model.empty() && model != "rational_polynomial" && model != "plumb_bob") {
    err = "modele de distorsion '" + model +
          "' non supporte (supportes : rational_polynomial, plumb_bob)";
    return false;
  }
  return true;
}

inline bool LoadRtabmapStereo(const std::string& dir, StereoCalib& out, std::string& err) {
  namespace fs = std::filesystem;
  std::string fl, fr, fp;
  std::error_code ec;
  for (auto& e : fs::directory_iterator(dir, ec)) {
    std::string name = e.path().filename().string();
    auto ends = [&](const char* suf) {
      size_t n = std::strlen(suf);
      return name.size() >= n && name.compare(name.size() - n, n, suf) == 0;
    };
    if (ends("_left.yaml")) fl = e.path().string();
    else if (ends("_right.yaml")) fr = e.path().string();
    else if (ends("_pose.yaml")) fp = e.path().string();
  }
  if (ec) { err = "dossier introuvable : " + dir; return false; }
  if (fl.empty() || fr.empty() || fp.empty()) {
    err = "il faut un *_left.yaml, un *_right.yaml et un *_pose.yaml dans " + dir;
    return false;
  }
  cv::Size s2;
  if (!ReadCamYaml(fl, out.size, out.K1, out.D1, err)) return false;
  if (!ReadCamYaml(fr, s2, out.K2, out.D2, err)) return false;
  if (s2 != out.size) { err = "gauche et droite n'ont pas la meme taille d'image"; return false; }

  cv::FileStorage ps(fp, cv::FileStorage::READ);
  if (!ps.isOpened()) { err = "impossible de lire " + fp; return false; }
  out.R = ReadYamlMat(ps["rotation_matrix"]);
  out.T = ReadYamlMat(ps["translation_matrix"]);
  if (out.R.rows != 3 || out.R.cols != 3 || out.T.rows != 3 || out.T.cols != 1) {
    err = "pose.yaml : rotation_matrix / translation_matrix invalides";
    return false;
  }
  out.files = std::filesystem::path(fl).filename().string() + " + _right + _pose";
  return true;
}

// Met K à l'échelle (convention centre-de-pixel d'OpenCV).
inline cv::Mat ScaleK(const cv::Mat& K, double sx, double sy) {
  cv::Mat k = K.clone();
  k.at<double>(0, 0) *= sx;
  k.at<double>(1, 1) *= sy;
  k.at<double>(0, 2) = (K.at<double>(0, 2) + 0.5) * sx - 0.5;
  k.at<double>(1, 2) = (K.at<double>(1, 2) + 0.5) * sy - 0.5;
  return k;
}

inline bool MakeRectifier(const StereoCalib& c, cv::Size eye, Rectifier& out, std::string& err) {
  double sx = (double)eye.width / c.size.width;
  double sy = (double)eye.height / c.size.height;
  if (std::abs(sx - sy) > 0.01 * sx) {
    err = "format d'image different de la calibration (" + std::to_string(c.size.width) + "x" +
          std::to_string(c.size.height) + " -> " + std::to_string(eye.width) + "x" +
          std::to_string(eye.height) + ") : mise a l'echelle impossible, refais la calibration";
    return false;
  }
  out = Rectifier();
  out.eye = eye;
  cv::Mat K1 = ScaleK(c.K1, sx, sy), K2 = ScaleK(c.K2, sx, sy);

  cv::Mat R1, R2, P1, P2, Q;
  cv::stereoRectify(K1, c.D1, K2, c.D2, eye, c.R, c.T, R1, R2, P1, P2, Q,
                    cv::CALIB_ZERO_DISPARITY, 0, eye);
  cv::initUndistortRectifyMap(K1, c.D1, R1, P1, eye, CV_16SC2, out.mapL1, out.mapL2);
  cv::initUndistortRectifyMap(K2, c.D2, R2, P2, eye, CV_16SC2, out.mapR1, out.mapR2);

  out.f = P1.at<double>(0, 0);
  out.baseline_mm = cv::norm(c.T) * 1000.0;
  out.hfov_deg = 2.0 * std::atan(eye.width * 0.5 / out.f) * 180.0 / CV_PI;

  // Signaux d'alerte sur la calibration elle-même
  if (std::abs(sx - 1.0) > 1e-6)
    out.warnings.push_back("calibration faite en " + std::to_string(c.size.width) + "x" +
                           std::to_string(c.size.height) + ", mise a l'echelle x" +
                           std::to_string(sx).substr(0, 4) +
                           " : valable seulement si le mode camera a le meme champ de vision");
  double offx = std::abs(c.K1.at<double>(0, 2) - c.size.width * 0.5) / c.size.width;
  double offy = std::abs(c.K1.at<double>(1, 2) - c.size.height * 0.5) / c.size.height;
  if (offx > 0.05 || offy > 0.05)
    out.warnings.push_back("centre optique tres decale du centre de l'image (gauche) : suspect");
  double dcy = std::abs(c.K1.at<double>(1, 2) - c.K2.at<double>(1, 2)) / c.size.height;
  if (dcy > 0.01)
    out.warnings.push_back("centre optique vertical tres different entre les deux yeux : suspect");
  const int radial[] = {0, 1, 4, 5, 6, 7};
  bool big = false;
  for (int i : radial) {
    if (i < c.D1.cols && std::abs(c.D1.at<double>(0, i)) > 1.0) big = true;
    if (i < c.D2.cols && std::abs(c.D2.at<double>(0, i)) > 1.0) big = true;
  }
  if (big) out.warnings.push_back("coefficients de distorsion tres grands : sur-ajustement probable");
  if (c.D1.cols > 0 && c.D2.cols > 0 &&
      std::abs(c.D1.at<double>(0, 0) - c.D2.at<double>(0, 0)) > 0.15)
    out.warnings.push_back("distorsion tres differente entre gauche et droite (deux optiques identiques)");
  return true;
}

// Mesure objective : sur des images RECTIFIEES, un meme point doit etre sur la
// meme ligne dans les deux images. On mesure l'ecart vertical median (en pixels).
inline std::string CheckRectification(const cv::Mat& rl, const cv::Mat& rr) {
  cv::Mat gl, gr;
  if (rl.channels() == 3) cv::cvtColor(rl, gl, cv::COLOR_BGR2GRAY); else gl = rl;
  if (rr.channels() == 3) cv::cvtColor(rr, gr, cv::COLOR_BGR2GRAY); else gr = rr;
  auto orb = cv::ORB::create(2000);
  std::vector<cv::KeyPoint> kl, kr;
  cv::Mat dl, dr;
  orb->detectAndCompute(gl, cv::noArray(), kl, dl);
  orb->detectAndCompute(gr, cv::noArray(), kr, dr);
  if (dl.empty() || dr.empty()) return "controle impossible : image sans details";
  cv::BFMatcher bf(cv::NORM_HAMMING, true);
  std::vector<cv::DMatch> m;
  bf.match(dl, dr, m);
  std::vector<double> dys;
  for (auto& x : m) {
    if (x.distance > 50) continue;
    const cv::Point2f& a = kl[x.queryIdx].pt;
    const cv::Point2f& b = kr[x.trainIdx].pt;
    if (a.x - b.x < 0) continue;  // un objet reel a une disparite positive
    dys.push_back(std::abs(a.y - b.y));
  }
  if (dys.size() < 30)
    return "controle : trop peu de correspondances (" + std::to_string(dys.size()) +
           "), vise une scene riche en details a 1-3 m";
  std::sort(dys.begin(), dys.end());
  double med = dys[dys.size() / 2];
  size_t ok = 0;
  for (double d : dys) if (d <= 1.0) ++ok;
  char buf[200];
  std::snprintf(buf, sizeof(buf), "ecart vertical median: %.2f px, %d%% sous 1 px (%d pts) -> %s",
                med, (int)(100.0 * ok / dys.size()), (int)dys.size(),
                med < 0.5 ? "EXCELLENT" : med < 1.0 ? "BON" : med < 2.0 ? "MOYEN" : "MAUVAIS, a refaire");
  return buf;
}
