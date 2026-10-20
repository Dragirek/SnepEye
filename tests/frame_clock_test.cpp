#include <cassert>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include "../src/frame_clock.h"

static double Rnd() { return rand() / (double)RAND_MAX; }

int main() {
  srand(4);
  FrameClock clk;
  const double T = 1.0 / 60.0, t_true0 = 100.0, decode_min = 0.012;
  double worstRaw = 0, worstSm = 0;
  for (int n = 0; n < 1200; n++) {                       // 20 s
    double tcap = t_true0 + n * T;                       // vraie prise de vue (cadence fixe)
    double decode = decode_min + Rnd() * 0.008 + (n % 37 == 0 ? 0.012 : 0.0);  // 12..20 ms, + pics de 12 ms
    double arrive = tcap + decode;
    double sm = clk.Update(n, arrive);
    if (n > 60) {
      double truth = tcap + decode_min;                  // arrivée la moins retardée = vérité + décodage minimal
      worstRaw = std::max(worstRaw, std::abs(arrive - truth));
      worstSm = std::max(worstSm, std::abs(sm - truth));
    }
  }
  std::printf("gigue max : brute %.1f ms -> lissee %.2f ms\n", worstRaw * 1000, worstSm * 1000);
  assert(worstSm < 0.0025 && worstSm < worstRaw * 0.3);
  std::puts("TEST HORLOGE D'IMAGES OK");
  return 0;
}
