#pragma once
// Exporte le modèle de caméra (calibration) en JSON pour Unity : intrinsèques, distorsion et
// rotation de rectification de chaque oeil. Unity s'en sert pour corriger l'image du passthrough.
#include <fstream>
#include <iomanip>
#include <sstream>
#include <string>

#include <opencv2/calib3d.hpp>
#include <opencv2/core.hpp>

#include "calib.h"

inline std::string JsonVec(const cv::Mat& m, int n) {
  cv::Mat d;
  m.convertTo(d, CV_64F);
  d = d.reshape(1, 1);
  std::ostringstream os;
  os << std::setprecision(12) << "[";
  for (int i = 0; i < n; i++) os << (i ? "," : "") << (i < d.cols ? d.at<double>(0, i) : 0.0);
  os << "]";
  return os.str();
}

inline bool ExportCameraModel(const StereoCalib& c, const std::string& path) {
  cv::Mat R1, R2, P1, P2, Q;
  cv::stereoRectify(c.K1, c.D1, c.K2, c.D2, c.size, c.R, c.T, R1, R2, P1, P2, Q, cv::CALIB_ZERO_DISPARITY, 0, c.size);
  double right_offset = -P2.at<double>(0, 3) / P1.at<double>(0, 0);  // position de l'oeil droit sur l'axe x rectifié (m, signée)
  std::ofstream f(path);
  if (!f) return false;
  f << std::setprecision(12);
  f << "{\n  \"version\": 1,\n  \"calib_width\": " << c.size.width << ",\n  \"calib_height\": " << c.size.height
    << ",\n  \"baseline_m\": " << std::abs(right_offset) << ",\n  \"right_offset_x_m\": " << right_offset
    << ",\n  \"rect_f\": " << P1.at<double>(0, 0) << ",\n  \"rect_cx\": " << P1.at<double>(0, 2)
    << ",\n  \"rect_cy\": " << P1.at<double>(1, 2) << ",\n  \"left\": {\"K\": " << JsonVec(c.K1, 9)
    << ", \"D\": " << JsonVec(c.D1, 8) << ", \"R\": " << JsonVec(R1, 9) << "},\n  \"right\": {\"K\": " << JsonVec(c.K2, 9)
    << ", \"D\": " << JsonVec(c.D2, 8) << ", \"R\": " << JsonVec(R2, 9) << "}\n}\n";
  return true;
}
