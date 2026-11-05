#pragma once
// Calibration stéréo : format NATIF (ar6dof_calib.yml, fait par calib_session.h) ou importé de RTAB-Map (*_left.yaml, *_right.yaml,
// *_pose.yaml). Ce fichier charge, convertit, met à l'échelle, rectifie et contrôle.
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
  // Renseignés par la calibration maison (ar6dof_calib.yml) ; valeurs « inconnu » pour un import RTAB-Map.
  std::string source = "rtabmap";   // "ar6dof" ou "rtabmap"
  std::string model;                // modèle de distorsion retenu (texte)
  std::string created;              // date de la calibration
  int fps = 0, views = 0;           // cadence du mode caméra utilisé, nombre de vues gardées
  double rmsL = -1, rmsR = -1, rmsStereo = -1, epiPx = -1;   // erreurs de reprojection (px) et écart vertical gauche/droite après rectification
  double squareMm = 0;              // taille d'un carreau de la mire utilisée (mm)
  double scaleCorr = 1.0;           // correction d'échelle appliquée à T après la vérification à la règle
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

// ---- Format natif : ar6dof_calib.yml (OpenCV FileStorage) --------------------------------------------------------------------------
// Une seule fichier, avec la résolution/cadence du mode caméra, le modèle de distorsion et les mesures de qualité. Les matrices sont
// celles d'OpenCV (stereoCalibrate : x_droite = R * x_gauche + T, T en mètres).
inline const char* NativeCalibName() { return "ar6dof_calib.yml"; }

inline bool SaveNativeCalib(const StereoCalib& c, const std::string& path, std::string& err) {
  try {
    std::string tmp = path + ".tmp";
    {
      cv::FileStorage fs(tmp, cv::FileStorage::WRITE);
      if (!fs.isOpened()) { err = "impossible d'ecrire " + path; return false; }
      fs << "format" << "ar6dof_stereo_calib";
      fs << "version" << 2;
      fs << "image_width" << c.size.width << "image_height" << c.size.height;
      fs << "K1" << c.K1 << "D1" << c.D1 << "K2" << c.K2 << "D2" << c.D2 << "R" << c.R << "T" << c.T;
      fs << "source" << c.source << "model" << c.model << "created" << c.created;
      fs << "fps" << c.fps << "views" << c.views;
      fs << "rms_left" << c.rmsL << "rms_right" << c.rmsR << "rms_stereo" << c.rmsStereo << "epipolar_px" << c.epiPx;
      fs << "square_mm" << c.squareMm << "scale_correction" << c.scaleCorr;
      fs << "baseline_mm" << cv::norm(c.T) * 1000.0;
    }
    std::error_code ec;
    std::filesystem::remove(path, ec);
    std::filesystem::rename(tmp, path, ec);
    if (ec) { err = "impossible de remplacer " + path + " : " + ec.message(); return false; }
    return true;
  } catch (const cv::Exception& e) {
    err = std::string("ecriture de la calibration : ") + e.what();
    return false;
  }
}

inline bool LoadNativeCalib(const std::string& path, StereoCalib& out, std::string& err) {
  try {
    cv::FileStorage fs(path, cv::FileStorage::READ);
    if (!fs.isOpened()) { err = "impossible de lire " + path; return false; }
    std::string fmt;
    fs["format"] >> fmt;
    if (fmt != "ar6dof_stereo_calib") { err = "ce n'est pas une calibration AR6DoF : " + path; return false; }
    StereoCalib c;
    c.size = cv::Size((int)fs["image_width"], (int)fs["image_height"]);
    fs["K1"] >> c.K1; fs["D1"] >> c.D1; fs["K2"] >> c.K2; fs["D2"] >> c.D2; fs["R"] >> c.R; fs["T"] >> c.T;
    c.K1.convertTo(c.K1, CV_64F); c.K2.convertTo(c.K2, CV_64F); c.D1.convertTo(c.D1, CV_64F);
    c.D2.convertTo(c.D2, CV_64F); c.R.convertTo(c.R, CV_64F); c.T.convertTo(c.T, CV_64F);
    if (c.T.total() == 3) c.T = c.T.reshape(1, 3).clone();
    if (c.D1.total() > 0) c.D1 = c.D1.reshape(1, 1).clone();   // 1 x n, comme les fichiers RTAB-Map
    if (c.D2.total() > 0) c.D2 = c.D2.reshape(1, 1).clone();
    if (c.size.width <= 0 || c.size.height <= 0 || c.K1.rows != 3 || c.K1.cols != 3 || c.K2.rows != 3 || c.K2.cols != 3 ||
        c.D1.empty() || c.D2.empty() || c.R.rows != 3 || c.R.cols != 3 || c.T.rows != 3 || c.T.cols != 1) {
      err = "calibration AR6DoF incomplete ou illisible : " + path;
      return false;
    }
    fs["source"] >> c.source; fs["model"] >> c.model; fs["created"] >> c.created;
    c.fps = (int)fs["fps"]; c.views = (int)fs["views"];
    c.rmsL = (double)fs["rms_left"]; c.rmsR = (double)fs["rms_right"]; c.rmsStereo = (double)fs["rms_stereo"]; c.epiPx = (double)fs["epipolar_px"];
    c.squareMm = (double)fs["square_mm"];
    double sc = (double)fs["scale_correction"];
    c.scaleCorr = sc > 0 ? sc : 1.0;
    c.files = std::filesystem::path(path).filename().string();
    out = c;
    return true;
  } catch (const cv::Exception& e) {
    err = std::string("lecture de la calibration : ") + e.what();
    return false;
  }
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

// Charge la calibration d'un dossier : le fichier natif ar6dof_calib.yml s'il existe, sinon les fichiers RTAB-Map. Si le natif est
// illisible mais que RTAB-Map est là, on retombe dessus (et on le dit dans 'files').
inline bool LoadCalibAuto(const std::string& dir, StereoCalib& out, std::string& err) {
  namespace fs = std::filesystem;
  std::error_code ec;
  fs::path nat = fs::path(dir) / NativeCalibName();
  if (fs::exists(nat, ec)) {
    std::string e1;
    if (LoadNativeCalib(nat.string(), out, e1)) return true;
    std::string e2;
    if (LoadRtabmapStereo(dir, out, e2)) { out.files += " (" + std::string(NativeCalibName()) + " illisible : " + e1 + ")"; return true; }
    err = e1;
    return false;
  }
  return LoadRtabmapStereo(dir, out, err);
}

// Import RTAB-Map -> format natif (garde les anciens fichiers intacts ; l'ancien natif est sauvegardé en .prev.yml).
inline bool ConvertRtabmapToNative(const std::string& dir, StereoCalib& out, std::string& err) {
  if (!LoadRtabmapStereo(dir, out, err)) return false;
  out.source = "rtabmap";
  out.model = out.D1.total() >= 8 ? "rationnel (RTAB-Map)" : "standard (RTAB-Map)";
  std::error_code ec;
  std::filesystem::path nat = std::filesystem::path(dir) / NativeCalibName();
  if (std::filesystem::exists(nat, ec)) {
    std::filesystem::path prev = std::filesystem::path(dir) / "ar6dof_calib.prev.yml";
    std::filesystem::remove(prev, ec);
    std::filesystem::copy_file(nat, prev, ec);
  }
  return SaveNativeCalib(out, nat.string(), err);
}

// ---- Un seul jeu d'intrinseques pour TOUS les modes de la camera ---------------------------------------------------------------------
// Une calibration est faite dans UN mode (taille d'image par oeil + cadence). Un autre mode de la meme camera est soit une version REDUITE de
// l'image (meme champ de vision : la focale en pixels suit la taille), soit un RECADRAGE du centre du capteur (focale en pixels inchangee).
// On ne peut pas le deviner : on part de « reduit » (zoom 1) et un « zoom du mode » est APPRIS (rotation vue par la camera / rotation du gyro,
// voir DelayEstimator::EstimateZoom) ou mesure avec la mire, puis garde par mode dans mode_zoom.yml. La distorsion est exprimee en coordonnees
// normalisees (rayon / focale) : elle ne change pas. La base T est une longueur physique : elle ne change pas non plus.
struct ModeRel {
  double sx = 1, sy = 1;    // rapports de taille (mode utilise / mode calibre)
  double scale = 1;         // rapport retenu pour la focale (si les formats different : celui de la hauteur)
  double zoom = 1;          // zoom du mode applique en plus
  bool sameSize = true;     // meme taille d'image que la calibration
  bool sameAspect = true;
};

inline std::string ModeKey(cv::Size eye, int fps) { return std::to_string(eye.width) + "x" + std::to_string(eye.height) + "@" + std::to_string(fps); }

// Calibration exprimee dans le mode `eye` : focales * (rapport de taille * zoom), centre optique replace par rapport au centre de l'image.
inline StereoCalib ResolveCalib(const StereoCalib& c, cv::Size eye, double zoom, ModeRel* rel = nullptr) {
  ModeRel r;
  r.sx = (double)eye.width / std::max(1, c.size.width);
  r.sy = (double)eye.height / std::max(1, c.size.height);
  r.sameAspect = std::abs(r.sx - r.sy) <= 0.01 * r.sx;
  r.sameSize = eye == c.size;
  r.scale = r.sameAspect ? 0.5 * (r.sx + r.sy) : r.sy;
  r.zoom = zoom > 0 ? zoom : 1.0;
  StereoCalib o = c;
  o.size = eye;
  const double k = r.scale * r.zoom;
  auto mk = [&](const cv::Mat& K) {
    cv::Mat m = K.clone();
    m.at<double>(0, 0) *= k;
    m.at<double>(1, 1) *= k;
    m.at<double>(0, 2) = (eye.width - 1) * 0.5 + (K.at<double>(0, 2) - (c.size.width - 1) * 0.5) * k;
    m.at<double>(1, 2) = (eye.height - 1) * 0.5 + (K.at<double>(1, 2) - (c.size.height - 1) * 0.5) * k;
    return m;
  };
  o.K1 = mk(c.K1);
  o.K2 = mk(c.K2);
  if (rel) *rel = r;
  return o;
}

// Identifiant d'une calibration : sert a invalider les zooms appris quand la calibration change.
inline std::string CalibId(const StereoCalib& c) {
  char b[160];
  std::snprintf(b, sizeof(b), "%s|%dx%d|%.3f|%.3f", c.created.c_str(), c.size.width, c.size.height, c.K1.empty() ? 0.0 : c.K1.at<double>(0, 0),
                c.T.empty() ? 0.0 : cv::norm(c.T) * 1000.0);
  return b;
}

struct ZoomEntry {
  std::string key, source;   // source : "auto", "manual"
  double zoom = 1.0;
  int samples = 0;
};

// mode_zoom.yml : un zoom par mode (taille par oeil @ cadence), valable pour UNE calibration (CalibId).
struct ZoomStore {
  std::string calibId;
  std::vector<ZoomEntry> e;

  const ZoomEntry* Find(const std::string& key) const {
    for (const auto& z : e) if (z.key == key) return &z;
    return nullptr;
  }
  void Set(const ZoomEntry& n) {
    for (auto& z : e) if (z.key == n.key) { z = n; return; }
    e.push_back(n);
  }
  void Erase(const std::string& key) {
    for (size_t i = 0; i < e.size(); i++) if (e[i].key == key) { e.erase(e.begin() + i); return; }
  }
  // Charge le fichier ; s'il appartient a une autre calibration, il est ignore (liste vide).
  bool Load(const std::string& path, const std::string& id) {
    e.clear();
    calibId = id;
    try {
      cv::FileStorage fs(path, cv::FileStorage::READ);
      if (!fs.isOpened()) return false;
      std::string fid;
      fs["calib_id"] >> fid;
      if (fid != id) return false;
      cv::FileNode n = fs["modes"];
      for (auto it = n.begin(); it != n.end(); ++it) {
        ZoomEntry z;
        (*it)["key"] >> z.key;
        z.zoom = (double)(*it)["zoom"];
        z.samples = (int)(*it)["samples"];
        (*it)["source"] >> z.source;
        if (!z.key.empty() && z.zoom > 0.2 && z.zoom < 5.0) e.push_back(z);
      }
      return true;
    } catch (const cv::Exception&) {
      e.clear();
      return false;
    }
  }
  bool Save(const std::string& path) const {
    try {
      const std::string tmp = path + ".tmp";
      {
        cv::FileStorage fs(tmp, cv::FileStorage::WRITE);
        if (!fs.isOpened()) return false;
        fs << "format" << "ar6dof_mode_zoom" << "calib_id" << calibId << "modes" << "[";
        for (const auto& z : e) fs << "{" << "key" << z.key << "zoom" << z.zoom << "samples" << z.samples << "source" << z.source << "}";
        fs << "]";
      }
      std::error_code ec;
      std::filesystem::remove(path, ec);
      std::filesystem::rename(tmp, path, ec);
      return !ec;
    } catch (const cv::Exception&) {
      return false;
    }
  }
};

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
                           " : FAUX si ce mode de la camera RECADRE le centre au lieu de reduire l'image (distances et deplacements"
                           " sous-estimes d'un facteur " + std::to_string(1.0 / sx).substr(0, 3) + "). Refais la calibration dans le mode utilise");
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
