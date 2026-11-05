#pragma once
// Estime le délai entre l'instant où une image arrive et l'instant où elle a réellement été prise, en comparant la
// rotation de la caméra vue dans les images (vision) à celle intégrée depuis le gyro. Pour chaque délai candidat d,
// on intègre le gyro sur [t0-d, t1-d] et on mesure l'écart avec la rotation vue entre les deux images ; le bon
// délai est celui qui minimise l'écart. Il faut de la rotation de la tête (quelques secondes) pour que ce soit fiable.
#include <algorithm>
#include <cmath>
#include <deque>
#include <vector>

#include "fusion.h"

class DelayEstimator {
 public:
  // Rotation de la caméra (repère caméra) entre deux images consécutives suivies, aux instants d'arrivée t0 et t1.
  void AddStep(double t0, double t1, const Mat3& dRcam) {
    steps_.push_back({t0, t1, dRcam});
    if (steps_.size() > kMaxSteps) steps_.pop_front();
  }
  void Reset() { steps_.clear(); }
  size_t Steps() const { return steps_.size(); }

  // delay : meilleur délai (s). quality : 0..1 (1 = minimum net). Retourne faux s'il y a trop peu de rotation.
  bool Estimate(const std::deque<GyroSample>& gyro, const Mat3& Rcb, double& delay, double& quality) const {
    // On ne garde que les pas où la tête tourne vraiment.
    std::vector<const Step*> use;
    double motion = 0;
    for (size_t i = steps_.size() > kDelaySteps ? steps_.size() - kDelaySteps : 0; i < steps_.size(); i++) {   // les plus recents seulement (le calcul est en pas x candidats)
      const Step& s = steps_[i];
      double a = Angle(s.dR);
      if (a > 0.004) { use.push_back(&s); motion += a; }   // > 0,23 deg entre deux images
    }
    if (use.size() < 20 || motion < 0.35) return false;     // au moins ~20 deg de rotation cumulée
    const double lo = 0.0, hi = 0.12, step = 0.0025;
    const int n = (int)std::round((hi - lo) / step) + 1;
    std::vector<double> err(n, 0.0);
    for (int i = 0; i < n; i++) {
      double d = lo + i * step, e = 0;
      for (const Step* s : use) {
        Mat3 g = Rcb * IntegrateGyro(gyro, s->t0 - d, s->t1 - d) * Rcb.T();
        double r = Angle(s->dR.T() * g);
        e += r * r;
      }
      err[i] = e;
    }
    int best = 0;
    for (int i = 1; i < n; i++) if (err[i] < err[best]) best = i;
    double d = lo + best * step;
    if (best > 0 && best < n - 1) {   // affinage parabolique autour du minimum
      double y0 = err[best - 1], y1 = err[best], y2 = err[best + 1];
      double den = y0 - 2 * y1 + y2;
      if (den > 1e-18) d += 0.5 * (y0 - y2) / den * step;
    }
    double mean = 0;
    for (double e : err) mean += e;
    mean /= n;
    quality = mean > 1e-18 ? std::max(0.0, std::min(1.0, 1.0 - err[best] / mean)) : 0.0;
    delay = std::max(0.0, std::min(hi, d));
    return quality > 0.3;  // minimum net, sinon on garde l'ancienne valeur
  }

  // « Zoom du mode » : rapport entre la rotation vue par la camera et celle du gyro. La rotation vue = rotation vraie x (focale vraie / focale supposee) :
  // 1 = les focales supposees sont justes ; 2 = la camera a une focale deux fois plus grande que supposee (mode recadre au lieu d'etre reduit).
  // Il faut multiplier les focales supposees par ce rapport. Mediane ponderee par l'angle du gyro, donc peu sensible aux pas bruites.
  // spread : dispersion relative (0 = parfait). n : pas utilises. motion : rotation cumulee du gyro (rad). Retourne faux s'il y a trop peu de rotation.
  bool EstimateZoom(const std::deque<GyroSample>& gyro, const Mat3& Rcb, double delay, double& zoom, double& spread, int& n, double& motion) const {
    std::vector<std::pair<double, double>> r;   // (rapport, poids)
    motion = 0;
    for (const auto& s : steps_) {
      const double ag = Angle(Rcb * IntegrateGyro(gyro, s.t0 - delay, s.t1 - delay) * Rcb.T());
      if (ag < 0.006) continue;   // moins de 0,34 deg entre deux images : le bruit domine
      r.push_back({Angle(s.dR) / ag, ag});
      motion += ag;
    }
    n = (int)r.size();
    zoom = 1.0;
    spread = 1.0;
    if (n < 30 || motion < 0.9) return false;   // au moins ~50 deg de rotation cumulee
    auto wmedian = [](std::vector<std::pair<double, double>> v) {
      std::sort(v.begin(), v.end());
      double tot = 0;
      for (const auto& p : v) tot += p.second;
      double acc = 0;
      for (const auto& p : v) { acc += p.second; if (acc >= 0.5 * tot) return p.first; }
      return v.back().first;
    };
    const double med = wmedian(r);
    std::vector<std::pair<double, double>> dev;
    for (const auto& p : r) dev.push_back({std::fabs(p.first - med), p.second});
    const double mad = wmedian(dev);
    zoom = med;
    spread = med > 1e-9 ? mad / med : 1.0;
    return med > 0.05 && med < 20.0;
  }

 private:
  struct Step { double t0, t1; Mat3 dR; };
  static double Angle(const Mat3& M) {
    double c = (M.m[0][0] + M.m[1][1] + M.m[2][2] - 1.0) * 0.5;
    return std::acos(std::max(-1.0, std::min(1.0, c)));
  }
  static constexpr size_t kMaxSteps = 300, kDelaySteps = 150;
  std::deque<Step> steps_;
};
