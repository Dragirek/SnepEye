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
  // 6) carte accumulee : le sol n'est vu que pendant 4 estimations (tete baissee), les autres images ne montrent que des murs
  {
    FloorMap fm;
    FloorEstimate e;
    int firstOk = -1;
    for (int it = 0; it < 80; it++) {
      std::vector<double> z;
      if (it >= 20 && it < 24)   // regard vers le bas : 14 points de sol a -1.60 m (peu), et des meubles
      {
        for (int i = 0; i < 14; i++) z.push_back(-1.60 + (Rnd() - 0.5) * 0.05);
        for (int i = 0; i < 12; i++) z.push_back(-1.0 + Rnd() * 0.5);
      }
      else
        for (int i = 0; i < 40; i++) z.push_back(-0.35 + Rnd() * 1.8);   // des murs, des etageres : jamais assez bas
      e = fm.Update(z, 0.0);
      if (e.ok && firstOk < 0) firstOk = it;
    }
    std::printf("carte accumulee : sol=%.3f (vrai -1.600) trouve a l'estimation %d ok=%d\n", e.z, firstOk, (int)e.ok);
    assert(e.ok && std::abs(e.z + 1.60) < 0.05 && firstOk >= 20 && firstOk < 30);
    // la meme scene avec l'ancienne methode (une seule image) ne trouve rien
    std::vector<double> z;
    for (int i = 0; i < 14; i++) z.push_back(-1.60 + (Rnd() - 0.5) * 0.05);
    for (int i = 0; i < 12; i++) z.push_back(-1.0 + Rnd() * 0.5);
    assert(!EstimateFloor(z, 0.0).ok);
  }
  // 7) la table a 0.7 m sous la tete ne devient jamais le sol, meme accumulee
  {
    FloorMap fm;
    FloorEstimate e;
    for (int it = 0; it < 60; it++) {
      std::vector<double> z;
      for (int i = 0; i < 50; i++) z.push_back(-0.7 + (Rnd() - 0.5) * 0.04);
      e = fm.Update(z, 0.0);
    }
    assert(!e.ok);
  }
  // 8) une fois le sol trouve, il est garde quand la tete descend (accroupi : 0.5 m au-dessus du sol)
  {
    FloorMap fm;
    FloorEstimate e;
    for (int it = 0; it < 30; it++) {
      std::vector<double> z;
      for (int i = 0; i < 40; i++) z.push_back(-1.62 + (Rnd() - 0.5) * 0.05);
      e = fm.Update(z, 0.0);
    }
    assert(e.ok);
    for (int it = 0; it < 10; it++) {
      std::vector<double> z;
      for (int i = 0; i < 40; i++) z.push_back(-1.62 + (Rnd() - 0.5) * 0.05); // les points ne bougent pas dans G ; c'est la tete qui descend de 1.1 m
      e = fm.Update(z, -1.1);
    }
    assert(e.ok && std::abs(e.z + 1.62) < 0.06);
  }
  std::puts("TOUS LES TESTS DU SOL OK");
  return 0;
}
