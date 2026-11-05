#pragma once
// Platines DJ d'ar6dof_audio : une piste entiere decodee en memoire (stereo 48 kHz, 16 bits), son analyse (forme d'onde, tempo, premier temps) et la lecture
// (tempo, pitch bend, boucle, synchro, egaliseur 3 bandes, filtre). Code portable, teste sous Linux (tests/audio_engine_test.cpp).
//
// Limites assumees :
//  - le tempo est un changement de vitesse de lecture (comme un vrai tourne-disque) : la hauteur du son suit le tempo, il n'y a pas de « key lock » ;
//  - une piste est gardee jusqu'a 10 minutes (au-dela, la fin est coupee) : 10 minutes = ~110 Mo par platine ;
//  - le tempo detecte est une estimation (sons percussifs reguliers) : il peut etre faux (moitie / double) ou absent sur de la musique sans pulsation.
#include <cstdint>
#include <memory>
#include <mutex>
#include <vector>
#include <atomic>
#include "audio_dsp.h"

namespace ar6audio {

constexpr int kWaveRate = 50;                       // points de la forme d'onde par seconde
constexpr double kDeckMaxSeconds = 600.0;           // longueur maximale gardee
constexpr int kOnsetRate = 200;                     // cadence de l'analyse du tempo (hop de 5 ms)

struct DeckTrack {
  std::vector<int16_t> pcm;                         // stereo entrelace, 48 kHz
  std::vector<uint8_t> peak, low;                   // forme d'onde : crete (compressee) et part de graves (0..255), kWaveRate points par seconde
  float bpm = 0.f;                                  // 0 = inconnu
  double firstBeat = 0.0;                           // secondes : un temps de la grille (le premier apres le debut)

  size_t frames() const { return pcm.size() / 2; }
  double seconds() const { return (double)frames() / kRate; }
  bool Full() const { return seconds() >= kDeckMaxSeconds; }
  // ajoute des echantillons flottants stereo ; renvoie le nombre de trames reellement gardees (la fin est coupee a la longueur maximale)
  size_t Append(const float* stereo, size_t n) {
    const size_t cap = (size_t)(kDeckMaxSeconds * kRate);
    if (frames() >= cap) return 0;
    n = std::min(n, cap - frames());
    const size_t base = pcm.size();
    pcm.resize(base + n * 2);
    for (size_t i = 0; i < n * 2; i++) {
      const float v = Clampf(stereo[i], -1.f, 1.f);
      pcm[base + i] = (int16_t)std::lrintf(v * 32767.f);
    }
    return n;
  }
};

// ------------------------------------------------------------------ analyse
inline void ComputeWave(DeckTrack& t) {
  const size_t n = t.frames();
  const size_t bucket = kRate / kWaveRate;           // 960 trames
  const size_t nb = (n + bucket - 1) / bucket;
  t.peak.assign(nb, 0); t.low.assign(nb, 0);
  const float aLp = 1.f - std::exp(-2.f * kPi * 150.f / kRate);
  float lp = 0.f;
  for (size_t b = 0; b < nb; b++) {
    float pk = 0.f, lk = 0.f;
    const size_t e = std::min(n, (b + 1) * bucket);
    for (size_t i = b * bucket; i < e; i++) {
      const float l = t.pcm[2 * i] * (1.f / 32768.f), r = t.pcm[2 * i + 1] * (1.f / 32768.f);
      const float m = 0.5f * (l + r);
      lp += aLp * (m - lp);
      pk = std::max(pk, std::max(std::fabs(l), std::fabs(r)));
      lk = std::max(lk, std::fabs(lp));
    }
    t.peak[b] = (uint8_t)std::min(255.f, 255.f * std::sqrt(std::min(1.f, pk)));
    t.low[b] = (uint8_t)std::min(255.f, 255.f * lk / (pk + 1e-4f));
  }
}

// Tempo et premier temps. Courbe d'attaques (hausses d'energie des graves et du signal complet, toutes les 5 ms) ->
// autocorrelation (estimation grossiere, 67..200 BPM, preference pour 125 BPM) -> affinage par repliement de la courbe sur la periode (pas de 0,01 BPM).
inline void AnalyzeTempo(DeckTrack& t) {
  t.bpm = 0.f; t.firstBeat = 0.0;
  const size_t n = t.frames();
  const int hop = kRate / kOnsetRate;                // 240
  const size_t nh = n / (size_t)hop;
  if (nh < (size_t)kOnsetRate * 8) return;           // moins de 8 s : pas d'estimation
  std::vector<float> on(nh, 0.f);
  const float aLp = 1.f - std::exp(-2.f * kPi * 200.f / kRate);
  float lp = 0.f, pLow = 0.f, pAll = 0.f;
  for (size_t h = 0; h < nh; h++) {
    float eLow = 0.f, eAll = 0.f;
    for (int i = 0; i < hop; i++) {
      const size_t k = h * (size_t)hop + (size_t)i;
      const float m = 0.5f * (t.pcm[2 * k] + t.pcm[2 * k + 1]) * (1.f / 32768.f);
      lp += aLp * (m - lp);
      eLow += lp * lp; eAll += m * m;
    }
    const float lLow = std::log(1.f + 1e4f * eLow / hop), lAll = std::log(1.f + 1e4f * eAll / hop);
    on[h] = std::max(0.f, lLow - pLow) + 0.5f * std::max(0.f, lAll - pAll);
    pLow = lLow; pAll = lAll;
  }
  double mean = 0.0;
  for (float v : on) mean += v;
  mean /= (double)nh;
  if (mean < 1e-6) return;                           // silence
  std::vector<float> oc(nh);
  for (size_t i = 0; i < nh; i++) oc[i] = on[i] - (float)mean;

  // 1) grossier : autocorrelation, lags de 60 a 180 (200 .. 66,7 BPM)
  const int lagMin = 60, lagMax = 180;
  std::vector<double> ac((size_t)lagMax + 2, 0.0);
  for (int lag = lagMin - 1; lag <= lagMax + 1; lag++) {
    double s = 0.0;
    for (size_t i = 0; i + (size_t)lag < nh; i++) s += (double)oc[i] * oc[i + (size_t)lag];
    ac[(size_t)lag] = s;
  }
  int best = -1;
  double bestScore = 0.0;
  for (int lag = lagMin; lag <= lagMax; lag++) {
    const double bpm = 60.0 * kOnsetRate / lag;
    const double lg = std::log2(bpm / 125.0) / 0.9;
    const double score = ac[(size_t)lag] * std::exp(-0.5 * lg * lg);
    if (score > bestScore) { bestScore = score; best = lag; }
  }
  if (best < 0) return;
  double lagF = best;
  {
    const double a = ac[(size_t)best - 1], b = ac[(size_t)best], c = ac[(size_t)best + 1];
    const double den = a - 2.0 * b + c;
    if (std::fabs(den) > 1e-12) lagF += std::max(-0.5, std::min(0.5, 0.5 * (a - c) / den));
  }
  const double bpm0 = 60.0 * kOnsetRate / lagF;

  // 2) fin : on replie la courbe d'attaques sur chaque periode candidate ; la bonne periode concentre les attaques dans une seule case
  double bestSc = -1.0, bestBpm = bpm0, bestPhase = 0.0;
  std::vector<double> fold;
  for (double bpm = bpm0 * 0.99; bpm <= bpm0 * 1.01; bpm += 0.01) {
    const double T = 60.0 * kOnsetRate / bpm;       // periode en cases de 5 ms
    const size_t nbin = (size_t)T + 1;
    fold.assign(nbin + 2, 0.0);
    double ph = 0.0;
    for (size_t i = 0; i < nh; i++) {
      fold[(size_t)ph] += on[i];
      ph += 1.0;
      if (ph >= T) ph -= T;
    }
    double sc = 0.0;
    size_t sb = 0;
    for (size_t b = 0; b < nbin; b++) {
      const double l = fold[b == 0 ? nbin - 1 : b - 1], c = fold[b], r = fold[b + 1 < nbin ? b + 1 : 0];
      const double v = l + 2.0 * c + r;
      if (v > sc) { sc = v; sb = b; }
    }
    if (sc > bestSc) {
      bestSc = sc; bestBpm = bpm;
      const double l = fold[sb == 0 ? nbin - 1 : sb - 1], c = fold[sb], r = fold[sb + 1 < nbin ? sb + 1 : 0];
      const double d = l + c + r;
      bestPhase = (double)sb + (d > 1e-12 ? (r - l) / d : 0.0);
    }
  }
  const double T = 60.0 * kOnsetRate / bestBpm;
  double ph = bestPhase;
  while (ph < 0.0) ph += T;
  t.bpm = (float)bestBpm;
  t.firstBeat = (ph + 0.5) / kOnsetRate;
}

inline void AnalyzeTrack(DeckTrack& t) { ComputeWave(t); AnalyzeTempo(t); }

// ------------------------------------------------------------------ platine
class Deck {
 public:
  struct Status { double pos = 0, dur = 0; bool playing = false, loop = false, loaded = false; float tempo = 1.f, vu = 0.f; };

  std::atomic<bool> playing{false};
  std::atomic<float> tempo{1.f};                    // vitesse de lecture (1 = vitesse d'origine)
  std::atomic<float> bend{0.f};                     // -1..+1 : +-8 % tant qu'on le tient
  std::atomic<float> vol{1.f}, trim{1.f};
  std::atomic<float> eqLo{0.f}, eqMid{0.f}, eqHi{0.f};   // dB, -30 (coupe) .. +6
  std::atomic<float> filt{0.f};                     // -1 passe-bas ... 0 neutre ... +1 passe-haut
  std::atomic<float> vu{0.f};

  void Load(std::shared_ptr<const DeckTrack> t) {
    std::shared_ptr<const DeckTrack> old;
    {
      std::lock_guard<std::mutex> l(m_);
      old = std::move(track_);
      track_ = std::move(t);
      pos_ = 0.0; env_ = 0.f; loopOn_ = false;
      tempo = 1.f; bend = 0.f;                      // une nouvelle piste repart au tempo d'origine
      posOut_ = 0.0; durOut_ = track_ ? track_->seconds() : 0.0;
      loaded_ = (bool)track_;
    }
    playing = false;
    // `old` (jusqu'a ~110 Mo) est libere ici, hors du verrou du thread audio
  }
  void Eject() { Load(nullptr); tempo = 1.f; }
  bool Loaded() const { return loaded_.load(); }

  void Seek(double sec) {
    std::lock_guard<std::mutex> l(m_);
    if (!track_) return;
    pos_ = Clamp(sec * kRate, 0.0, (double)track_->frames() - 2.0);
    if (playing.load()) env_ = 0.f;                 // petit fondu : pas de claquement au saut
    posOut_ = pos_ / kRate;
  }

  // boucle de 'beats' temps a partir du temps le plus proche ; 0 = la couper
  void SetLoop(float beats) {
    std::lock_guard<std::mutex> l(m_);
    if (!track_ || beats <= 0.f) { loopOn_ = false; return; }
    const double beatLen = BeatLenFrames();
    double in = pos_;
    if (track_->bpm > 0.f) {
      const double t0 = track_->firstBeat * kRate;
      in = t0 + std::floor((pos_ - t0) / beatLen + 0.5) * beatLen;
      if (in < 0.0) in = 0.0;
    }
    const double len = std::max(64.0, (double)beats * beatLen);
    if (in + len >= (double)track_->frames() - 2.0) { loopOn_ = false; return; }
    loopIn_ = in; loopOut_ = in + len; loopOn_ = true;
  }

  // Cale ce tempo et cette phase sur l'autre platine (les deux grilles tombent ensemble). Faux si l'une des pistes n'a pas de tempo.
  bool SyncTo(Deck& o) {
    std::scoped_lock lk(m_, o.m_);
    if (!track_ || !o.track_ || track_->bpm <= 0.f || o.track_->bpm <= 0.f) return false;
    double target = (double)o.track_->bpm * o.tempo.load() / track_->bpm;
    while (target > 1.5) target *= 0.5;             // moitie / double : on garde le rapport le plus proche de 1
    while (target < 0.7) target *= 2.0;
    if (target < 0.5 || target > 1.5) return false;
    tempo = (float)target;
    const double pm = BeatPhase(), po = o.BeatPhase();
    double d = po - pm;
    d -= std::floor(d + 0.5);
    pos_ = Clamp(pos_ + d * BeatLenFrames(), 0.0, (double)track_->frames() - 2.0);
    posOut_ = pos_ / kRate;
    return true;
  }

  Status Get() {
    Status s;
    s.pos = posOut_.load(); s.dur = durOut_.load(); s.playing = playing.load(); s.loop = loopOut2_.load(); s.loaded = loaded_.load();
    s.tempo = tempo.load(); s.vu = vu.load();
    return s;
  }
  float Bpm() { std::lock_guard<std::mutex> l(m_); return track_ ? track_->bpm : 0.f; }
  double Position() { return posOut_.load(); }

  // ajoute la sortie de cette platine (gain 'xg' du crossfader) dans L / R
  void Render(float* L, float* R, int n, float xg) {
    std::lock_guard<std::mutex> l(m_);
    if (!track_) { vu = vu.load() * 0.85f; return; }
    UpdateFilters();
    const DeckTrack& t = *track_;
    const int16_t* d = t.pcm.data();
    const double last = (double)t.frames() - 2.0;
    const double rate = (double)tempo.load() * (1.0 + 0.08 * (double)bend.load());
    const float g = trim.load() * vol.load() * xg * (1.f / 32768.f);
    const bool eq = eqOn_, fl = fOn_;
    bool play = playing.load();
    float pk = 0.f;
    for (int i = 0; i < n; i++) {
      if (play) env_ = std::min(1.f, env_ + 1.f / 96.f); else env_ = std::max(0.f, env_ - 1.f / 96.f);
      if (env_ <= 0.f && !play) break;              // arrete : la position ne bouge plus
      if (loopOn_ && pos_ >= loopOut_) pos_ = loopIn_ + std::fmod(pos_ - loopIn_, loopOut_ - loopIn_);
      if (pos_ >= last) { pos_ = last; playing = false; play = false; env_ = 0.f; break; }
      const size_t i0 = (size_t)pos_;
      const float f = (float)(pos_ - (double)i0);
      float l = (d[2 * i0] * (1.f - f) + d[2 * i0 + 2] * f);
      float r = (d[2 * i0 + 1] * (1.f - f) + d[2 * i0 + 3] * f);
      if (eq) {
        l = hi_[0].Process(mid_[0].Process(lo_[0].Process(l)));
        r = hi_[1].Process(mid_[1].Process(lo_[1].Process(r)));
      }
      if (fl) { l = flt_[0].Process(l); r = flt_[1].Process(r); }
      l *= g * env_; r *= g * env_;
      L[i] += l; R[i] += r;
      pk = std::max(pk, std::max(std::fabs(l), std::fabs(r)));
      pos_ += rate;
    }
    vu = std::max(pk, vu.load() * 0.85f);
    posOut_ = pos_ / kRate;
    loopOut2_ = loopOn_;
  }

  // phase dans le temps courant, 0..1 (0 = pile sur un temps de la grille). Appelant : verrou pris.
  double BeatPhase() const {
    if (!track_ || track_->bpm <= 0.f) return 0.0;
    const double bl = BeatLenFrames();
    double x = (pos_ - track_->firstBeat * kRate) / bl;
    return x - std::floor(x);
  }

 private:
  static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }
  double BeatLenFrames() const { return track_ && track_->bpm > 0.f ? 60.0 / track_->bpm * kRate : 0.5 * kRate; }

  void UpdateFilters() {
    const float l = eqLo.load(), m = eqMid.load(), h = eqHi.load();
    if (l != aLo_ || m != aMid_ || h != aHi_) {
      aLo_ = l; aMid_ = m; aHi_ = h;
      for (int i = 0; i < 2; i++) {
        if (std::fabs(l) < 0.05f) lo_[i].Off(); else lo_[i].LowShelf(120.f, l);
        if (std::fabs(m) < 0.05f) mid_[i].Off(); else mid_[i].Peak(1000.f, 0.9f, m);
        if (std::fabs(h) < 0.05f) hi_[i].Off(); else hi_[i].HighShelf(6000.f, h);
      }
      eqOn_ = std::fabs(l) >= 0.05f || std::fabs(m) >= 0.05f || std::fabs(h) >= 0.05f;
    }
    const float f = Clampf(filt.load(), -1.f, 1.f);
    if (f != aF_) {
      aF_ = f;
      for (int i = 0; i < 2; i++) {
        if (f < -0.03f) flt_[i].LowPass(18000.f * std::pow(200.f / 18000.f, -f), 0.9f);
        else if (f > 0.03f) flt_[i].HighPass(30.f * std::pow(8000.f / 30.f, f), 0.9f);
        else flt_[i].Off();
      }
      fOn_ = std::fabs(f) > 0.03f;
    }
  }

  std::mutex m_;
  std::shared_ptr<const DeckTrack> track_;
  double pos_ = 0.0, loopIn_ = 0.0, loopOut_ = 0.0;
  float env_ = 0.f;
  bool loopOn_ = false;
  std::atomic<double> posOut_{0.0}, durOut_{0.0};
  std::atomic<bool> loaded_{false}, loopOut2_{false};
  Biquad lo_[2], mid_[2], hi_[2], flt_[2];
  float aLo_ = 1e9f, aMid_ = 1e9f, aHi_ = 1e9f, aF_ = 1e9f;
  bool eqOn_ = false, fOn_ = false;
};

}  // namespace ar6audio
