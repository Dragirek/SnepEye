#pragma once
// Traitement de la voix et analyse du son : code portable (aucune dependance, teste sous Linux : tests/audio_dsp_test.cpp).
// Tout travaille en mono, flottant, 48 kHz.
#include <algorithm>
#include <cmath>
#include <complex>
#include <cstdint>
#include <cstring>
#include <vector>

namespace ar6audio {

constexpr int kRate = 48000;
constexpr float kPi = 3.14159265358979f;

inline float DbToLin(float db) { return std::pow(10.0f, db / 20.0f); }
inline float LinToDb(float l) { return 20.0f * std::log10(std::max(l, 1e-9f)); }
inline float Clampf(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }

// ------------------------------------------------------------------ filtre biquad (formules « Audio EQ Cookbook »)
struct Biquad {
  float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
  bool active = false;
  float Process(float x) {
    if (!active) return x;
    const float y = b0 * x + z1;
    z1 = b1 * x - a1 * y + z2;
    z2 = b2 * x - a2 * y;
    return y;
  }
  void Reset() { z1 = z2 = 0; }
  void Set(float nb0, float nb1, float nb2, float na0, float na1, float na2) {
    b0 = nb0 / na0; b1 = nb1 / na0; b2 = nb2 / na0; a1 = na1 / na0; a2 = na2 / na0;
    active = true;
  }
  void Off() { active = false; Reset(); }
  void LowPass(float fc, float q = 0.7071f) {
    const float w = 2 * kPi * fc / kRate, c = std::cos(w), a = std::sin(w) / (2 * q);
    Set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
  }
  void HighPass(float fc, float q = 0.7071f) {
    const float w = 2 * kPi * fc / kRate, c = std::cos(w), a = std::sin(w) / (2 * q);
    Set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
  }
  void LowShelf(float fc, float gainDb) {
    const float A = std::pow(10.0f, gainDb / 40.0f), w = 2 * kPi * fc / kRate, c = std::cos(w), s = std::sin(w);
    const float al = s / 2 * std::sqrt(2.0f), t = 2 * std::sqrt(A) * al;
    Set(A * ((A + 1) - (A - 1) * c + t), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - t),
        (A + 1) + (A - 1) * c + t, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - t);
  }
  void HighShelf(float fc, float gainDb) {
    const float A = std::pow(10.0f, gainDb / 40.0f), w = 2 * kPi * fc / kRate, c = std::cos(w), s = std::sin(w);
    const float al = s / 2 * std::sqrt(2.0f), t = 2 * std::sqrt(A) * al;
    Set(A * ((A + 1) + (A - 1) * c + t), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - t),
        (A + 1) - (A - 1) * c + t, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - t);
  }
  void Peak(float fc, float q, float gainDb) {
    const float A = std::pow(10.0f, gainDb / 40.0f), w = 2 * kPi * fc / kRate, c = std::cos(w), a = std::sin(w) / (2 * q);
    Set(1 + a * A, -2 * c, 1 - a * A, 1 + a / A, -2 * c, 1 - a / A);
  }
};

// ------------------------------------------------------------------ changement de hauteur (deux lectures croisees d'une ligne a retard)
// Latence ~ 17 ms. Une voix tient bien de -12 a +12 demi-tons ; aux extremes un leger effet « chorus » est normal.
class PitchShifter {
 public:
  PitchShifter() : buf_(kSize, 0.f) {}
  void SetSemitones(float st) { ratio_ = std::pow(2.0f, st / 12.0f); }
  float Process(float x) {
    buf_[w_] = x;
    const float inc = (1.0f - ratio_) / kWin;
    phase_ += inc;
    phase_ -= std::floor(phase_);
    float p2 = phase_ + 0.5f;
    p2 -= std::floor(p2);
    const float y = Tap(phase_ * kWin) * Win(phase_) + Tap(p2 * kWin) * Win(p2);
    w_ = (w_ + 1) % kSize;
    return y;
  }
  void Reset() { std::fill(buf_.begin(), buf_.end(), 0.f); phase_ = 0; }
 private:
  static constexpr int kSize = 8192;
  static constexpr float kWin = 1600.f;   // 33 ms
  static float Win(float p) { const float s = std::sin(kPi * p); return s * s; }
  float Tap(float delay) const {
    float pos = (float)w_ - delay - 1.0f;
    while (pos < 0) pos += kSize;
    const int i0 = (int)pos, i1 = (i0 + 1) % kSize;
    const float f = pos - (float)i0;
    return buf_[i0 % kSize] * (1 - f) + buf_[i1] * f;
  }
  std::vector<float> buf_;
  int w_ = 0;
  float ratio_ = 1.0f, phase_ = 0.f;
};

// ------------------------------------------------------------------ echo
class Echo {
 public:
  Echo() : buf_(kRate * 2, 0.f) {}
  float Process(float x, float ms, float fb, float mix) {
    const int d = std::max(1, std::min((int)(ms * kRate / 1000.f), (int)buf_.size() - 1));
    const int r = (w_ - d + (int)buf_.size()) % (int)buf_.size();
    const float y = buf_[r];
    buf_[w_] = x + y * fb;
    w_ = (w_ + 1) % (int)buf_.size();
    return x + y * mix;
  }
  void Reset() { std::fill(buf_.begin(), buf_.end(), 0.f); }
 private:
  std::vector<float> buf_;
  int w_ = 0;
};

// ------------------------------------------------------------------ reverberation (Freeverb simplifie : 4 peignes + 2 passe-tout)
class Reverb {
 public:
  Reverb() {
    static const int combs[4] = {1116, 1188, 1277, 1356};
    static const int aps[2] = {556, 441};
    for (int i = 0; i < 4; i++) { comb_[i].buf.assign((size_t)(combs[i] * kRate / 44100), 0.f); }
    for (int i = 0; i < 2; i++) { ap_[i].buf.assign((size_t)(aps[i] * kRate / 44100), 0.f); }
  }
  float Process(float x, float room, float damp, float mix) {
    const float fb = 0.70f + 0.28f * Clampf(room, 0, 1), d = Clampf(damp, 0, 1) * 0.4f;
    float s = 0;
    const float in = x * 0.015f * 8.0f;
    for (auto& c : comb_) {
      const float o = c.buf[c.i];
      c.f = o * (1 - d) + c.f * d;
      c.buf[c.i] = in + c.f * fb;
      c.i = (c.i + 1) % c.buf.size();
      s += o;
    }
    for (auto& a : ap_) {
      const float o = a.buf[a.i];
      const float y = -s + o;
      a.buf[a.i] = s + o * 0.5f;
      a.i = (a.i + 1) % a.buf.size();
      s = y;
    }
    return x * (1.0f - 0.5f * mix) + s * mix;
  }
  void Reset() { for (auto& c : comb_) { std::fill(c.buf.begin(), c.buf.end(), 0.f); c.f = 0; } for (auto& a : ap_) std::fill(a.buf.begin(), a.buf.end(), 0.f); }
 private:
  struct Comb { std::vector<float> buf; size_t i = 0; float f = 0; };
  struct Ap { std::vector<float> buf; size_t i = 0; };
  Comb comb_[4];
  Ap ap_[2];
};

// ------------------------------------------------------------------ reglages de la voix
struct VoiceParams {
  bool on = true;            // false : voix brute (seuls le gain d'entree et de sortie s'appliquent)
  float inGain = 0;          // dB
  bool gateOn = true;
  float gateThr = -50;       // dB : en dessous, la voix est coupee (bruit de fond, souffle)
  float gateAtt = 5;         // ms
  float gateRel = 120;       // ms
  float hp = 80;             // Hz, 0 = coupe-bas desactive
  float lp = 20000;          // Hz, >= 19000 = desactive
  float eqLow = 0, eqMid = 0, eqHigh = 0;   // dB
  float pitch = 0;           // demi-tons
  float robot = 0;           // 0..1
  float robotHz = 60;
  float echoMix = 0, echoMs = 220, echoFb = 0.35f;
  float revMix = 0, revRoom = 0.5f, revDamp = 0.4f;
  float drive = 0;           // 0..1 distorsion
  float crush = 16;          // bits (16 = desactive)
  bool compOn = true;
  float compThr = -18, compRatio = 3;
  float outGain = 0;         // dB
};

class VoiceChain {
 public:
  void SetParams(const VoiceParams& p) {
    const bool pitchChanged = std::fabs(p.pitch - p_.pitch) > 1e-4f;
    const bool filtChanged = first_ || p.hp != p_.hp || p.lp != p_.lp || p.eqLow != p_.eqLow || p.eqMid != p_.eqMid || p.eqHigh != p_.eqHigh;
    p_ = p;
    if (first_ || pitchChanged) pitch_.SetSemitones(p_.pitch);
    if (filtChanged) {
      if (p_.hp > 20.f) hp_.HighPass(p_.hp); else hp_.Off();
      if (p_.lp < 19000.f) lp_.LowPass(p_.lp); else lp_.Off();
      if (std::fabs(p_.eqLow) > 0.05f) low_.LowShelf(200.f, p_.eqLow); else low_.Off();
      if (std::fabs(p_.eqMid) > 0.05f) mid_.Peak(1500.f, 0.8f, p_.eqMid); else mid_.Off();
      if (std::fabs(p_.eqHigh) > 0.05f) high_.HighShelf(5000.f, p_.eqHigh); else high_.Off();
    }
    first_ = false;
  }
  const VoiceParams& params() const { return p_; }
  bool gateOpen() const { return gateOpen_; }

  float Process(float x) {
    x *= DbToLin(p_.inGain);
    if (p_.on) {
      // porte de bruit
      if (p_.gateOn) {
        const float a = std::fabs(x);
        const float ca = 1.0f - std::exp(-1.0f / (0.001f * 3.0f * kRate));
        env_ += (a > env_ ? ca : (1.0f - std::exp(-1.0f / (0.001f * 150.f * kRate)))) * (a - env_);
        const float thr = DbToLin(p_.gateThr);
        const bool want = env_ > thr || (gateOpen_ && env_ > thr * 0.7f);   // hysteresis
        gateOpen_ = want;
        const float tgt = want ? 1.f : 0.f;
        const float ms = want ? std::max(0.5f, p_.gateAtt) : std::max(1.f, p_.gateRel);
        gain_ += (1.0f - std::exp(-1.0f / (0.001f * ms * kRate))) * (tgt - gain_);
        x *= gain_;
      } else { gateOpen_ = true; gain_ = 1.f; }
      x = hp_.Process(x);
      x = low_.Process(x);
      x = mid_.Process(x);
      x = high_.Process(x);
      x = lp_.Process(x);
      if (std::fabs(p_.pitch) > 0.01f) x = pitch_.Process(x);
      if (p_.robot > 0.01f) {
        ph_ += 2 * kPi * p_.robotHz / kRate;
        if (ph_ > 2 * kPi) ph_ -= 2 * kPi;
        x *= (1.f - p_.robot) + p_.robot * std::sin(ph_);
      }
      if (p_.drive > 0.01f) { const float g = 1.f + p_.drive * 25.f; x = std::tanh(x * g) / std::tanh(g * 0.5f + 0.5f) * 0.8f; }
      if (p_.crush < 15.5f) { const float l = std::pow(2.f, std::max(2.f, p_.crush) - 1.f); x = std::round(x * l) / l; }
      if (p_.echoMix > 0.01f) x = echo_.Process(x, p_.echoMs, Clampf(p_.echoFb, 0, 0.9f), p_.echoMix);
      if (p_.revMix > 0.01f) x = rev_.Process(x, p_.revRoom, p_.revDamp, p_.revMix);
      if (p_.compOn) {
        const float a = std::fabs(x);
        cenv_ += (a > cenv_ ? 1.0f - std::exp(-1.0f / (0.005f * kRate)) : 1.0f - std::exp(-1.0f / (0.08f * kRate))) * (a - cenv_);
        const float lvl = LinToDb(cenv_);
        float red = 0;
        if (lvl > p_.compThr) red = (lvl - p_.compThr) * (1.f - 1.f / std::max(1.f, p_.compRatio));
        x *= DbToLin(-red);
        x *= DbToLin(red * 0.45f);   // compensation partielle du niveau
      }
    }
    x *= DbToLin(p_.outGain);
    // limiteur doux : transparent sous 0.7, puis courbe qui tend vers 1.0 sans jamais le depasser
    const float ax = std::fabs(x);
    if (ax <= 0.7f) return x;
    const float y = 0.7f + 0.3f * std::tanh((ax - 0.7f) / 0.3f);
    return x < 0 ? -y : y;
  }

  void Reset() { pitch_.Reset(); echo_.Reset(); rev_.Reset(); hp_.Reset(); lp_.Reset(); low_.Reset(); mid_.Reset(); high_.Reset(); env_ = cenv_ = 0; gain_ = 1; }

 private:
  VoiceParams p_;
  bool first_ = true, gateOpen_ = true;
  Biquad hp_, lp_, low_, mid_, high_;
  PitchShifter pitch_;
  Echo echo_;
  Reverb rev_;
  float env_ = 0, gain_ = 1, cenv_ = 0, ph_ = 0;
};

// ------------------------------------------------------------------ FFT et spectre (visualisation du son)
inline void Fft(std::vector<std::complex<float>>& a) {
  const size_t n = a.size();
  for (size_t i = 1, j = 0; i < n; i++) {
    size_t bit = n >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) std::swap(a[i], a[j]);
  }
  for (size_t len = 2; len <= n; len <<= 1) {
    const float ang = -2 * kPi / (float)len;
    const std::complex<float> wl(std::cos(ang), std::sin(ang));
    for (size_t i = 0; i < n; i += len) {
      std::complex<float> w(1, 0);
      for (size_t k = 0; k < len / 2; k++) {
        const std::complex<float> u = a[i + k], v = a[i + k + len / 2] * w;
        a[i + k] = u + v;
        a[i + k + len / 2] = u - v;
        w *= wl;
      }
    }
  }
}

// 32 bandes logarithmiques (60 Hz ... 12 kHz) de 0 a 1 a partir des 1024 derniers echantillons.
constexpr int kBands = 32;
inline void Spectrum32(const float* last1024, float* out) {
  std::vector<std::complex<float>> a(1024);
  for (int i = 0; i < 1024; i++) {
    const float w = 0.5f - 0.5f * std::cos(2 * kPi * i / 1023.f);
    a[i] = std::complex<float>(last1024[i] * w, 0);
  }
  Fft(a);
  const float binHz = (float)kRate / 1024.f;
  for (int b = 0; b < kBands; b++) {
    const float f0 = 60.f * std::pow(12000.f / 60.f, (float)b / kBands), f1 = 60.f * std::pow(12000.f / 60.f, (float)(b + 1) / kBands);
    int i0 = std::max(1, (int)std::floor(f0 / binHz)), i1 = std::max(i0 + 1, (int)std::ceil(f1 / binHz));
    i1 = std::min(i1, 511);
    float m = 0;
    for (int i = i0; i < i1; i++) m = std::max(m, std::abs(a[i]));
    const float db = LinToDb(m / 256.f);   // ~0 dB = pleine echelle
    out[b] = Clampf((db + 72.f) / 72.f, 0.f, 1.f);
  }
}

}  // namespace ar6audio
