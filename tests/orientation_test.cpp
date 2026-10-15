#include <cassert>
#include <cmath>
#include <cstdio>
#include <vector>
#include "../src/orientation.h"

static const double G = 9.81;

// Les 24 rotations "cube" (permutations d'axes avec signes, det = +1).
static std::vector<Mat3> AllMounts() {
  std::vector<Mat3> out;
  int perm[6][3] = {{0,1,2},{0,2,1},{1,0,2},{1,2,0},{2,0,1},{2,1,0}};
  for (auto& p : perm)
    for (int s = 0; s < 8; s++) {
      Mat3 r;
      for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++) r.m[i][j] = 0;
      for (int i = 0; i < 3; i++) r.m[i][p[i]] = ((s >> i) & 1) ? -1.0 : 1.0;
      if (r.Det() > 0.5) out.push_back(r);
    }
  return out;
}

int main() {
  // 1) Calibration guidée : pour CHAQUE montage possible, retrouver la bonne matrice
  auto mounts = AllMounts();
  assert(mounts.size() == 24);
  int good = 0;
  for (auto& Rt : mounts) {
    Mat3 Rsb = Rt.T();  // capteur = R^T * casque
    double tilt = 30.0 * 3.14159265358979 / 180.0;
    // petites erreurs de pose (3 deg) pour verifier la tolerance
    double e = 3.0 * 3.14159265358979 / 180.0;
    Vec3 a_level = Rsb * Vec3{G * std::sin(e), 0, G * std::cos(e)};
    Vec3 a_tilt = Rsb * Vec3{-G * std::sin(tilt), G * std::sin(e), G * std::cos(tilt)};
    Vec3 gyr = Rsb * Vec3{0.01, 0.0, -0.01};
    Vec3 up; Mat3 R; std::string msg;
    bool ok = MountStep1(a_level, gyr, up, msg) && MountStep2(up, a_tilt, gyr, R, msg);
    bool same = ok;
    for (int i = 0; i < 3 && same; i++)
      for (int j = 0; j < 3; j++)
        if (std::abs(R.m[i][j] - Rt.m[i][j]) > 1e-9) same = false;
    if (same && std::abs(R.Det() - 1.0) < 1e-9) ++good;
    else std::printf("ECHEC montage: %s\n", msg.c_str());
  }
  std::printf("calibration de montage : %d/24 corrects\n", good);
  assert(good == 24);

  // Ton cas reel : accel capteur = (+0.113, +0.168, -9.586) a plat -> haut = -Z
  {
    Vec3 up; std::string msg;
    bool ok = MountStep1({0.113, 0.168, -9.586}, {0.05, 0.027, 0.014}, up, msg);
    std::printf("cas reel: %s | %s\n", ok ? "ok" : "refuse", msg.c_str());
    assert(ok && up.z < -0.5);
  }

  // 2) Refus des mauvaises poses
  {
    Vec3 up; std::string msg;
    assert(!MountStep1({5, 0, 8.4}, {0, 0, 0}, up, msg));     // trop incline
    assert(!MountStep1({0, 0, 9.8}, {1.0, 0, 0}, up, msg));   // en mouvement
    Mat3 R;
    assert(!MountStep2({0, 0, 1}, {0.2, 0, 9.8}, {0, 0, 0}, R, msg));  // pas assez penche
  }

  // 3) Filtre : orientation vraie qui tourne, gyro avec biais, accel = gravite
  {
    Quat truth;  // casque->monde
    Mahony f;
    double dt = 0.005, maxerr = 0;
    for (int i = 0; i < 6000; i++) {  // 30 s
      double t = i * dt;
      Vec3 w = {0.4 * std::sin(0.7 * t), 0.5 * std::cos(0.5 * t), 0.3};  // rad/s corps
      Quat wq{0, w.x, w.y, w.z};
      Quat dq = truth * wq;
      truth = Normalized({truth.w + 0.5 * dq.w * dt, truth.x + 0.5 * dq.x * dt,
                          truth.y + 0.5 * dq.y * dt, truth.z + 0.5 * dq.z * dt});
      Vec3 a_body = ToMat3(truth).T() * Vec3{0, 0, G};
      Vec3 gyro_meas = w + Vec3{0.01, -0.01, 0.005};  // biais
      f.Update(gyro_meas, a_body, i == 0 ? 0.0 : dt);
      if (t > 3.0) {
        Vec3 up_est = ToMat3(f.q()).T() * Vec3{0, 0, 1};
        Vec3 up_true = ToMat3(truth).T() * Vec3{0, 0, 1};
        double c = std::max(-1.0, std::min(1.0, Dot(up_est, up_true)));
        maxerr = std::max(maxerr, std::acos(c) * 180.0 / 3.14159265358979);
      }
    }
    std::printf("filtre : erreur max de verticale apres 3 s = %.2f deg\n", maxerr);
    assert(maxerr < 2.0);
  }

  // 4) Conventions : penche vers l'avant => pitch > 0 ; penche a droite => roll > 0
  {
    double r, p, y;
    Mahony f;
    f.Update({0, 0, 0}, {-G * 0.5, 0, G * 0.866}, 0);  // nez vers le bas 30 deg
    ToEuler(f.q(), r, p, y);
    std::printf("nez bas 30 deg -> pitch=%.1f deg\n", p * 180 / 3.14159265358979);
    assert(std::abs(p * 180 / 3.14159265358979 - 30) < 0.5);
    Mahony g;
    g.Update({0, 0, 0}, {0, G * 0.5, G * 0.866}, 0);  // penche a droite 30 deg
    ToEuler(g.q(), r, p, y);
    assert(std::abs(r * 180 / 3.14159265358979 - 30) < 0.5);
  }

  // 5) Projection : gauche apparait a gauche, haut en haut
  {
    View v = MakeView(-35, 22, 1.1);
    double cx = 320, cy = 260, f = 560, sx0, sy0, sx, sy;
    assert(Project(v, {0, 0, 0}, f, cx, cy, sx0, sy0));
    assert(Project(v, {0, 0.3, 0}, f, cx, cy, sx, sy) && sx < sx0);
    assert(Project(v, {0, 0, 0.3}, f, cx, cy, sx, sy) && sy < sy0);
    View v0 = MakeView(0, 0, 1.0);
    assert(Project(v0, {0, 0.3, 0}, f, cx, cy, sx, sy) && sx < cx);
    assert(Project(v0, {0.3, 0, 0}, f, cx, cy, sx, sy) && std::abs(sx - cx) < 1e-6);
  }
  std::puts("TOUS LES TESTS OK");
  return 0;
}
