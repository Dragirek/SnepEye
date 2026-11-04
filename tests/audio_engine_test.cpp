// g++ -std=c++17 -O2 -Isrc/audio tests/audio_engine_test.cpp -lpthread
#include <cstdio>
#include "audio_engine.h"
using namespace ar6audio;
static int fails = 0;
#define CHECK(c) do { if (!(c)) { std::printf("ECHEC ligne %d : %s\n", __LINE__, #c); fails++; } } while (0)

static std::shared_ptr<Sound> Tone(float hz, float sec, float amp = 0.5f) {
  auto s = std::make_shared<Sound>();
  const int n = (int)(sec * kRate);
  s->data.resize((size_t)n * 2);
  for (int i = 0; i < n; i++) s->data[2 * i] = s->data[2 * i + 1] = amp * std::sin(2 * kPi * hz * i / kRate);
  return s;
}
static float RenderPeak(Mixer& m, int blocks, int frames = 480) {
  std::vector<float> o((size_t)frames * 2);
  float pk = 0;
  for (int b = 0; b < blocks; b++) { m.Render(o.data(), frames); for (float v : o) pk = std::max(pk, std::fabs(v)); }
  return pk;
}

int main() {
  // 1) rendu silencieux sans rien
  { Mixer m; CHECK(RenderPeak(m, 5) == 0.f); }
  // 2) lecture d'un son, fin naturelle, polyphonie (spam sans couper)
  {
    Mixer m; m.SetSound(1, Tone(440, 0.5f));
    CHECK(m.Handle("PLAY 1 1 0 1 0"));
    RenderPeak(m, 2);
    CHECK(m.activeVoices == 1);
    for (int i = 0; i < 10; i++) { m.Handle("PLAY 1"); RenderPeak(m, 1); }
    CHECK(m.activeVoices == 11);   // aucune voix n'a ete coupee par la suivante
    RenderPeak(m, 100);            // 1 s plus tard tout est fini
    CHECK(m.activeVoices == 0);
  }
  // 3) plus de 64 voix : on remplace les plus anciennes, jamais de crash
  {
    Mixer m; m.SetSound(2, Tone(300, 2.f));
    for (int i = 0; i < 200; i++) m.Play(2, 0.1f, 0, 1, false);
    RenderPeak(m, 1);
    CHECK(m.activeVoices == Mixer::kMaxVoices);
  }
  // 4) mute, volume, STOP avec fondu
  {
    Mixer m; m.SetSound(3, Tone(440, 5.f));
    m.Handle("PLAY 3 1 0 1 1");
    CHECK(RenderPeak(m, 5) > 0.2f);
    m.Handle("MUTE sfx 1");
    CHECK(RenderPeak(m, 3) == 0.f);
    m.Handle("MUTE sfx 0"); m.Handle("VOL sfx 0.5");
    const float p = RenderPeak(m, 3);
    CHECK(p > 0.15f && p < 0.3f);
    m.Handle("STOP 3");
    RenderPeak(m, 3);
    CHECK(m.activeVoices == 0);
    CHECK(RenderPeak(m, 2) == 0.f);
  }
  // 5) boucle : un son court qui boucle continue de jouer
  {
    Mixer m; m.SetSound(4, Tone(1000, 0.05f));
    m.Handle("PLAY 4 1 0 1 1");
    RenderPeak(m, 50);
    CHECK(m.activeVoices == 1);
    m.Handle("STOPALL"); RenderPeak(m, 3);
    CHECK(m.activeVoices == 0);
  }
  // 6) panoramique
  {
    Mixer m; m.SetSound(5, Tone(440, 1.f));
    m.Handle("PLAY 5 1 -1");
    std::vector<float> o(960);
    float l = 0, r = 0;
    for (int b = 0; b < 3; b++) { m.Render(o.data(), 480); for (int i = 0; i < 480; i++) { l = std::max(l, std::fabs(o[2 * i])); r = std::max(r, std::fabs(o[2 * i + 1])); } }
    CHECK(l > 0.2f && r < 0.001f);
  }
  // 7) voix : passe par la chaine d'effets, mute immediat, rattrapage de derive d'horloge
  {
    Mixer m;
    m.Handle("V gate 0 hp 0 comp 0");
    std::vector<float> in(480);
    for (int b = 0; b < 30; b++) {   // 300 ms de voix
      for (int i = 0; i < 480; i++) in[(size_t)i] = 0.4f * std::sin(2 * kPi * 300.f * (b * 480 + i) / kRate);
      m.PushCapture(in.data(), 480);
      std::vector<float> o(960); m.Render(o.data(), 480);
    }
    CHECK(m.peakIn > 0.3f);
    CHECK(m.peakVoice > 0.2f);
    m.Handle("MUTE voice 1");
    for (int b = 0; b < 40; b++) { m.PushCapture(in.data(), 480); std::vector<float> o(960); m.Render(o.data(), 480); }
    CHECK(m.peakVoice < 0.01f);
    m.Handle("MUTE voice 0");
    // le micro va 0,5 % plus vite que la sortie pendant 60 s : le tampon ne doit pas deborder ni grossir sans fin
    Mixer d; d.Handle("V gate 0 hp 0 comp 0");
    size_t mx = 0; double acc = 0;
    for (int b = 0; b < 6000; b++) {
      acc += 480 * 1.005;
      int n = (int)acc; acc -= n;
      std::vector<float> t((size_t)n, 0.1f); d.PushCapture(t.data(), n);
      std::vector<float> oo(960); d.Render(oo.data(), 480);
      mx = std::max(mx, d.VoiceBuffered());
    }
    std::printf("tampon voix max : %zu echantillons (%.0f ms)\n", mx, mx * 1000.0 / kRate);
    CHECK(mx < 4000);
  }
  // 8) musique : consommation, pause, position
  {
    Mixer m; m.musicActive = true; m.musicDur = 100;
    std::vector<float> st(960 * 2, 0.3f);
    m.musicRing.Write(st.data(), 960);
    std::vector<float> o(960); m.Render(o.data(), 480);
    CHECK(m.musicConsumed == 480);
    m.musicPaused = true; m.Render(o.data(), 480);
    CHECK(m.musicConsumed == 480);
    m.musicPaused = false; m.Render(o.data(), 480);
    CHECK(m.musicConsumed == 960);
    CHECK(std::fabs(m.MusicPosition() - 0.02) < 1e-6);
    m.MusicReset(30); CHECK(std::fabs(m.MusicPosition() - 30.0) < 1e-6);
  }
  // 9) analyse de la ligne de commande
  {
    auto w = SplitWords("LOAD 3 C:\\Mes sons\\a b.wav", 3);
    CHECK(w.size() == 3 && w[2] == "C:\\Mes sons\\a b.wav");
    VoiceParams p; CHECK(SetVoiceParam(p, "pitch", 4.f) && p.pitch == 4.f); CHECK(!SetVoiceParam(p, "nimporte", 1));
  }
  // 10) limiteur du maitre : plein de voix fortes ne depassent jamais 1.0
  {
    Mixer m; m.SetSound(6, Tone(200, 1.f, 0.9f));
    for (int i = 0; i < 30; i++) m.Play(6, 1.f, 0, 1, false);
    CHECK(RenderPeak(m, 10) <= 1.0001f);
  }
  if (!fails) std::printf("audio_engine_test : OK\n");
  return fails;
}
