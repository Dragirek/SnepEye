#pragma once
// Paquet de pose envoyé en UDP à Unity (68 octets, little-endian). Repère G :
// X avant, Y gauche, Z haut (main droite), mètres et radians. Unity convertit
// (voir unity/Assets/AR6DoF/Ar6dofPose.cs).
//
//   offset  type      contenu
//        0  uint32    magic 0x44365241  (octets 'A','R','6','D')
//        4  uint16    version = 1
//        6  uint16    flags : bit0 tracking OK | bit1 sol valide | bit2 tracking perdu | bit3 sol verrouillé
//        8  uint32    numéro de séquence
//       12  float32x3 position tête (x, y, z)
//       24  float32x4 orientation casque->monde (w, x, y, z)
//       40  float32x3 vitesse linéaire (m/s, repère G)
//       52  float32x3 vitesse angulaire (rad/s, repère G)
//       64  float32   hauteur du sol Z (repère G), valide si bit1
#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>

#include "orientation.h"

constexpr uint32_t kPoseMagic = 0x44365241u;
constexpr size_t kPoseSize = 68;
enum PoseFlags : uint16_t { kFlagTracking = 1, kFlagFloor = 2, kFlagLost = 4, kFlagFloorLocked = 8 };

// Matrice de rotation -> quaternion (w, x, y, z).
inline Quat QuatFromMat3(const Mat3& R) {
  Quat q;
  double t = R.m[0][0] + R.m[1][1] + R.m[2][2];
  if (t > 0) {
    double s = std::sqrt(t + 1.0) * 2;
    q.w = 0.25 * s;
    q.x = (R.m[2][1] - R.m[1][2]) / s;
    q.y = (R.m[0][2] - R.m[2][0]) / s;
    q.z = (R.m[1][0] - R.m[0][1]) / s;
  } else if (R.m[0][0] > R.m[1][1] && R.m[0][0] > R.m[2][2]) {
    double s = std::sqrt(1.0 + R.m[0][0] - R.m[1][1] - R.m[2][2]) * 2;
    q.w = (R.m[2][1] - R.m[1][2]) / s;
    q.x = 0.25 * s;
    q.y = (R.m[0][1] + R.m[1][0]) / s;
    q.z = (R.m[0][2] + R.m[2][0]) / s;
  } else if (R.m[1][1] > R.m[2][2]) {
    double s = std::sqrt(1.0 + R.m[1][1] - R.m[0][0] - R.m[2][2]) * 2;
    q.w = (R.m[0][2] - R.m[2][0]) / s;
    q.x = (R.m[0][1] + R.m[1][0]) / s;
    q.y = 0.25 * s;
    q.z = (R.m[1][2] + R.m[2][1]) / s;
  } else {
    double s = std::sqrt(1.0 + R.m[2][2] - R.m[0][0] - R.m[1][1]) * 2;
    q.w = (R.m[1][0] - R.m[0][1]) / s;
    q.x = (R.m[0][2] + R.m[2][0]) / s;
    q.y = (R.m[1][2] + R.m[2][1]) / s;
    q.z = 0.25 * s;
  }
  return Normalized(q);
}

inline std::array<uint8_t, kPoseSize> PackPose(uint16_t flags, uint32_t seq, const Vec3& p, const Quat& q,
                                               const Vec3& v, const Vec3& w, double floorZ) {
  std::array<uint8_t, kPoseSize> b{};
  uint8_t* d = b.data();
  uint16_t ver = 1;
  std::memcpy(d + 0, &kPoseMagic, 4);
  std::memcpy(d + 4, &ver, 2);
  std::memcpy(d + 6, &flags, 2);
  std::memcpy(d + 8, &seq, 4);
  float f[14] = {(float)p.x, (float)p.y, (float)p.z, (float)q.w, (float)q.x, (float)q.y, (float)q.z,
                 (float)v.x, (float)v.y, (float)v.z, (float)w.x, (float)w.y, (float)w.z, (float)floorZ};
  std::memcpy(d + 12, f, sizeof(f));
  return b;
}
