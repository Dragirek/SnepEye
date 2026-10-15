#include <cassert>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include "../src/fusion.h"

static double Err(const Mat3& a, const Mat3& b) {
  double e = 0;
  for (int i = 0; i < 3; i++)
    for (int j = 0; j < 3; j++) e = std::max(e, std::abs(a.m[i][j] - b.m[i][j]));
  return e;
}

int main() {
  // 1) Repère caméra : l'avant du casque est l'axe optique, gauche = -x_cam, haut = -y_cam
  Mat3 Rcb = CamFromBody();
  assert(std::abs(Rcb.Det() - 1) < 1e-12);
  Vec3 f = Rcb * Vec3{1, 0, 0}, l = Rcb * Vec3{0, 1, 0}, u = Rcb * Vec3{0, 0, 1};
  assert(f.z == 1 && l.x == -1 && u.y == -1);

  // 2) Intégration du gyro : rotation constante de 90 deg/s autour de Z casque pendant 0.5 s
  std::deque<GyroSample> buf;
  for (int i = 0; i <= 400; i++) buf.push_back({i * 0.005, {0, 0, 1.5707963}});  // 200 Hz, 2 s
  Mat3 R = IntegrateGyro(buf, 0.5, 1.0);
  assert(Err(R, ExpMap({0, 0, 1.5707963 * 0.5})) < 1e-6);
  // composition sur deux sous-intervalles = intervalle complet
  Mat3 R2 = IntegrateGyro(buf, 0.5, 0.8) * IntegrateGyro(buf, 0.8, 1.0);
  assert(Err(R, R2) < 1e-6);
  // extrapolation au-dela du dernier echantillon (t1 = 2.05, dernier echantillon a 2.0)
  Mat3 Re = IntegrateGyro(buf, 1.9, 2.05);
  assert(Err(Re, ExpMap({0, 0, 1.5707963 * 0.15})) < 1e-6);

  // 3) Changement de repere du prior : dR_cam = Rcb * dR_body * Rcb^T
  //    tourner la tete vers la GAUCHE (yaw > 0 casque) = la camera tourne autour de -y_cam
  Mat3 dRb = ExpMap({0, 0, 0.1});
  Mat3 dRc = Rcb * dRb * Rcb.T();
  Mat3 expect = ExpMap({0, -0.1, 0});
  assert(Err(dRc, expect) < 1e-9);

  // 4) Horloge : retard USB variable (0..8 ms) + decalage 12345.678 s
  ClockSync cs;
  srand(3);
  double worst = 0;
  for (int i = 0; i < 4000; i++) {            // 20 s a 200 Hz
    double dev = i * 0.005;
    double host = dev + 12345.678 + (rand() % 8000) * 1e-6;
    double mapped = cs.ToHost(dev, host);
    if (i > 400) worst = std::max(worst, std::abs(mapped - (dev + 12345.678)));
  }
  std::printf("horloge : erreur max apres 2 s = %.2f ms\n", worst * 1000);
  assert(worst < 0.008);

  // 5) Alignement gravite : le "haut" mesure dans V est envoye sur Z
  Vec3 up_V = {0.05, -0.97, 0.2};  // camera legerement penchee, y_cam = bas => haut ~ -y
  Mat3 G = GravityAlign(up_V);
  Vec3 z = G * (up_V * (1.0 / Norm(up_V)));
  assert(std::abs(z.x) < 1e-9 && std::abs(z.y) < 1e-9 && std::abs(z.z - 1) < 1e-9);
  assert(std::abs(G.Det() - 1) < 1e-9);
  // camera a l'horizontale : l'axe optique devient l'axe X du monde
  Mat3 G0 = GravityAlign({0, -1, 0});
  Vec3 fwd = G0 * Vec3{0, 0, 1};
  assert(std::abs(fwd.x - 1) < 1e-9);

  // 6) MatToEuler == ToEuler(quaternion)
  {
    Quat q = Normalized({0.9, 0.2, -0.3, 0.25});
    double r1, p1, y1, r2, p2, y2;
    ToEuler(q, r1, p1, y1);
    MatToEuler(ToMat3(q), r2, p2, y2);
    assert(std::abs(r1 - r2) < 1e-9 && std::abs(p1 - p2) < 1e-9 && std::abs(y1 - y2) < 1e-9);
  }

  std::puts("TOUS LES TESTS DE FUSION OK");
  return 0;
}
