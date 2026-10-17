#pragma once
// Pose publiée vers Unity : dernière pose de la VISION (une fois par image) + gyro de l'IMU (200 Hz).
// Sans dépendance Windows : testable partout (tests/pose_snap_test.cpp).
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <deque>
#include <mutex>

#include "fusion.h"
#include "orientation.h"

// Dernière pose calculée par la VISION (une fois par image de caméra). Le thread IMU la complète à 200 Hz
// avec le gyro pour publier la pose vers Unity : la rotation n'est plus limitée à la cadence de la caméra.
struct SnapData {
  bool valid = false;
  double tcap = 0;            // instant de l'image (horloge NowS)
  double tUpdate = 0;         // instant où cette mesure est arrivée : la correction de continuité s'efface à partir de là
  Mat3 Rvc, R_GV;
  Vec3 t, v, corr;            // position et vitesse (repère V), correction de continuité qui s'efface
  uint16_t flags = 0;
  double floorZ = 0, predS = 0.015;
};
struct VisionSnap { std::mutex m; SnapData d; };
struct PoseOut { Vec3 p, vG, wG; Mat3 R; };

// Pose à l'instant `target` : rotation = vision + gyro intégré depuis l'image, position = vision + vitesse.
static PoseOut ComputePose(const SnapData& s, const std::deque<GyroSample>& gyro, double target, const Mat3& Rcb) {
  double span = std::min(0.2, std::max(0.0, target - s.tcap));
  Mat3 dRb = IntegrateGyro(gyro, s.tcap, s.tcap + span);
  Vec3 omegaB = LastOmega(gyro);
  Mat3 R_Vb = s.Rvc * Rcb * dRb;
  Vec3 pV = s.t + s.v * std::min(span, 0.1) + s.corr * std::exp(-std::max(0.0, target - s.tUpdate) / 0.08);
  PoseOut o;
  o.R = s.R_GV * R_Vb;
  o.p = s.R_GV * pV;
  o.vG = s.R_GV * s.v;
  o.wG = o.R * omegaB;
  return o;
}

// Nouvelle pose de la vision, arrivée à l'instant tNow. La correction de continuité évite un saut de position
// à chaque image : à cet instant, la pose publiée reste ce qu'elle était, puis rejoint la nouvelle mesure en ~80 ms.
static void UpdateSnap(VisionSnap& s, const Mat3& Rvc, const Vec3& t, const Vec3& v, double tcap, double tNow,
                       const Mat3& R_GV, uint16_t flags, bool floorValid, double floorZ, double predS) {
  std::lock_guard<std::mutex> l(s.m);
  SnapData& d = s.d;
  d.flags = flags;
  d.floorZ = floorValid ? floorZ : 0.0;
  d.predS = predS;
  if (d.valid && tcap == d.tcap) return;  // pas de nouvelle image : seuls les indicateurs changent
  Vec3 corr;
  if (d.valid && tNow - d.tUpdate < 0.3) {
    double tEval = tNow + predS;  // instant que Unity affichera
    Vec3 oldP = d.t + d.v * std::min(std::max(0.0, tEval - d.tcap), 0.1) +
                d.corr * std::exp(-std::max(0.0, tEval - d.tUpdate) / 0.08);
    Vec3 newRaw = t + v * std::min(std::max(0.0, tEval - tcap), 0.1);
    corr = oldP - newRaw;
    if (Norm(corr) > 0.2) corr = Vec3();  // vrai saut (perte puis reprise) : pas de lissage
  }
  d.Rvc = Rvc; d.t = t; d.v = v; d.corr = corr; d.tcap = tcap; d.tUpdate = tNow; d.R_GV = R_GV; d.valid = true;
}
static void InvalidateSnap(VisionSnap& s) {
  std::lock_guard<std::mutex> l(s.m);
  s.d.valid = false;
}

