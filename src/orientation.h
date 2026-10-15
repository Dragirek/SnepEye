#pragma once
// Orientation de la sonde IMU : filtre gyro+accéléromètre, correction de
// montage (capteur posé sur le côté / à l'envers) et projection 3D pour la
// visualisation. Aucune dépendance (testable partout, voir tests/).
//
// Repère "casque" (comme ROS) : X = avant, Y = gauche, Z = haut. Main droite.
//   roll  > 0 : penché vers la droite
//   pitch > 0 : regarde vers le bas
//   yaw   > 0 : tourne vers la gauche
#include <algorithm>
#include <cmath>
#include <string>

struct Vec3 { double x = 0, y = 0, z = 0; };
inline Vec3 operator+(Vec3 a, Vec3 b) { return {a.x + b.x, a.y + b.y, a.z + b.z}; }
inline Vec3 operator-(Vec3 a, Vec3 b) { return {a.x - b.x, a.y - b.y, a.z - b.z}; }
inline Vec3 operator*(Vec3 a, double s) { return {a.x * s, a.y * s, a.z * s}; }
inline double Dot(Vec3 a, Vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
inline Vec3 Cross(Vec3 a, Vec3 b) {
  return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}
inline double Norm(Vec3 a) { return std::sqrt(Dot(a, a)); }
inline double Comp(const Vec3& v, int i) { return i == 0 ? v.x : i == 1 ? v.y : v.z; }

struct Mat3 {
  double m[3][3] = {{1, 0, 0}, {0, 1, 0}, {0, 0, 1}};
  Vec3 operator*(const Vec3& v) const {
    return {m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z,
            m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z,
            m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z};
  }
  Mat3 operator*(const Mat3& o) const {
    Mat3 r;
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++) {
        r.m[i][j] = 0;
        for (int k = 0; k < 3; k++) r.m[i][j] += m[i][k] * o.m[k][j];
      }
    return r;
  }
  Mat3 T() const {
    Mat3 r;
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++) r.m[i][j] = m[j][i];
    return r;
  }
  double Det() const {
    return m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) -
           m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0]) +
           m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
  }
  Vec3 Row(int i) const { return {m[i][0], m[i][1], m[i][2]}; }
};

// Rotation de +/-90 degres autour d'un axe du casque (0=X, 1=Y, 2=Z).
inline Mat3 Rot90(int axis, int sign) {
  Mat3 r;
  double c = 0, s = sign >= 0 ? 1.0 : -1.0;
  int a = (axis + 1) % 3, b = (axis + 2) % 3;
  r.m[0][0] = r.m[1][1] = r.m[2][2] = 0;
  r.m[axis][axis] = 1;
  r.m[a][a] = c; r.m[a][b] = -s;
  r.m[b][a] = s; r.m[b][b] = c;
  return r;
}

struct Quat { double w = 1, x = 0, y = 0, z = 0; };
inline Quat operator*(const Quat& a, const Quat& b) {
  return {a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
          a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
          a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
          a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w};
}
inline Quat Normalized(Quat q) {
  double n = std::sqrt(q.w * q.w + q.x * q.x + q.y * q.y + q.z * q.z);
  if (n < 1e-12) return Quat();
  return {q.w / n, q.x / n, q.y / n, q.z / n};
}
// Rotation casque -> monde.
inline Mat3 ToMat3(const Quat& q) {
  Mat3 r;
  r.m[0][0] = 1 - 2 * (q.y * q.y + q.z * q.z);
  r.m[0][1] = 2 * (q.x * q.y - q.w * q.z);
  r.m[0][2] = 2 * (q.x * q.z + q.w * q.y);
  r.m[1][0] = 2 * (q.x * q.y + q.w * q.z);
  r.m[1][1] = 1 - 2 * (q.x * q.x + q.z * q.z);
  r.m[1][2] = 2 * (q.y * q.z - q.w * q.x);
  r.m[2][0] = 2 * (q.x * q.z - q.w * q.y);
  r.m[2][1] = 2 * (q.y * q.z + q.w * q.x);
  r.m[2][2] = 1 - 2 * (q.x * q.x + q.y * q.y);
  return r;
}
inline void ToEuler(const Quat& q, double& roll, double& pitch, double& yaw) {
  roll = std::atan2(2 * (q.w * q.x + q.y * q.z), 1 - 2 * (q.x * q.x + q.y * q.y));
  double s = std::max(-1.0, std::min(1.0, 2 * (q.w * q.y - q.z * q.x)));
  pitch = std::asin(s);
  yaw = std::atan2(2 * (q.w * q.z + q.x * q.y), 1 - 2 * (q.y * q.y + q.z * q.z));
}
inline Quat FromRollPitch(double roll, double pitch) {
  Quat qy{std::cos(pitch / 2), 0, std::sin(pitch / 2), 0};
  Quat qx{std::cos(roll / 2), std::sin(roll / 2), 0, 0};
  return Normalized(qy * qx);
}

// Filtre complémentaire de type Mahony : intègre le gyro, corrige le roll/pitch
// avec la gravité. Le yaw n'a pas de référence (pas de boussole) : il dérive.
class Mahony {
 public:
  double kp = 1.0;  // gain de correction par la gravité
  void Reset() { init_ = false; q_ = Quat(); }
  bool Initialized() const { return init_; }
  Quat q() const { return q_; }

  // gyro en rad/s, accel en m/s^2, tous deux dans le repère casque.
  void Update(Vec3 gyro, Vec3 accel, double dt) {
    double n = Norm(accel);
    if (!init_) {
      if (n < 1.0) return;
      double roll = std::atan2(accel.y, accel.z);
      double pitch = std::atan2(-accel.x, std::sqrt(accel.y * accel.y + accel.z * accel.z));
      q_ = FromRollPitch(roll, pitch);
      init_ = true;
    }
    // On ne fait confiance a la gravite que si l'accel ressemble a 1 g
    // (sinon le casque est en train d'accélérer).
    if (n > 1e-3 && std::abs(n - 9.81) < 3.0) {
      Vec3 a = accel * (1.0 / n);
      Vec3 up = {2 * (q_.x * q_.z - q_.w * q_.y), 2 * (q_.y * q_.z + q_.w * q_.x),
                 q_.w * q_.w - q_.x * q_.x - q_.y * q_.y + q_.z * q_.z};
      gyro = gyro + Cross(a, up) * kp;
    }
    Quat w{0, gyro.x, gyro.y, gyro.z};
    Quat dq = q_ * w;
    q_ = Normalized({q_.w + 0.5 * dq.w * dt, q_.x + 0.5 * dq.x * dt,
                     q_.y + 0.5 * dq.y * dt, q_.z + 0.5 * dq.z * dt});
  }
  void ZeroYaw() {
    double r, p, y;
    ToEuler(q_, r, p, y);
    Quat qz{std::cos(-y / 2), 0, 0, std::sin(-y / 2)};
    q_ = Normalized(qz * q_);
  }

 private:
  bool init_ = false;
  Quat q_;
};

// ---------------------------------------------------------------------------
// Calibration du montage en 2 gestes (le capteur peut être sur le côté, à
// l'envers...). Le résultat est une matrice R telle que  v_casque = R * v_capteur
// dont les lignes sont des axes du capteur (montage à 90 degres près).
//   Étape 1 : casque à plat, à l'endroit, immobile  -> trouve l'axe "haut".
//   Étape 2 : casque penché vers l'avant (~30 deg), immobile -> trouve l'axe "avant".
// ---------------------------------------------------------------------------
inline int MaxAxis(const Vec3& v) {
  double ax = std::abs(v.x), ay = std::abs(v.y), az = std::abs(v.z);
  return ax >= ay && ax >= az ? 0 : (ay >= az ? 1 : 2);
}
inline Vec3 AxisVec(int i, double sign) {
  Vec3 v;
  double s = sign >= 0 ? 1.0 : -1.0;
  if (i == 0) v.x = s; else if (i == 1) v.y = s; else v.z = s;
  return v;
}
inline std::string AxisName(const Vec3& v) {
  int i = MaxAxis(v);
  const char* n[3] = {"X", "Y", "Z"};
  return std::string(Comp(v, i) >= 0 ? "+" : "-") + n[i] + " capteur";
}

inline bool MountStep1(const Vec3& acc, const Vec3& gyr, Vec3& up_s, std::string& msg) {
  double n = Norm(acc);
  if (n < 8.0 || n > 11.5) { msg = "etape 1 : accel anormale (|a|=" + std::to_string(n).substr(0, 5) + "), immobilise le casque"; return false; }
  if (Norm(gyr) > 0.15) { msg = "etape 1 : le casque bouge, immobilise-le"; return false; }
  int i = MaxAxis(acc);
  if (std::abs(Comp(acc, i)) / n < 0.9) { msg = "etape 1 : pose le casque bien a plat (< 25 deg d'inclinaison)"; return false; }
  up_s = AxisVec(i, Comp(acc, i));
  msg = "etape 1 OK : haut = " + AxisName(up_s) + ". Penche maintenant le casque vers l'avant (~30 deg), immobile, puis touche f";
  return true;
}

inline bool MountStep2(const Vec3& up_s, const Vec3& acc, const Vec3& gyr, Mat3& R, std::string& msg) {
  if (Norm(gyr) > 0.15) { msg = "etape 2 : le casque bouge, immobilise-le"; return false; }
  Vec3 h = acc - up_s * Dot(acc, up_s);  // composante horizontale de l'accel
  double hn = Norm(h);
  if (hn < 2.5) { msg = "etape 2 : penche plus vers l'avant (au moins 15 deg)"; return false; }
  int j = MaxAxis(h);
  if (std::abs(Comp(h, j)) / hn < 0.8) { msg = "etape 2 : penche bien droit vers l'avant, pas sur le cote"; return false; }
  // Penche vers l'avant => l'accel (qui mesure "le haut") pointe vers l'ARRIERE.
  Vec3 forward = AxisVec(j, Comp(h, j)) * -1.0;
  Vec3 left = Cross(up_s, forward);
  for (int c = 0; c < 3; c++) {
    R.m[0][c] = Comp(forward, c);
    R.m[1][c] = Comp(left, c);
    R.m[2][c] = Comp(up_s, c);
  }
  msg = "montage OK : avant = " + AxisName(forward) + ", gauche = " + AxisName(left) + ", haut = " + AxisName(up_s);
  return true;
}

// ---------------------------------------------------------------------------
// Vue 3D "classique" : repère monde X avant, Y gauche, Z haut. La caméra de vue
// est derrière et au-dessus du modèle.
// ---------------------------------------------------------------------------
struct View { Vec3 eye, r, u, f; };

inline View MakeView(double az_deg, double el_deg, double dist) {
  const double d2r = 3.14159265358979323846 / 180.0;
  double az = az_deg * d2r, el = el_deg * d2r;
  View v;
  v.eye = {-dist * std::cos(el) * std::cos(az), -dist * std::cos(el) * std::sin(az), dist * std::sin(el)};
  Vec3 f = v.eye * -1.0;
  v.f = f * (1.0 / Norm(f));
  Vec3 r = Cross(v.f, {0, 0, 1});
  v.r = r * (1.0 / Norm(r));
  v.u = Cross(v.r, v.f);
  return v;
}

// Retourne false si le point est derrière la caméra.
inline bool Project(const View& v, const Vec3& p, double focal, double cx, double cy,
                    double& sx, double& sy) {
  Vec3 d = p - v.eye;
  double zc = Dot(d, v.f);
  if (zc < 0.05) return false;
  sx = cx + focal * Dot(d, v.r) / zc;
  sy = cy - focal * Dot(d, v.u) / zc;
  return true;
}
