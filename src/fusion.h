#pragma once
// Outils de fusion IMU <-> vision (sans dépendance, testables partout).
//
// Repères :
//   casque  : X avant, Y gauche, Z haut                (celui de l'IMU après calibration du montage)
//   caméra  : x droite, y bas, z avant (OpenCV)
//   V       : repère de la 1re image (monde du tracker visuel, convention caméra)
//   G       : monde final aligné sur la gravité (Z haut, X = direction initiale du regard)
#include <algorithm>
#include <cmath>
#include <deque>
#include <vector>

#include "orientation.h"

// exp(phi) : rotation de vecteur-rotation phi (formule de Rodrigues).
inline Mat3 ExpMap(const Vec3& phi) {
  double th = Norm(phi);
  Mat3 R;
  if (th < 1e-12) return R;
  Vec3 k = phi * (1.0 / th);
  double c = std::cos(th), s = std::sin(th), C = 1 - c;
  R.m[0][0] = c + k.x * k.x * C;       R.m[0][1] = k.x * k.y * C - k.z * s; R.m[0][2] = k.x * k.z * C + k.y * s;
  R.m[1][0] = k.y * k.x * C + k.z * s; R.m[1][1] = c + k.y * k.y * C;       R.m[1][2] = k.y * k.z * C - k.x * s;
  R.m[2][0] = k.z * k.x * C - k.y * s; R.m[2][1] = k.z * k.y * C + k.x * s; R.m[2][2] = c + k.z * k.z * C;
  return R;
}

// Camera regardant vers l'avant du casque, axes alignés : v_cam = R_cb * v_casque.
inline Mat3 CamFromBody() {
  Mat3 r;
  for (int i = 0; i < 3; i++)
    for (int j = 0; j < 3; j++) r.m[i][j] = 0;
  r.m[0][1] = -1;  // x_cam (droite) = -Y_casque (gauche)
  r.m[1][2] = -1;  // y_cam (bas)    = -Z_casque (haut)
  r.m[2][0] = 1;   // z_cam (avant)  = +X_casque (avant)
  return r;
}

struct GyroSample { double t; Vec3 w; };  // t en secondes (horloge PC), w en rad/s, repère casque

// Rotation du casque entre t0 et t1 (exprimée dans le repère casque de t0),
// en intégrant le gyro. Au-delà du dernier échantillon, on extrapole avec lui.
inline Mat3 IntegrateGyro(const std::deque<GyroSample>& s, double t0, double t1) {
  Mat3 R;
  if (s.empty() || t1 <= t0) return R;
  // premier échantillon strictement après t0 (le tampon est trié par le temps)
  size_t lo = 0, hi = s.size();
  while (lo < hi) {
    size_t mid = (lo + hi) / 2;
    if (s[mid].t <= t0) lo = mid + 1; else hi = mid;
  }
  double prev = t0;
  Vec3 w_prev = lo > 0 ? s[lo - 1].w : s.front().w;
  for (size_t i = lo; i < s.size(); i++) {
    const auto& g = s[i];
    double te = std::min(g.t, t1);
    if (te > prev) { R = R * ExpMap(w_prev * (te - prev)); prev = te; }
    w_prev = g.w;
    if (g.t >= t1) break;
  }
  if (prev < t1) R = R * ExpMap(w_prev * (t1 - prev));  // extrapolation jusqu'à t1
  return R;
}

// Dernière vitesse angulaire connue (repère casque).
inline Vec3 LastOmega(const std::deque<GyroSample>& s) { return s.empty() ? Vec3() : s.back().w; }

// Synchronisation d'horloge : ramène le temps de l'ESP32 sur l'horloge du PC.
// On suit le MINIMUM de (heure PC - heure ESP32) : l'échantillon le moins retardé
// par l'USB donne le meilleur décalage. Fenêtres de 2 s pour suivre la dérive.
class ClockSync {
 public:
  double ToHost(double dev_s, double host_s) {
    double o = host_s - dev_s;
    if (!have_) { cur_ = wmin_ = o; wstart_ = host_s; have_ = true; }
    wmin_ = std::min(wmin_, o);
    if (host_s - wstart_ > 2.0) { cur_ = wmin_; wmin_ = o; wstart_ = host_s; }
    else cur_ = std::min(cur_, o);
    return dev_s + cur_;
  }
 private:
  bool have_ = false;
  double cur_ = 0, wmin_ = 0, wstart_ = 0;
};

// Monde G aligné sur la gravité, à partir de la direction "haut" mesurée dans le
// repère V (= repère caméra de la 1re image). Retourne R_GV : v_G = R_GV * v_V.
inline Mat3 GravityAlign(const Vec3& up_V) {
  Vec3 z = up_V * (1.0 / Norm(up_V));
  Vec3 fwd = {0, 0, 1};                      // axe optique de la 1re image
  Vec3 x = fwd - z * Dot(fwd, z);
  double n = Norm(x);
  if (n < 1e-6) { x = {1, 0, 0}; n = 1; }    // regarde pile vers le haut/bas : cas dégénéré
  x = x * (1.0 / n);
  Vec3 y = Cross(z, x);
  Mat3 R;
  for (int c = 0; c < 3; c++) { R.m[0][c] = Comp(x, c); R.m[1][c] = Comp(y, c); R.m[2][c] = Comp(z, c); }
  return R;
}

// Angles (roll, pitch, yaw) d'une matrice casque -> monde. Mêmes conventions que ToEuler.
inline void MatToEuler(const Mat3& R, double& roll, double& pitch, double& yaw) {
  roll = std::atan2(R.m[2][1], R.m[2][2]);
  pitch = std::asin(std::max(-1.0, std::min(1.0, -R.m[2][0])));
  yaw = std::atan2(R.m[1][0], R.m[0][0]);
}
