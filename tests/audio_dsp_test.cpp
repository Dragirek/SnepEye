// g++ -std=c++17 -O2 -Isrc/audio tests/audio_dsp_test.cpp
#include <cstdio>
#include "audio_dsp.h"
using namespace ar6audio;
static int fails = 0;
#define CHECK(c) do { if (!(c)) { std::printf("ECHEC ligne %d : %s\n", __LINE__, #c); fails++; } } while (0)

static float DominantHz(const std::vector<float>& x, size_t start) {
  std::vector<std::complex<float>> a(8192);
  for (int i = 0; i < 8192; i++) { float w = 0.5f - 0.5f * std::cos(2 * kPi * i / 8191.f); a[i] = {x[start + i] * w, 0}; }
  Fft(a);
  int best = 1; float bm = 0;
  for (int i = 1; i < 4096; i++) if (std::abs(a[i]) > bm) { bm = std::abs(a[i]); best = i; }
  return best * (float)kRate / 8192.f;
}
static std::vector<float> Sine(float hz, int n, float amp = 0.5f) {
  std::vector<float> v(n);
  for (int i = 0; i < n; i++) v[i] = amp * std::sin(2 * kPi * hz * i / kRate);
  return v;
}
static float Rms(const std::vector<float>& x, size_t a, size_t b) { double s = 0; for (size_t i = a; i < b; i++) s += x[i] * x[i]; return (float)std::sqrt(s / (b - a)); }

int main() {
  // 1) hauteur : +12 demi-tons double la frequence, -12 la divise par deux
  for (float st : {12.f, -12.f, 5.f, -7.f}) {
    VoiceParams p; p.on = true; p.gateOn = false; p.hp = 0; p.compOn = false; p.pitch = st;
    VoiceChain c; c.SetParams(p);
    auto in = Sine(240.f, kRate * 2);   // 240 Hz = periode de 200 echantillons, divise la fenetre (1600)
    std::vector<float> out(in.size());
    for (size_t i = 0; i < in.size(); i++) out[i] = c.Process(in[i]);
    const float hz = DominantHz(out, kRate);
    const float want = 240.f * std::pow(2.f, st / 12.f);
    std::printf("pitch %+0.0f : %.1f Hz (attendu %.1f)\n", st, hz, want);
    CHECK(std::fabs(hz - want) / want < 0.04f);
  }
  // 2) porte de bruit : un souffle faible est coupe, une voix forte passe
  {
    VoiceParams p; p.gateOn = true; p.gateThr = -40; p.hp = 0; p.compOn = false;
    VoiceChain c; c.SetParams(p);
    auto quiet = Sine(300.f, kRate, 0.003f), loud = Sine(300.f, kRate, 0.3f);
    std::vector<float> oq(quiet.size()), ol(loud.size());
    for (size_t i = 0; i < quiet.size(); i++) oq[i] = c.Process(quiet[i]);
    for (size_t i = 0; i < loud.size(); i++) ol[i] = c.Process(loud[i]);
    CHECK(Rms(oq, kRate / 2, kRate) < 0.0004f);
    CHECK(Rms(ol, kRate / 2, kRate) > 0.15f);
  }
  // 3) coupe-bas : 50 Hz fortement attenue, 1 kHz conserve
  {
    VoiceParams p; p.gateOn = false; p.hp = 300; p.compOn = false;
    VoiceChain c; c.SetParams(p);
    auto lo = Sine(50.f, kRate), hi = Sine(1000.f, kRate);
    std::vector<float> a(lo.size()), b(hi.size());
    for (size_t i = 0; i < lo.size(); i++) a[i] = c.Process(lo[i]);
    c.Reset();
    for (size_t i = 0; i < hi.size(); i++) b[i] = c.Process(hi[i]);
    CHECK(Rms(a, kRate / 2, kRate) < 0.05f);
    CHECK(Rms(b, kRate / 2, kRate) > 0.30f);
  }
  // 4) limiteur : jamais au-dela de 1.0, meme avec un gain enorme
  {
    VoiceParams p; p.gateOn = false; p.hp = 0; p.compOn = false; p.inGain = 24; p.outGain = 12;
    VoiceChain c; c.SetParams(p);
    float mx = 0;
    for (int i = 0; i < kRate; i++) mx = std::max(mx, std::fabs(c.Process(0.9f * std::sin(2 * kPi * 440.f * i / kRate))));
    CHECK(mx <= 1.0001f);
  }
  // 5) spectre : un 1 kHz allume la bonne bande
  {
    auto s = Sine(1000.f, 1024, 0.5f);
    float out[kBands];
    Spectrum32(s.data(), out);
    int best = 0; for (int b = 1; b < kBands; b++) if (out[b] > out[best]) best = b;
    const float fc = 60.f * std::pow(200.f, (best + 0.5f) / kBands);
    std::printf("spectre : bande %d (~%.0f Hz)\n", best, fc);
    CHECK(fc > 800.f && fc < 1250.f);
  }
  // 6) echo et reverberation produisent de la queue sonore, sans exploser
  {
    VoiceParams p; p.gateOn = false; p.hp = 0; p.compOn = false; p.echoMix = 0.5f; p.revMix = 0.5f;
    VoiceChain c; c.SetParams(p);
    float e = 0, mx = 0;
    for (int i = 0; i < kRate * 3; i++) { float y = c.Process(i < 4800 ? 0.5f * std::sin(i * 0.2f) : 0.f); if (i > kRate) e += std::fabs(y); mx = std::max(mx, std::fabs(y)); }
    CHECK(e > 0.01f);
    CHECK(mx < 1.01f);
  }
  if (!fails) std::printf("audio_dsp_test : OK\n");
  return fails;
}
