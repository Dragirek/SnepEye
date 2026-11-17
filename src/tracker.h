#pragma once
// Odométrie visuelle stéréo avec carte locale de points 3D.
// Port fidèle du prototype Python validé sur scène simulée (tests/vo_sim.py).
//
// Principe : des points sont détectés dans l'image gauche, triangulés avec
// l'image droite (stéréo rectifiée : Z = f*B/disparité), puis suivis d'image en
// image (KLT). La pose de la caméra est calculée par PnP-RANSAC sur ces points
// 3D. Un a priori de rotation (gyro de l'IMU) guide le suivi et le PnP, ce qui
// aide pendant les rotations rapides. Pas de fermeture de boucle : dérive lente.
//
// Monde du tracker "V" = repère de la 1re image (convention OpenCV).
// R, t : pose caméra -> V  (X_V = R * X_cam + t).
#include <algorithm>
#include <cmath>
#include <vector>

#include <opencv2/calib3d.hpp>
#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/video/tracking.hpp>

#include "calib.h"  // StereoCalib, ScaleK

// Intrinsèques des images RECTIFIÉES à la taille `eye` (mêmes calculs que MakeRectifier).
inline bool RectifiedIntrinsics(const StereoCalib& c, cv::Size eye, cv::Matx33d& K, double& baseline_m) {
  double sx = (double)eye.width / c.size.width, sy = (double)eye.height / c.size.height;
  if (std::abs(sx - sy) > 0.01 * sx) return false;
  cv::Mat K1 = ScaleK(c.K1, sx, sy), K2 = ScaleK(c.K2, sx, sy);
  cv::Mat R1, R2, P1, P2, Q;
  cv::stereoRectify(K1, c.D1, K2, c.D2, eye, c.R, c.T, R1, R2, P1, P2, Q, cv::CALIB_ZERO_DISPARITY, 0, eye);
  K = cv::Matx33d(P1.at<double>(0, 0), 0, P1.at<double>(0, 2),
                  0, P1.at<double>(1, 1), P1.at<double>(1, 2),
                  0, 0, 1);
  baseline_m = -P2.at<double>(0, 3) / P1.at<double>(0, 0);
  return true;
}

class StereoVO {
 public:
  enum State { kInit, kOk, kLost };

  cv::Matx33d R = cv::Matx33d::eye();  // caméra -> V
  cv::Vec3d t = cv::Vec3d(0, 0, 0);
  cv::Vec3d v = cv::Vec3d(0, 0, 0);    // vitesse (m/s) dans V, lissée
  State state = kInit;
  int nInliers = 0;

  StereoVO(const cv::Matx33d& K, double baseline_m, int maxPts = 350, double depthMax = 7.0)
      : K_(K), B_(baseline_m), maxPts_(maxPts), depthMax_(depthMax) {}

  int nPoints() const { return (int)p2_.size(); }
  // points suivis, repère V (en monocaméra : seulement ceux dont la profondeur a été mesurée par le mouvement)
  const std::vector<cv::Point3d>& Points3D() const { return mono_ ? refinedPts_ : p3_; }

  // Monocaméra : l'image de droite n'est pas utilisée. Un nouveau point reçoit d'abord une profondeur supposée (médiane de la scène déjà mesurée,
  // sinon assumedDepth_m), puis sa vraie profondeur est calculée par triangulation entre l'endroit où on l'a vu la première fois et la pose actuelle,
  // dès que la tête s'est assez déplacée. L'échelle de départ dépend donc de assumedDepth_m (réglable avec « Echelle des déplacements » dans Unity).
  void SetMono(bool on, double assumedDepth_m = 1.5) {
    mono_ = on;
    assumedDepth_ = assumedDepth_m;
  }
  bool IsMono() const { return mono_; }
  int nRefined() const { int n = 0; for (const auto& m : mi_) n += m.refined ? 1 : 0; return n; }

  void Reset() {
    R = cv::Matx33d::eye();
    t = cv::Vec3d(0, 0, 0);
    v = cv::Vec3d(0, 0, 0);
    state = kInit;
    nInliers = 0;
    prev_.release();
    p2_.clear();
    p3_.clear();
    mi_.clear();
    refinedPts_.clear();
  }

  // left/right : images rectifiées 8 bits, même taille. dRcam : rotation de la
  // caméra entre l'image précédente et celle-ci (a priori IMU) ou nullptr.
  void Process(const cv::Mat& left, const cv::Mat& right, const cv::Matx33d* dRcam, double dt) {
    if (prev_.empty()) {
      AddPoints(left, right);
      prev_ = left.clone();
      state = kOk;
      return;
    }
    // Aucun point suivi (image sombre/unie au démarrage, tous perdus) : OpenCV
    // refuse un tableau vide dans le KLT (assertion = arrêt du programme).
    // On cherche de nouveaux points et on réessaie à l'image suivante.
    if (p2_.empty() || p2_.size() != p3_.size() || (mono_ && mi_.size() != p2_.size())) {
      p2_.clear();
      p3_.clear();
      mi_.clear();
      prev_ = left.clone();
      AddPoints(left, right);
      state = kLost;
      return;
    }
    cv::Matx33d Rprior = dRcam ? R * (*dRcam) : R;
    cv::Vec3d tprior = t + v * dt;
    cv::Matx33d Rcw = Rprior.t();
    cv::Vec3d tcw = -(Rcw * tprior);

    // Prédiction 2D des points (guide le KLT pendant les rotations rapides)
    const size_t n = p2_.size();
    std::vector<cv::Point2f> nxt(n);
    for (size_t i = 0; i < n; i++) {
      cv::Vec3d pc = Rcw * cv::Vec3d(p3_[i].x, p3_[i].y, p3_[i].z) + tcw;
      nxt[i] = p2_[i];
      if (pc[2] > 0.1) {
        float u = (float)(K_(0, 0) * pc[0] / pc[2] + K_(0, 2));
        float w = (float)(K_(1, 1) * pc[1] / pc[2] + K_(1, 2));
        if (u > 0 && u < left.cols && w > 0 && w < left.rows) nxt[i] = cv::Point2f(u, w);
      }
    }
    const cv::TermCriteria crit(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 30, 0.01);
    std::vector<uchar> st, st2;
    std::vector<float> err;
    cv::calcOpticalFlowPyrLK(prev_, left, p2_, nxt, st, err, cv::Size(21, 21), 3, crit, cv::OPTFLOW_USE_INITIAL_FLOW);
    std::vector<cv::Point2f> bk;
    cv::calcOpticalFlowPyrLK(left, prev_, nxt, bk, st2, err, cv::Size(21, 21), 3, crit);

    std::vector<cv::Point2f> q2;
    std::vector<cv::Point3d> q3;
    std::vector<MonoInfo> qm;
    for (size_t i = 0; i < n; i++) {
      if (!st[i] || !st2[i]) continue;
      double dx = bk[i].x - p2_[i].x, dy = bk[i].y - p2_[i].y;
      if (dx * dx + dy * dy > 1.0) continue;
      if (nxt[i].x <= 0 || nxt[i].x >= left.cols || nxt[i].y <= 0 || nxt[i].y >= left.rows) continue;
      q2.push_back(nxt[i]);
      q3.push_back(p3_[i]);
      if (mono_) qm.push_back(mi_[i]);
    }
    p2_.swap(q2);
    p3_.swap(q3);
    if (mono_) mi_.swap(qm);

    if (p2_.size() < 12) {  // trop peu de points : on repart de zéro (pose conservée)
      state = kLost;
      prev_ = left.clone();
      p2_.clear();
      p3_.clear();
      mi_.clear();
      AddPoints(left, right);
      return;
    }

    cv::Mat rvec, tvec, Km(K_);
    cv::Rodrigues(cv::Mat(Rcw), rvec);
    tvec = (cv::Mat_<double>(3, 1) << tcw[0], tcw[1], tcw[2]);
    std::vector<int> inl;
    // Monocaméra : quand assez de points ont une profondeur mesurée, seuls ceux-là servent à la pose (les autres ont une profondeur supposée)
    std::vector<int> sel;
    if (mono_ && nRefined() >= 25) {
      for (size_t i = 0; i < mi_.size(); i++) if (mi_[i].refined) sel.push_back((int)i);
    }
    bool ok;
    if (!sel.empty()) {
      std::vector<cv::Point3d> s3;
      std::vector<cv::Point2f> s2;
      for (int i : sel) { s3.push_back(p3_[i]); s2.push_back(p2_[i]); }
      std::vector<int> inSel;
      ok = cv::solvePnPRansac(s3, s2, Km, cv::noArray(), rvec, tvec, true, 120, 2.0f, 0.995, inSel, cv::SOLVEPNP_ITERATIVE);
      for (int k : inSel) inl.push_back(sel[k]);
    } else {
      ok = cv::solvePnPRansac(p3_, p2_, Km, cv::noArray(), rvec, tvec, true, 120, 2.0f, 0.995, inl, cv::SOLVEPNP_ITERATIVE);
    }
    if (!ok || inl.size() < 12) {
      state = kLost;
      prev_ = left.clone();
      return;
    }
    std::vector<cv::Point3d> o;
    std::vector<cv::Point2f> im;
    for (int i : inl) { o.push_back(p3_[i]); im.push_back(p2_[i]); }
    cv::solvePnPRefineLM(o, im, Km, cv::noArray(), rvec, tvec);

    cv::Mat Rm;
    cv::Rodrigues(rvec, Rm);
    cv::Matx33d Rcw2(Rm.ptr<double>());
    cv::Vec3d tcw2(tvec.at<double>(0), tvec.at<double>(1), tvec.at<double>(2));
    cv::Matx33d Rnew = Rcw2.t();
    cv::Vec3d tnew = -(Rnew * tcw2);
    if (dt > 1e-4) v = 0.5 * v + 0.5 * ((tnew - t) * (1.0 / dt));
    R = Rnew;
    t = tnew;

    // On ne garde que les points cohérents avec la pose (inliers)
    std::vector<char> keep(p2_.size(), 0);
    for (int i : inl) keep[i] = 1;
    if (mono_ && !sel.empty())   // monocaméra : les points encore sans profondeur mesurée restent (ils n'ont pas servi à la pose)
      for (size_t i = 0; i < mi_.size(); i++) if (!mi_[i].refined) keep[i] = 1;
    std::vector<cv::Point2f> k2;
    std::vector<cv::Point3d> k3;
    std::vector<MonoInfo> km;
    for (size_t i = 0; i < p2_.size(); i++)
      if (keep[i]) { k2.push_back(p2_[i]); k3.push_back(p3_[i]); if (mono_) km.push_back(mi_[i]); }
    p2_.swap(k2);
    p3_.swap(k3);
    if (mono_) { mi_.swap(km); RefineMono(); }
    nInliers = (int)inl.size();
    state = kOk;
    prev_ = left.clone();
    if ((double)p2_.size() < 0.8 * maxPts_) AddPoints(left, right);
  }

 private:
  // Stéréo rectifiée : le point de gauche (u,v) est retrouvé à droite par KLT,
  // contrôlé (même ligne, aller-retour cohérent, disparité positive).
  void StereoTriangulate(const cv::Mat& left, const cv::Mat& right, const std::vector<cv::Point2f>& pts,
                         std::vector<int>& idx, std::vector<cv::Point3d>& X) const {
    idx.clear();
    X.clear();
    if (pts.empty()) return;
    const cv::TermCriteria crit(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 30, 0.01);
    std::vector<cv::Point2f> pr, pb;
    std::vector<uchar> st, st2;
    std::vector<float> err;
    cv::calcOpticalFlowPyrLK(left, right, pts, pr, st, err, cv::Size(21, 21), 3, crit);
    cv::calcOpticalFlowPyrLK(right, left, pr, pb, st2, err, cv::Size(21, 21), 3, crit);
    const double f = K_(0, 0);
    for (size_t i = 0; i < pts.size(); i++) {
      if (!st[i] || !st2[i]) continue;
      double d = pts[i].x - pr[i].x;
      double dy = std::abs(pts[i].y - pr[i].y);
      double bx = pb[i].x - pts[i].x, by = pb[i].y - pts[i].y;
      if (dy >= 1.0 || bx * bx + by * by >= 1.0 || d <= 0.8) continue;
      double z = f * B_ / d;
      if (z >= depthMax_ || z <= 0.25) continue;
      idx.push_back((int)i);
      X.push_back(cv::Point3d((pts[i].x - K_(0, 2)) * z / f, (pts[i].y - K_(1, 2)) * z / K_(1, 1), z));
    }
  }

  struct MonoInfo {
    cv::Vec3d c0;      // centre de la caméra (repère V) quand le point a été vu la première fois
    cv::Vec3d d0;      // direction du rayon à ce moment (repère V, unitaire)
    bool refined;      // profondeur mesurée par triangulation
  };

  cv::Vec3d RayV(const cv::Point2f& p) const {
    cv::Vec3d r(((double)p.x - K_(0, 2)) / K_(0, 0), ((double)p.y - K_(1, 2)) / K_(1, 1), 1.0);
    cv::Vec3d w = R * r;
    return w * (1.0 / std::max(1e-12, std::sqrt(w.dot(w))));
  }

  // Profondeur typique de la scène vue maintenant : médiane des points déjà mesurés, sinon la profondeur supposée.
  double SceneDepth() const {
    std::vector<double> z;
    const cv::Matx33d Rcw = R.t();
    for (size_t i = 0; i < mi_.size(); i++) {
      if (!mi_[i].refined) continue;
      const cv::Vec3d pc = Rcw * (cv::Vec3d(p3_[i].x, p3_[i].y, p3_[i].z) - t);
      if (pc[2] > 0.2 && pc[2] < 15.0) z.push_back(pc[2]);
    }
    if (z.size() < 8) return assumedDepth_;
    std::nth_element(z.begin(), z.begin() + z.size() / 2, z.end());
    return z[z.size() / 2];
  }

  void AddPointsMono(const cv::Mat& left) {
    int need = maxPts_ - (int)p2_.size();
    if (need <= 10) return;
    cv::Mat mask(left.size(), CV_8U, cv::Scalar(255));
    for (const auto& p : p2_) cv::circle(mask, cv::Point((int)p.x, (int)p.y), 14, cv::Scalar(0), -1);
    std::vector<cv::Point2f> c;
    cv::goodFeaturesToTrack(left, c, need, 0.01, 10, mask);
    if (c.empty()) return;
    const double z = SceneDepth();
    for (const auto& p : c) {
      const cv::Vec3d Xc(((double)p.x - K_(0, 2)) / K_(0, 0) * z, ((double)p.y - K_(1, 2)) / K_(1, 1) * z, z);
      const cv::Vec3d Xw = R * Xc + t;
      p2_.push_back(p);
      p3_.push_back(cv::Point3d(Xw[0], Xw[1], Xw[2]));
      mi_.push_back(MonoInfo{t, RayV(p), false});
    }
  }

  // Triangulation entre le rayon de la première vue et le rayon actuel (points les plus proches de deux droites). Acceptée seulement si la tête s'est
  // assez déplacée (>= 2 cm, >= 1 degré de parallaxe) et si les deux rayons se croisent presque (écart < 3 % de la distance).
  void RefineMono() {
    refinedPts_.clear();
    for (size_t i = 0; i < mi_.size(); i++) {
      MonoInfo& m = mi_[i];
      if (!m.refined) {
        const cv::Vec3d d1 = RayV(p2_[i]);
        const cv::Vec3d w0 = m.c0 - t;
        const double b = m.d0.dot(d1);
        if (cv::norm(t - m.c0) < 0.02 || b > 0.99985) continue;
        const double den = 1.0 - b * b;
        if (den < 1e-9) continue;
        const double dd = m.d0.dot(w0), ee = d1.dot(w0);
        const double s0 = (b * ee - dd) / den, s1 = (ee - b * dd) / den;
        if (s0 < 0.2 || s1 < 0.2 || s0 > 20.0 || s1 > 20.0) continue;
        const cv::Vec3d P = m.c0 + m.d0 * s0, Q = t + d1 * s1;
        if (cv::norm(P - Q) > 0.03 * s1) continue;
        const cv::Vec3d X = 0.5 * (P + Q);
        p3_[i] = cv::Point3d(X[0], X[1], X[2]);
        m.refined = true;
      }
      if (m.refined) refinedPts_.push_back(p3_[i]);
    }
  }

  void AddPoints(const cv::Mat& left, const cv::Mat& right) {
    if (mono_) { AddPointsMono(left); return; }
    int need = maxPts_ - (int)p2_.size();
    if (need <= 10) return;
    cv::Mat mask(left.size(), CV_8U, cv::Scalar(255));
    for (const auto& p : p2_) cv::circle(mask, cv::Point((int)p.x, (int)p.y), 14, cv::Scalar(0), -1);
    std::vector<cv::Point2f> c;
    cv::goodFeaturesToTrack(left, c, need, 0.01, 10, mask);
    if (c.empty()) return;
    std::vector<int> idx;
    std::vector<cv::Point3d> X;
    StereoTriangulate(left, right, c, idx, X);
    for (size_t k = 0; k < idx.size(); k++) {
      cv::Vec3d Xw = R * cv::Vec3d(X[k].x, X[k].y, X[k].z) + t;
      p2_.push_back(c[idx[k]]);
      p3_.push_back(cv::Point3d(Xw[0], Xw[1], Xw[2]));
    }
  }

  cv::Matx33d K_;
  double B_;
  int maxPts_;
  double depthMax_;
  cv::Mat prev_;
  std::vector<cv::Point2f> p2_;
  std::vector<cv::Point3d> p3_;
  bool mono_ = false;
  double assumedDepth_ = 1.5;
  std::vector<MonoInfo> mi_;                 // en monocaméra : une entrée par point de p2_ / p3_
  std::vector<cv::Point3d> refinedPts_;
};
