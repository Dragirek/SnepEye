// g++ -std=c++17 -Isrc tests/mount_auto_test.cpp
#include <cassert>
#include <cstdio>
#include "mount_auto.h"
int main() {
  setvbuf(stdout, nullptr, _IONBF, 0);
  const double G = 9.81, tilt = 30.0 * 3.14159265 / 180.0;
  // Capteur monte a l'envers (haut = -Z capteur) : la matrice attendue vient de MountStep1/2 avec les memes entrees.
  Vec3 aFlat{0.1, 0.1, -G}, aTilt{-G * std::sin(tilt) * 0.0 + 0.0, G * std::sin(tilt), -G * std::cos(tilt)};
  Vec3 still{0.01, 0.0, -0.01}, moving{0.9, 0.2, 0.0};
  MountAuto m; std::string msg; Mat3 R;
  m.Start(0, msg);
  assert(m.Active());
  bool done = false; int beeps = 0;
  double t = 0.0;
  for (; t < 40.0 && !done; t += 0.02) {
    // 0..6 s decompte ; casque agite jusqu'a 8 s ; immobile a plat jusqu'a 14 s ; pose inclinee ensuite (agitee jusqu'a 17 s)
    Vec3 a, g;
    if (t < 8.0) { a = aFlat + Vec3{std::sin(t * 30) * 2, 0, 0}; g = moving; }
    else if (t < 14.0) { a = aFlat; g = still; }
    else if (t < 17.0) { a = aTilt + Vec3{std::sin(t * 40) * 3, 0, 0}; g = moving; }
    else { a = aTilt; g = still; }
    std::string before = msg;
    done = m.Update(t, a, g, R, msg);
    if (msg != before) std::printf("%.2f: %s\n", t, msg.c_str());
    if (m.TakeBeep()) beeps++;
  }
  std::printf("termine a %.1f s : %s\n", t, msg.c_str());
  std::printf("done=%d beeps=%d\n", (int)done, beeps); assert(done && !m.Active() && beeps == 4);
  assert(std::abs(R.Det() - 1.0) < 1e-9);
  // Toujours agite : echec propre apres 3 essais
  MountAuto f; f.Start(0, msg); bool d2 = false;
  for (t = 0; t < 200 && f.Active(); t += 0.02) d2 = f.Update(t, aFlat + Vec3{std::sin(t * 30) * 2, 0, 0}, moving, R, msg);
  assert(!d2 && !f.Active());
  std::printf("echec : %s\nmount_auto_test : OK\n", msg.c_str());
  return 0;
}
