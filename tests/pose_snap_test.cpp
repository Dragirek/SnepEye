#include <cassert>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include "../src/pose_snap.h"

static double Rnd() { return rand() / (double)RAND_MAX - 0.5; }

int main() {
  srand(11);
  const Mat3 Rcb = CamFromBody();
  // Scénario : on avance à 1 m/s en tournant à 90 deg/s. La vision donne une pose à 30 Hz AVEC du bruit (±2 mm),
  // le gyro arrive à 200 Hz. On échantillonne la pose publiée à 1 kHz et on mesure le plus gros saut entre 2 échantillons.
  const double fps = 30, gyroHz = 200, w_true = 90.0 * 3.14159265358979 / 180.0, vx = 1.0;
  std::deque<GyroSample> gyro;
  VisionSnap withCorr, noCorr;   // noCorr : même chose sans la correction de continuité (ancien comportement)
  Mat3 R_GV;                     // identité
  double maxJumpPos = 0, maxJumpPosNo = 0, maxJumpRot = 0, maxErrPos = 0, maxErrRot = 0;
  Vec3 lastP, lastPno;
  Mat3 lastR;
  bool have = false;
  double nextVision = 0, t = 0;
  for (int i = 0; i < 4000; i++) {          // 4 s à 1 kHz
    t = i * 0.001;
    if (std::fmod(t * gyroHz, 1.0) < 0.2 || i % 5 == 0) gyro.push_back({t, Vec3{0, 0, w_true}});  // gyro à 200 Hz, repère casque
    while (gyro.size() > 1 && gyro.back().t - gyro.front().t > 3.0) gyro.pop_front();
    if (t >= nextVision) {                  // nouvelle image : la vision donne la vraie pose + du bruit, avec 40 ms de retard
      double tc = t - 0.04;
      double yaw = w_true * tc;
      Mat3 Rwb = ExpMap({0, 0, yaw});       // casque->monde vrai (rotation autour de Z)
      Mat3 Rvc = Rwb * Rcb.T();
      Vec3 p = {vx * tc + Rnd() * 0.004, Rnd() * 0.004, Rnd() * 0.004};
      Vec3 v = {vx + Rnd() * 0.1, Rnd() * 0.05, 0};
      UpdateSnap(withCorr, Rvc, p, v, tc, t, R_GV, 1, false, 0, 0.015, 0.03);
      {  // ancien comportement : on remplace sans correction
        std::lock_guard<std::mutex> l(noCorr.m);
        noCorr.d.Rvc = Rvc; noCorr.d.R_GV = R_GV; noCorr.d.t = p; noCorr.d.v = v; noCorr.d.corr = Vec3();
        noCorr.d.tcap = tc; noCorr.d.tUpdate = t; noCorr.d.valid = true;
      }
      nextVision += 1.0 / fps;
    }
    if (!withCorr.d.valid) continue;
    PoseOut a = ComputePose(withCorr.d, gyro, t + 0.015, Rcb);
    PoseOut b = ComputePose(noCorr.d, gyro, t + 0.015, Rcb);
    if (have && t > 0.5) {
      maxJumpPos = std::max(maxJumpPos, Norm(a.p - lastP));
      maxJumpPosNo = std::max(maxJumpPosNo, Norm(b.p - lastPno));
      Mat3 d = lastR.T() * a.R;
      double tr = std::max(-1.0, std::min(1.0, (d.m[0][0] + d.m[1][1] + d.m[2][2] - 1) / 2));
      maxJumpRot = std::max(maxJumpRot, std::acos(tr) * 180 / 3.14159265358979);
      // précision vs vérité (la vérité à t+15ms)
      double yawT = w_true * (t + 0.015);
      Vec3 pT = {vx * (t + 0.015), 0, 0};
      maxErrPos = std::max(maxErrPos, Norm(a.p - pT));
      Mat3 e = ExpMap({0, 0, yawT}).T() * a.R;
      double tr2 = std::max(-1.0, std::min(1.0, (e.m[0][0] + e.m[1][1] + e.m[2][2] - 1) / 2));
      maxErrRot = std::max(maxErrRot, std::acos(tr2) * 180 / 3.14159265358979);
    }
    lastP = a.p; lastPno = b.p; lastR = a.R; have = true;
  }
  std::printf("plus gros saut de position entre 2 echantillons a 1 kHz : %.2f mm avec correction | %.2f mm sans\n",
              maxJumpPos * 1000, maxJumpPosNo * 1000);
  std::printf("plus gros saut de rotation : %.3f deg (vitesse vraie : %.3f deg par ms)\n", maxJumpRot, 90.0 / 1000);
  std::printf("erreur max de la pose publiee : %.1f mm, %.2f deg\n", maxErrPos * 1000, maxErrRot);
  std::fflush(stdout);
  assert(maxJumpPos < maxJumpPosNo * 0.6);   // la correction de continuité adoucit nettement les sauts
  assert(maxJumpRot < 0.2);                  // la rotation est fluide : pas de saut à la cadence caméra
  assert(maxErrRot < 1.0 && maxErrPos < 0.02);

  // une image sans nouveauté ne doit pas effacer la correction (même instant) ; Invalidate coupe la publication
  SnapData before = withCorr.d;
  UpdateSnap(withCorr, before.Rvc, before.t, before.v, before.tcap, before.tUpdate + 0.001, R_GV, 5, true, -1.6, 0.02, 0.041);
  assert(withCorr.d.corr.x == before.corr.x && withCorr.d.flags == 5 && withCorr.d.floorZ == -1.6 && withCorr.d.camDelay == 0.041);
  InvalidateSnap(withCorr);
  assert(!withCorr.d.valid);
  std::puts("TEST POSE PUBLIEE OK");
  return 0;
}
