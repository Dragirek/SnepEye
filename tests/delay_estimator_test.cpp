#include <cassert>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include "../src/delay_estimator.h"
#include "../src/frame_clock.h"

static double Rnd() { return rand() / (double)RAND_MAX - 0.5; }

// Vitesse angulaire vraie de la tête dans son propre repère (rad/s) : mouvement régulier sur les 3 axes.
static Vec3 Omega(double t) {
  return {0.9 * std::sin(2.1 * t) + 0.3 * std::sin(5.3 * t), 0.7 * std::sin(1.3 * t + 1.0), 1.1 * std::sin(1.7 * t + 0.4)};
}

static double RunCase(double dTrue, double gyroNoise, double visionNoiseDeg, double bias, double* qOut) {
  const double fps = 60, gHz = 200;
  const Mat3 Rcb = CamFromBody();
  // orientation vraie (casque -> monde) intégrée finement
  const double dt = 1.0 / 2000;
  std::deque<GyroSample> gyro;
  std::vector<double> tcap;                       // instants vrais de prise de vue
  std::vector<Mat3> Rcam;                         // orientation vraie de la caméra à ces instants
  Mat3 Rb;
  double nextG = 0, nextF = 0.05;
  for (double t = 0; t < 12.0; t += dt) {
    Rb = Rb * ExpMap(Omega(t) * dt);
    if (t >= nextG) {                             // gyro mesuré (bruit + biais), horodaté sans retard
      Vec3 w = Omega(t) + Vec3{Rnd() * gyroNoise + bias, Rnd() * gyroNoise, Rnd() * gyroNoise - bias};
      gyro.push_back({t, w});
      nextG += 1.0 / gHz;
    }
    if (t >= nextF) { tcap.push_back(t); Rcam.push_back(Rb * Rcb.T()); nextF += 1.0 / fps; }
  }
  DelayEstimator est;
  const double dvis = visionNoiseDeg * 3.14159265358979 / 180.0;
  for (size_t k = 1; k < tcap.size(); k++) {
    Mat3 dV = Rcam[k - 1].T() * Rcam[k];
    dV = dV * ExpMap({Rnd() * dvis, Rnd() * dvis, Rnd() * dvis});   // bruit de la vision
    est.AddStep(tcap[k - 1] + dTrue, tcap[k] + dTrue, dV);          // l'estimateur ne voit que les instants d'ARRIVEE
    if (k > 400) break;                                             // ~6,5 s de données
  }
  double d = -1, q = 0;
  bool ok = est.Estimate(gyro, Rcb, d, q);
  if (qOut) *qOut = q;
  return ok ? d : -1;
}

int main() {
  srand(9);
  double worst = 0;
  for (double dTrue : {0.012, 0.028, 0.037, 0.055, 0.080}) {
    double q;
    double d = RunCase(dTrue, 0.02, 0.15, 0.01, &q);
    std::printf("delai vrai %.1f ms -> estime %.1f ms (qualite %.2f)\n", dTrue * 1000, d * 1000, q);
    assert(d >= 0);
    worst = std::max(worst, std::abs(d - dTrue));
  }
  std::printf("erreur max : %.1f ms\n", worst * 1000);
  assert(worst < 0.004);
  // trop peu de mouvement : pas d'estimation (on garde l'ancien délai)
  {
    DelayEstimator est;
    std::deque<GyroSample> gyro;
    for (int i = 0; i < 600; i++) gyro.push_back({i * 0.005, Vec3{0.001, 0, 0}});
    for (int k = 1; k < 100; k++) est.AddStep(k / 60.0, (k + 1) / 60.0, ExpMap({1e-5, 0, 0}));
    double d, q;
    assert(!est.Estimate(gyro, CamFromBody(), d, q));
  }
  std::puts("TEST ESTIMATEUR DE DELAI OK");
  return 0;
}
