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
  // 11) table de mixage : balance, solo, egaliseur, limiteur coupe
  {
    // balance de la tranche soundboard : tout a droite -> la gauche se tait
    Mixer m; m.SetSound(7, Tone(440, 3.f, 0.5f));
    m.Handle("PLAY 7 1 0 1 1");
    m.Handle("PAN sfx 1");
    std::vector<float> o(480 * 2);
    float l = 0, r = 0;
    for (int b = 0; b < 4; b++) { m.Render(o.data(), 480); for (int i = 0; i < 480; i++) { l = std::max(l, std::fabs(o[2 * i])); r = std::max(r, std::fabs(o[2 * i + 1])); } }
    CHECK(l < 0.001f && r > 0.2f);
    // solo musique alors que le son joue : le son se tait ; le solo retire, il revient
    m.Handle("PAN sfx 0");
    m.Handle("SOLO music 1");
    CHECK(RenderPeak(m, 4) == 0.f);
    m.Handle("SOLO music 0");
    CHECK(RenderPeak(m, 4) > 0.2f);
    // le maitre n'a pas de solo
    m.Handle("SOLO master 1");
    CHECK(RenderPeak(m, 4) > 0.2f);
  }
  {
    // egaliseur : -12 dB de graves sur un 60 Hz le coupe nettement, +0 le laisse intact (et ne coute rien)
    auto level = [](const char* eqCmd) {
      Mixer m; m.SetSound(8, Tone(60, 3.f, 0.4f));
      m.Handle("PLAY 8 1 0 1 1");
      if (eqCmd) m.Handle(eqCmd);
      RenderPeak(m, 20);   // laisse le filtre s'etablir
      return RenderPeak(m, 10);
    };
    const float flat = level(nullptr), cut = level("EQ sfx -12 0 0"), boost = level("EQ sfx 12 0 0"), hiOnly = level("EQ sfx 0 0 12");
    CHECK(flat > 0.35f && flat < 0.45f);
    CHECK(cut < flat * 0.4f);
    CHECK(boost > flat * 1.8f);
    CHECK(std::fabs(hiOnly - flat) < 0.05f);   // des aigus ajoutes ne changent pas un 60 Hz
    Mixer m; m.Handle("EQ voice 99 -99 5");
    CHECK(m.ch[Mixer::kVoice].eqLo == 12.f && m.ch[Mixer::kVoice].eqMid == -12.f && m.ch[Mixer::kVoice].eqHi == 5.f);
  }
  {
    // limiteur coupe : ecretage dur a 1.0 ; limiteur actif : jamais au-dessus de 1 non plus, mais plus doux sous 1
    Mixer m; m.SetSound(9, Tone(200, 1.f, 0.9f));
    for (int i = 0; i < 30; i++) m.Play(9, 1.f, 0, 1, false);
    m.Handle("LIMIT 0");
    CHECK(!m.limiter);
    CHECK(RenderPeak(m, 5) == 1.f);
    m.Handle("LIMIT 1");
    CHECK(m.limiter);
  }
  // ---------------------------------------------------------------- platines DJ
  {
    // piste « techno » synthetique : un coup de grosse caisse a chaque temps (bpm donne), le premier a 'offset' secondes, + un charleston sur les contretemps
    auto Kick = [](float bpm, double offset, double sec) {
      auto t = std::make_shared<DeckTrack>();
      const size_t n = (size_t)(sec * kRate);
      std::vector<float> st(n * 2, 0.f);
      const double beat = 60.0 / bpm;
      for (double b = offset; b < sec - 0.3; b += beat) {
        const size_t i0 = (size_t)(b * kRate);
        for (size_t i = 0; i < 9600 && i0 + i < n; i++) {
          const float e = std::exp(-(float)i / 2400.f);
          const float v = 0.8f * e * std::sin(2 * kPi * (55.f + 90.f * std::exp(-(float)i / 600.f)) * (float)i / kRate);
          st[2 * (i0 + i)] += v; st[2 * (i0 + i) + 1] += v;
        }
        const size_t h0 = (size_t)((b + beat / 2) * kRate);
        for (size_t i = 0; i < 1200 && h0 + i < n; i++) {
          const float v = 0.15f * std::exp(-(float)i / 300.f) * (((i * 2654435761u) >> 9) % 2000 / 1000.f - 1.f);
          st[2 * (h0 + i)] += v; st[2 * (h0 + i) + 1] += v;
        }
      }
      t->Append(st.data(), n);
      AnalyzeTrack(*t);
      return t;
    };
    // forme d'onde : un point toutes les 20 ms
    {
      auto t = Kick(128.f, 0.2, 20.0);
      CHECK(t->peak.size() == (size_t)(20 * kWaveRate));
      CHECK(t->peak[10] > 100 || t->peak[11] > 100);   // 0,2 s : un coup de grosse caisse
      CHECK(t->low[10] > 150 || t->low[11] > 150);     // c'est surtout des graves
    }
    // tempo et premier temps retrouves, a differentes vitesses
    const float bpms[] = {90.f, 110.f, 128.f, 140.f, 174.f};
    for (float bpm : bpms) {
      auto t = Kick(bpm, 0.31, 60.0);
      CHECK(std::fabs(t->bpm - bpm) < 0.15f);
      const double beat = 60.0 / bpm;
      double d = std::fmod(t->firstBeat - 0.31, beat);
      if (d > beat / 2) d -= beat;
      if (d < -beat / 2) d += beat;
      CHECK(std::fabs(d) < 0.02);
      if (std::fabs(t->bpm - bpm) >= 0.15f || std::fabs(d) >= 0.02) std::printf("  tempo %.1f -> %.3f, decalage %.4f s\n", bpm, t->bpm, d);
    }
    // du bruit ne donne pas de tempo precis mais ne plante pas
    {
      auto t = std::make_shared<DeckTrack>();
      std::vector<float> st((size_t)kRate * 30 * 2);
      unsigned x = 1;
      for (auto& v : st) { x = x * 1664525u + 1013904223u; v = ((x >> 8) % 2000) / 10000.f - 0.1f; }
      t->Append(st.data(), st.size() / 2);
      AnalyzeTrack(*t);
      CHECK(t->bpm >= 0.f);
    }
    // lecture : rien tant qu'aucune piste, puis la piste avance au tempo demande
    {
      Mixer m;
      CHECK(RenderPeak(m, 5) == 0.f);
      auto t = Kick(120.f, 0.0, 30.0);
      m.deck[0].Load(t);
      CHECK(m.Handle("DPLAY A 1"));
      RenderPeak(m, 100);   // 1 s
      CHECK(std::fabs(m.deck[0].Position() - 1.0) < 0.05);
      CHECK(RenderPeak(m, 2) > 0.05f);
      m.Handle("DTEMPO A 1.5");
      RenderPeak(m, 100);
      CHECK(std::fabs(m.deck[0].Position() - 2.5) < 0.1);
      m.Handle("DPLAY A 0");
      RenderPeak(m, 10);
      const double p = m.deck[0].Position();
      RenderPeak(m, 50);
      CHECK(std::fabs(m.deck[0].Position() - p) < 1e-6);   // arrete : plus de mouvement
      CHECK(RenderPeak(m, 3) == 0.f);
      m.Handle("DSEEK A 10");
      CHECK(std::fabs(m.deck[0].Position() - 10.0) < 0.001);
      m.Handle("DSEEK A 9999");
      CHECK(m.deck[0].Position() < 30.0);
      m.Handle("DEJECT A");
      CHECK(!m.deck[0].Loaded());
      CHECK(RenderPeak(m, 3) == 0.f);
    }
    // fin de piste : la platine s'arrete d'elle-meme
    {
      Mixer m;
      m.deck[1].Load(Kick(120.f, 0.0, 2.0));
      m.Handle("DPLAY B 1");
      RenderPeak(m, 300);
      CHECK(!m.deck[1].playing);
    }
    // crossfader : courbe « mix » (les deux a fond au centre) et « coupe »
    {
      float a, b;
      Mixer m;
      m.Handle("XFADE 0"); m.CrossGains(a, b); CHECK(a == 1.f && b == 0.f);
      m.Handle("XFADE 1"); m.CrossGains(a, b); CHECK(a == 0.f && b == 1.f);
      m.Handle("XFADE 0.5"); m.CrossGains(a, b); CHECK(a == 1.f && b == 1.f);
      m.Handle("XFADE 0.25"); m.CrossGains(a, b); CHECK(std::fabs(a - 1.f) < 1e-6f && std::fabs(b - 0.5f) < 1e-6f);
      m.Handle("XFCURVE 1"); m.Handle("XFADE 0.45"); m.CrossGains(a, b); CHECK(a == 1.f && b == 1.f);
      m.Handle("XFADE 0.98"); m.CrossGains(a, b); CHECK(a < 0.4f && b == 1.f);   // coupe : plein presque jusqu'au bord, puis chute nette
      // la platine A, crossfader tout a droite, ne fait aucun bruit
      Mixer n;
      n.deck[0].Load(Kick(120.f, 0.0, 10.0));
      n.Handle("DPLAY A 1"); n.Handle("XFADE 1");
      RenderPeak(n, 10);
      CHECK(RenderPeak(n, 10) == 0.f);
      n.Handle("XFADE 0");
      CHECK(RenderPeak(n, 10) > 0.05f);
    }
    // boucle : de 4 temps a 120 BPM = 2 s ; la lecture reste dans la boucle
    {
      Mixer m;
      m.deck[0].Load(Kick(120.f, 0.0, 30.0));
      m.Handle("DSEEK A 8.1"); m.Handle("DPLAY A 1");
      RenderPeak(m, 5);
      m.Handle("DLOOP A 4");
      double lo = 1e9, hi = 0;
      for (int i = 0; i < 600; i++) { RenderPeak(m, 1); const double p = m.deck[0].Position(); lo = std::min(lo, p); hi = std::max(hi, p); }
      CHECK(hi - lo < 2.05 && hi - lo > 1.8);   // 2 s de boucle, jouee 6 s de suite
      m.Handle("DLOOP A 0");
      RenderPeak(m, 300);
      CHECK(m.deck[0].Position() > hi + 1.0);   // boucle coupee : la lecture repart
    }
    // synchro : B (100 BPM) se cale sur A (128 BPM), la phase aussi
    {
      Mixer m;
      m.deck[0].Load(Kick(128.f, 0.15, 40.0));
      m.deck[1].Load(Kick(100.f, 0.40, 40.0));
      m.Handle("DSEEK A 12.3"); m.Handle("DSEEK B 7.9");
      m.Handle("DSYNC B");
      CHECK(std::fabs(m.deck[1].tempo.load() - 1.28f) < 0.01f);
      const double a = m.deck[0].Position(), b = m.deck[1].Position();
      auto phase = [](double pos, double first, double bpm) { double x = (pos - first) / (60.0 / bpm); return x - std::floor(x); };
      double pa = phase(a, m.deck[0].Bpm() > 0 ? 0.15 : 0, m.deck[0].Bpm()), pb = phase(b, 0.40, m.deck[1].Bpm());
      double d = pa - pb; d -= std::floor(d + 0.5);
      CHECK(std::fabs(d) < 0.08);
      Mixer q;   // sans tempo connu : refus sans effet
      q.deck[0].Load(std::make_shared<DeckTrack>());
      q.Handle("DSYNC B");
      CHECK(q.deck[1].tempo.load() == 1.f);
    }
    // egaliseur / filtre de platine : les graves coupes baissent un 60 Hz ; commandes mal formees sans effet
    {
      auto level = [](const char* cmd) {
        Mixer m;
        auto t = std::make_shared<DeckTrack>();
        auto tone = Tone(60, 5.f, 0.4f);
        t->Append(tone->data.data(), tone->frames());
        m.deck[0].Load(t);
        m.Handle("DPLAY A 1");
        if (cmd) m.Handle(cmd);
        RenderPeak(m, 20);
        return RenderPeak(m, 10);
      };
      const float flat = level(nullptr), cut = level("DEQ A -30 0 0"), lpf = level("DFILT A 1");
      CHECK(flat > 0.3f && flat < 0.5f);
      CHECK(cut < flat * 0.2f);
      CHECK(lpf < flat * 0.2f);   // filtre passe-haut a fond : le 60 Hz disparait
      Mixer m;
      CHECK(!m.Handle("DLOAD A x"));   // gere par le programme principal (chargement), pas par le melangeur
      CHECK(m.Handle("DTEMPO A"));     // pas de valeur : sans effet, sans plantage
      CHECK(m.Handle("DPLAY Z 1") == false);
    }
  }
  if (!fails) std::printf("audio_engine_test : OK\n");
  return fails;
}
