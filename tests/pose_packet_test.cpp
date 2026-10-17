#include <cassert>
#include <cstdio>
#include <fstream>
#include "../src/pose_packet.h"

int main() {
  // Quaternion <-> matrice : aller-retour sur de nombreuses rotations
  for (int i = 0; i < 2000; i++) {
    Quat q = Normalized({std::sin(i * 0.37) + 1.5, std::cos(i * 0.91), std::sin(i * 1.7), std::cos(i * 2.3) - 0.2});
    Quat r = QuatFromMat3(ToMat3(q));
    double dot = std::abs(q.w * r.w + q.x * r.x + q.y * r.y + q.z * r.z);  // q et -q = meme rotation
    assert(dot > 1 - 1e-9);
  }
  Quat q = Normalized({0.9, 0.2, -0.3, 0.25});
  auto pk = PackPose(kFlagTracking | kFlagFloor, 7, {1.5, -2.25, 0.125}, q, {0.1, 0.2, 0.3}, {-0.4, 0.5, -0.6}, -1.62);
  static_assert(kPoseSize == 68, "taille du paquet");
  std::ofstream("/tmp/pose.bin", std::ios::binary).write(reinterpret_cast<const char*>(pk.data()), pk.size());
  std::printf("paquet ecrit (%zu octets)\n", pk.size());
  std::puts("TEST PAQUET OK");
  return 0;
}
