#include <cassert>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include "../src/floor.h"

static double Rnd() { return rand() / (double)RAND_MAX; }

int main() {
  srand(5);
  // 1) sol a -1.65 m (60 pts, bruit 3 cm) + fouillis + quelques points aberrants sous le sol
  {
    std::vector<double> z;
    for (int i = 0; i < 60; i++) z.push_back(-1.65 + (Rnd() - 0.5) * 0.06);
    for (int i = 0; i < 100; i++) z.push_back(-1.4 + Rnd() * 2.9);
    for (int i = 0; i < 10; i++) z.push_back(-4.0 + Rnd() * 1.5);
    FloorEstimate e = EstimateFloor(z, 0.0);
    std::printf("synthetique : sol=%.3f (vrai -1.650) n=%d\n", e.z, e.count);
    assert(e.ok && std::abs(e.z + 1.65) < 0.04);
  }
  // 2) aucun sol visible : mur seul -> pas d'estimation
  {
    std::vector<double> z;
    for (int i = 0; i < 200; i++) z.push_back(-1.2 + Rnd() * 2.4);
    assert(!EstimateFloor(z, 0.0).ok);
  }
  // 3) seulement une table a 0.7 m sous la tete -> rejetee (trop haut pour etre le sol)
  {
    std::vector<double> z;
    for (int i = 0; i < 80; i++) z.push_back(-0.7 + (Rnd() - 0.5) * 0.04);
    for (int i = 0; i < 50; i++) z.push_back(Rnd() * 1.5);
    assert(!EstimateFloor(z, 0.0).ok);
  }
  // 4) points reels issus du tracker simule (sol vrai a -1.50 m)
  {
    std::ifstream f("tests/data/floor_sim_points.csv");
    if (!f) f.open("data/floor_sim_points.csv");
    std::vector<double> z;
    double v;
    while (f >> v) z.push_back(v);
    if (z.empty()) {
      std::puts("(points simules absents : test 4 ignore)");
    } else {
      FloorEstimate e = EstimateFloor(z, 0.0);
      std::printf("points du tracker simule (%zu pts) : sol=%.3f (vrai -1.500) n=%d ok=%d\n", z.size(), e.z, e.count, (int)e.ok);
      assert(e.ok && std::abs(e.z + 1.50) < 0.06);
    }
  }
  // 5) filtre temporel : mediane stable malgre des estimations aberrantes
  {
    FloorFilter ff;
    for (int i = 0; i < 20; i++) {
      FloorEstimate e; e.ok = true; e.z = (i % 7 == 0) ? -1.2 : -1.62 + (Rnd() - 0.5) * 0.04; ff.Add(e);
    }
    assert(ff.Valid() && std::abs(ff.Z() + 1.62) < 0.03);
  }
  std::puts("TOUS LES TESTS DU SOL OK");
  return 0;
}
