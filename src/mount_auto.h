#pragma once
// Calibration du montage de l'IMU en UN geste : un bouton, puis deux poses avec compte a rebours.
// Contrairement aux anciens boutons « 1 » puis « 2 » (une seule mesure instantanee, ratee des que le casque bougeait au clic),
// chaque pose est mesuree sur ~1 s en moyenne, seulement quand le casque est vraiment immobile ; si ce n'est pas le cas
// la mesure attend (jusqu'a 12 s) puis recommence toute seule (3 essais). Pure logique, sans dependance (voir tests/).
#include <deque>
#include <string>

#include "orientation.h"

class MountAuto {
 public:
  enum State { Idle, Count1, Hold1, Count2, Hold2 };
  double countS = 6.0;      // compte a rebours avant chaque pose (le temps de lacher le casque)
  double windowS = 1.0;     // duree de la fenetre de mesure immobile
  double timeoutS = 12.0;   // attente maximale d'une pose immobile
  int maxAttempts = 3;

  bool Active() const { return st_ != Idle; }
  State state() const { return st_; }
  // Vrai une fois (puis remis a faux) quand une mesure vient d'etre prise : l'appelant joue un bip.
  bool TakeBeep() { bool b = beep_; beep_ = false; return b; }

  void Start(double now, std::string& msg) {
    st_ = Count1; t0_ = now; attempts_ = 0; last_ = -1; buf_.clear();
    msg = Countdown(0);
  }
  void Cancel(std::string& msg) {
    if (st_ != Idle) msg = "calibration du montage annulee";
    st_ = Idle;
  }

  // A appeler souvent (20 ms ... 8 ms). Renvoie vrai quand le montage est calcule (R valide).
  bool Update(double now, const Vec3& acc, const Vec3& gyr, Mat3& R, std::string& msg) {
    if (st_ == Idle) return false;
    if (st_ == Count1 || st_ == Count2) {
      const int step = st_ == Count1 ? 1 : 2;
      const double left = (st_ == Count1 ? countS : countS - 1.0) - (now - t0_);
      if (left <= 0) {
        st_ = step == 1 ? Hold1 : Hold2;
        t0_ = now; buf_.clear(); beep_ = true;
        msg = "etape " + std::to_string(step) + "/2 : mesure en cours, ne bouge plus";
        return false;
      }
      int sec = (int)std::ceil(left);
      if (sec != last_) { last_ = sec; msg = Countdown(sec); }
      return false;
    }
    // ---- Hold1 / Hold2 : fenetre glissante de mesure
    const int step = st_ == Hold1 ? 1 : 2;
    buf_.push_back({now, acc, gyr});
    while (!buf_.empty() && now - buf_.front().t > windowS) buf_.pop_front();
    if (now - t0_ > timeoutS) return Fail(now, step, "le casque n'est pas reste immobile", msg);
    if (now - t0_ < windowS || buf_.size() < 8 || now - buf_.front().t < windowS * 0.9) return false;
    Vec3 ma, mg;
    for (const auto& s : buf_) { ma = ma + s.a; mg = mg + s.g; }
    const double n = (double)buf_.size();
    ma = ma * (1.0 / n);
    double gyrMean = 0, devMax = 0;
    for (const auto& s : buf_) { gyrMean += Norm(s.g); devMax = std::max(devMax, Norm(s.a - ma)); }
    gyrMean /= n;
    if (gyrMean > 0.25 || devMax > 1.0) return false;   // pas immobile : on attend la prochaine fenetre
    std::string m;
    if (step == 1) {
      if (!MountStep1(ma, Vec3(), up_, m)) return Fail(now, 1, m, msg);
      st_ = Count2; t0_ = now; last_ = -1; beep_ = true;
      msg = "etape 1/2 OK. " + Countdown(0).substr(0);
      return false;
    }
    if (!MountStep2(up_, ma, Vec3(), R, m)) return Fail(now, 2, m, msg);
    st_ = Idle; beep_ = true;
    msg = m;
    return true;
  }

 private:
  struct Sample { double t; Vec3 a, g; };
  State st_ = Idle;
  double t0_ = 0;
  int attempts_ = 0, last_ = -1;
  bool beep_ = false;
  Vec3 up_;
  std::deque<Sample> buf_;

  std::string Countdown(int sec) const {
    const bool one = st_ == Count1;
    std::string s = one ? "etape 1/2 : pose le casque a plat, a l'endroit, et lache-le"
                        : "etape 2/2 : penche le casque vers l'avant (~30 deg, nez en haut) et tiens-le immobile";
    if (sec > 0) s += " - mesure dans " + std::to_string(sec) + " s";
    return s;
  }
  bool Fail(double now, int step, const std::string& why, std::string& msg) {
    if (++attempts_ >= maxAttempts) {
      st_ = Idle;
      msg = "echec de la calibration (" + why + "). Recommence en posant le casque bien a plat sur une table.";
      return false;
    }
    st_ = step == 1 ? Count1 : Count2;
    t0_ = now; last_ = -1; buf_.clear();
    msg = "essai " + std::to_string(attempts_ + 1) + "/" + std::to_string(maxAttempts) + " : " + why + ". " + Countdown(0);
    return false;
  }
};
