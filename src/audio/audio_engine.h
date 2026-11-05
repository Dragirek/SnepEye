#pragma once
// Melangeur portable d'ar6dof_audio : voix (micro -> effets), soundboard polyphonique, musique, volumes, mutes, niveaux.
// Aucune API Windows ici : tout est teste sous Linux (tests/audio_engine_test.cpp). La partie Windows (WASAPI, Media Foundation, UDP)
// est dans ar6dof_audio.cpp.
#include <atomic>
#include <cstdlib>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>
#include "audio_deck.h"
#include "audio_dsp.h"

namespace ar6audio {

// ------------------------------------------------------------------ tampon circulaire (verrou tres court, quelques centaines d'acces par seconde)
class Ring {
 public:
  Ring(int ch, size_t frames) : ch_(ch), cap_(frames), buf_(frames * (size_t)ch, 0.f) {}
  size_t Write(const float* in, size_t n) {   // renvoie le nombre de trames ecrites (le surplus est perdu)
    std::lock_guard<std::mutex> l(m_);
    n = std::min(n, cap_ - count_);
    for (size_t i = 0; i < n; i++) {
      std::memcpy(&buf_[w_ * ch_], in + i * ch_, sizeof(float) * ch_);
      w_ = (w_ + 1) % cap_;
    }
    count_ += n;
    return n;
  }
  size_t Read(float* out, size_t n) {
    std::lock_guard<std::mutex> l(m_);
    n = std::min(n, count_);
    for (size_t i = 0; i < n; i++) {
      std::memcpy(out + i * ch_, &buf_[r_ * ch_], sizeof(float) * ch_);
      r_ = (r_ + 1) % cap_;
    }
    count_ -= n;
    return n;
  }
  void Drop(size_t n) { std::lock_guard<std::mutex> l(m_); n = std::min(n, count_); r_ = (r_ + n) % cap_; count_ -= n; }
  size_t Size() { std::lock_guard<std::mutex> l(m_); return count_; }
  size_t Free() { std::lock_guard<std::mutex> l(m_); return cap_ - count_; }
  void Clear() { std::lock_guard<std::mutex> l(m_); r_ = w_ = count_ = 0; }
 private:
  int ch_;
  size_t cap_;
  std::vector<float> buf_;
  size_t r_ = 0, w_ = 0, count_ = 0;
  std::mutex m_;
};

// 1024 derniers echantillons pour le spectre (ecriture par un thread audio, lecture par le thread des niveaux : un petit decalage visuel est sans importance)
class Scope {
 public:
  Scope() { for (auto& v : d_) v.store(0.f, std::memory_order_relaxed); }
  void Push(float x) { const unsigned i = w_.load(std::memory_order_relaxed); d_[i & 1023].store(x, std::memory_order_relaxed); w_.store(i + 1, std::memory_order_release); }
  void Snapshot(float* out1024) const {
    const unsigned w = w_.load(std::memory_order_acquire);
    for (unsigned i = 0; i < 1024; i++) out1024[i] = d_[(w + i) & 1023].load(std::memory_order_relaxed);
  }
 private:
  std::atomic<float> d_[1024];
  std::atomic<unsigned> w_{0};
};

struct Sound {
  std::vector<float> data;   // stereo entrelace, 48 kHz
  size_t frames() const { return data.size() / 2; }
  double seconds() const { return (double)frames() / kRate; }
};

// Une tranche de la table de mixage : fader, muet, solo, balance et egaliseur 3 bandes (graves / medium / aigus, en dB).
// Ecrite par le thread des commandes, lue par le thread audio : de simples atomiques, sans verrou.
struct Channel {
  std::atomic<float> vol{1.0f};
  std::atomic<bool> mute{false}, solo{false};
  std::atomic<float> pan{0.f};                        // -1 gauche ... +1 droite
  std::atomic<float> eqLo{0.f}, eqMid{0.f}, eqHi{0.f};   // dB, -12 ... +12
  float Gain() const { return mute.load() ? 0.f : vol.load(); }
};

// Filtres d'une tranche (deux voies). Aucun calcul tant que l'egaliseur est a plat.
struct StripState {
  Biquad lo[2], mid[2], hi[2];
  float aLo = 1e9f, aMid = 1e9f, aHi = 1e9f;   // dernieres valeurs appliquees (1e9 : jamais calcule)
  bool eqOn = false;
  static constexpr float kLoHz = 120.f, kMidHz = 1000.f, kHiHz = 6000.f, kMidQ = 0.9f;
  void Update(const Channel& c) {
    const float l = c.eqLo.load(), m = c.eqMid.load(), h = c.eqHi.load();
    if (l == aLo && m == aMid && h == aHi) return;
    aLo = l; aMid = m; aHi = h;
    for (int i = 0; i < 2; i++) {
      if (std::fabs(l) < 0.05f) lo[i].Off(); else lo[i].LowShelf(kLoHz, l);
      if (std::fabs(m) < 0.05f) mid[i].Off(); else mid[i].Peak(kMidHz, kMidQ, m);
      if (std::fabs(h) < 0.05f) hi[i].Off(); else hi[i].HighShelf(kHiHz, h);
    }
    eqOn = std::fabs(l) >= 0.05f || std::fabs(m) >= 0.05f || std::fabs(h) >= 0.05f;
  }
};

// ------------------------------------------------------------------ reglages : « V cle valeur »
inline bool SetVoiceParam(VoiceParams& p, const std::string& k, float v) {
  struct E { const char* k; float* f; };
  const E tf[] = {{"in", &p.inGain}, {"gthr", &p.gateThr}, {"gatt", &p.gateAtt}, {"grel", &p.gateRel}, {"hp", &p.hp}, {"lp", &p.lp},
                  {"eql", &p.eqLow}, {"eqm", &p.eqMid}, {"eqh", &p.eqHigh}, {"pitch", &p.pitch}, {"robot", &p.robot}, {"robothz", &p.robotHz},
                  {"echo", &p.echoMix}, {"echoms", &p.echoMs}, {"echofb", &p.echoFb}, {"rev", &p.revMix}, {"room", &p.revRoom}, {"damp", &p.revDamp},
                  {"drive", &p.drive}, {"crush", &p.crush}, {"cthr", &p.compThr}, {"cratio", &p.compRatio}, {"out", &p.outGain}};
  for (const auto& e : tf) if (k == e.k) { *e.f = v; return true; }
  if (k == "on") { p.on = v > 0.5f; return true; }
  if (k == "gate") { p.gateOn = v > 0.5f; return true; }
  if (k == "comp") { p.compOn = v > 0.5f; return true; }
  return false;
}

inline std::vector<std::string> SplitWords(const std::string& s, size_t maxWords = 0) {
  std::vector<std::string> o;
  size_t i = 0;
  while (i < s.size()) {
    while (i < s.size() && (s[i] == ' ' || s[i] == '\t')) i++;
    if (i >= s.size()) break;
    if (maxWords && o.size() + 1 == maxWords) { size_t e = s.size(); while (e > i && (s[e - 1] == ' ' || s[e - 1] == '\r' || s[e - 1] == '\n')) e--; o.push_back(s.substr(i, e - i)); break; }
    size_t j = i;
    while (j < s.size() && s[j] != ' ' && s[j] != '\t') j++;
    o.push_back(s.substr(i, j - i));
    i = j;
  }
  return o;
}
inline std::string Upper(std::string s) { for (auto& c : s) c = (char)std::toupper((unsigned char)c); return s; }
inline float ToF(const std::string& s, float d = 0) { char* e = nullptr; const float v = std::strtof(s.c_str(), &e); return e == s.c_str() ? d : v; }

class Mixer {
 public:
  enum { kVoice, kSfx, kMusic, kMaster, kChannels };
  static constexpr int kMaxVoices = 64, kMaxSlots = 1024;

  Channel ch[kChannels];
  Ring voiceRing{1, kRate / 2};
  Ring musicRing{2, kRate * 2};
  Scope inScope, outScope;

  // platines DJ (A = 0, B = 1) : leur sortie est ajoutee a la tranche MUSIC, avant son egaliseur / fader et le maitre. Zero calcul tant qu'aucune piste n'est chargee.
  Deck deck[2];
  std::atomic<float> xfade{0.5f};   // 0 = tout sur A, 1 = tout sur B
  std::atomic<int> xfCurve{0};      // 0 = mix (les deux a fond au centre), 1 = coupe (saut net pres des bords)
  void CrossGains(float& ga, float& gb) const {
    const float v = Clampf(xfade.load(), 0.f, 1.f);
    if (xfCurve.load() == 1) { ga = Clampf((1.f - v) * 16.f, 0.f, 1.f); gb = Clampf(v * 16.f, 0.f, 1.f); }
    else { ga = std::min(1.f, 2.f * (1.f - v)); gb = std::min(1.f, 2.f * v); }
  }
  static int DeckIdx(const std::string& n) {
    const std::string u = Upper(n);
    if (u == "0" || u == "A") return 0;
    if (u == "1" || u == "B") return 1;
    return -1;
  }

  // niveaux (0..1+), decroissance rapide : lus par le thread qui envoie les niveaux
  std::atomic<float> peakIn{0}, peakVoice{0}, peakSfx{0}, peakMusic{0}, peakL{0}, peakR{0};
  std::atomic<bool> gateOpen{true};
  std::atomic<bool> limiter{true};   // limiteur doux du maitre ; coupe : simple ecretage a +-1
  std::atomic<int> activeVoices{0};

  // musique
  std::atomic<bool> musicActive{false}, musicPaused{false}, musicLoop{false};
  std::atomic<double> musicDur{0}, musicBase{0};
  std::atomic<unsigned long long> musicConsumed{0};
  double MusicPosition() const {
    double p = musicBase.load() + (double)musicConsumed.load() / kRate;
    const double d = musicDur.load();
    if (musicLoop.load() && d > 0.5) p = std::fmod(p, d);
    return p;
  }
  void MusicReset(double offsetSec) { musicRing.Clear(); musicBase = offsetSec; musicConsumed = 0; }

  // ---------------- voix
  void SetVoiceParamLine(const std::vector<std::string>& w) {   // w = {"V", cle, valeur, cle, valeur...}
    std::lock_guard<std::mutex> l(pm_);
    for (size_t i = 1; i + 1 < w.size(); i += 2) SetVoiceParam(pending_, w[i], ToF(w[i + 1]));
    dirty_ = true;
  }
  // appele par le thread de capture : mono 48 kHz brut -> chaine d'effets -> tampon de la voix
  void PushCapture(const float* mono, int n) {
    if (dirty_.exchange(false)) { std::lock_guard<std::mutex> l(pm_); chain_.SetParams(pending_); }
    float pk = 0;
    proc_.resize((size_t)n);
    for (int i = 0; i < n; i++) {
      const float x = mono[i];
      pk = std::max(pk, std::fabs(x));
      inScope.Push(x);
      proc_[(size_t)i] = chain_.Process(x);
    }
    peakIn = std::max(pk, peakIn.load() * 0.85f);
    gateOpen = chain_.gateOpen();
    voiceRing.Write(proc_.data(), (size_t)n);
  }

  // ---------------- soundboard
  void SetSound(int slot, std::shared_ptr<const Sound> s) {
    if (slot < 0 || slot >= kMaxSlots) return;
    std::lock_guard<std::mutex> l(sm_);
    sounds_[slot] = std::move(s);
  }
  void Unload(int slot) { Stop(slot); std::lock_guard<std::mutex> l(sm_); sounds_.erase(slot); }
  double SoundSeconds(int slot) { std::lock_guard<std::mutex> l(sm_); auto it = sounds_.find(slot); return it == sounds_.end() ? -1 : it->second->seconds(); }
  bool Play(int slot, float vol, float pan, float rate, bool loop) {
    std::lock_guard<std::mutex> l(sm_);
    auto it = sounds_.find(slot);
    if (it == sounds_.end() || it->second->frames() < 2) return false;
    Voice nv;
    nv.snd = it->second; nv.slot = slot; nv.vol = Clampf(vol, 0, 4); nv.rate = Clampf(rate, 0.25f, 4.f); nv.loop = loop;
    pan = Clampf(pan, -1, 1);
    nv.gl = std::min(1.f, 1.f - pan); nv.gr = std::min(1.f, 1.f + pan);
    nv.seq = ++seq_;
    Voice* slotp = nullptr;
    for (auto& v : voices_) if (!v.snd) { slotp = &v; break; }
    if (!slotp) {   // plein : on remplace la voix la plus ancienne (jamais de refus)
      slotp = &voices_[0];
      for (auto& v : voices_) if (v.seq < slotp->seq) slotp = &v;
    }
    *slotp = nv;
    return true;
  }
  void Stop(int slot) { std::lock_guard<std::mutex> l(sm_); for (auto& v : voices_) if (v.snd && v.slot == slot) v.stopping = true; }
  void StopAll() { std::lock_guard<std::mutex> l(sm_); for (auto& v : voices_) if (v.snd) v.stopping = true; }

  // ---------------- commandes portables ; renvoie vrai si la commande a ete traitee ici
  bool Handle(const std::string& line) {
    const auto w = SplitWords(line);
    if (w.empty()) return false;
    const std::string c = Upper(w[0]);
    if (c == "V") { SetVoiceParamLine(w); return true; }
    if (c == "VOL" && w.size() >= 3) { const int i = ChanIndex(w[1]); if (i >= 0) ch[i].vol = Clampf(ToF(w[2], 1), 0, 4); return true; }
    if (c == "MUTE" && w.size() >= 3) { const int i = ChanIndex(w[1]); if (i >= 0) ch[i].mute = ToF(w[2]) > 0.5f; return true; }
    if (c == "SOLO" && w.size() >= 3) { const int i = ChanIndex(w[1]); if (i >= 0 && i != kMaster) ch[i].solo = ToF(w[2]) > 0.5f; return true; }
    if (c == "PAN" && w.size() >= 3) { const int i = ChanIndex(w[1]); if (i >= 0) ch[i].pan = Clampf(ToF(w[2]), -1.f, 1.f); return true; }
    if (c == "EQ" && w.size() >= 5) {   // EQ <canal> <graves dB> <medium dB> <aigus dB>
      const int i = ChanIndex(w[1]);
      if (i >= 0) { ch[i].eqLo = Clampf(ToF(w[2]), -12.f, 12.f); ch[i].eqMid = Clampf(ToF(w[3]), -12.f, 12.f); ch[i].eqHi = Clampf(ToF(w[4]), -12.f, 12.f); }
      return true;
    }
    if (c == "LIMIT" && w.size() >= 2) { limiter = ToF(w[1], 1) > 0.5f; return true; }
    if (c == "PLAY" && w.size() >= 2) {
      Play((int)ToF(w[1], -1), w.size() > 2 ? ToF(w[2], 1) : 1.f, w.size() > 3 ? ToF(w[3]) : 0.f, w.size() > 4 ? ToF(w[4], 1) : 1.f, w.size() > 5 && ToF(w[5]) > 0.5f);
      return true;
    }
    if (c == "STOP" && w.size() >= 2) { Stop((int)ToF(w[1], -1)); return true; }
    if (c == "STOPALL") { StopAll(); return true; }
    if (c == "UNLOAD" && w.size() >= 2) { Unload((int)ToF(w[1], -1)); return true; }
    // ---- platines : D<commande> <platine A|B|0|1> <valeurs>
    if (c == "XFADE" && w.size() >= 2) { xfade = Clampf(ToF(w[1], 0.5f), 0.f, 1.f); return true; }
    if (c == "XFCURVE" && w.size() >= 2) { xfCurve = ToF(w[1]) > 0.5f ? 1 : 0; return true; }
    if (w.size() >= 2 && c.size() >= 2 && c[0] == 'D') {
      const int d = DeckIdx(w[1]);
      if (d >= 0) {
        Deck& k = deck[d];
        const std::string v2 = w.size() > 2 ? w[2] : std::string();
        const float v = ToF(v2);
        if (c == "DPLAY") { if (k.Loaded()) k.playing = v > 0.5f; return true; }
        if (c == "DSEEK") { k.Seek((double)v); return true; }
        if (c == "DTEMPO") { k.tempo = Clampf(ToF(v2, 1.f), 0.5f, 1.5f); return true; }
        if (c == "DBEND") { k.bend = Clampf(v, -1.f, 1.f); return true; }
        if (c == "DVOL") { k.vol = Clampf(ToF(v2, 1.f), 0.f, 1.5f); return true; }
        if (c == "DTRIM") { k.trim = Clampf(ToF(v2, 1.f), 0.f, 2.f); return true; }
        if (c == "DEQ" && w.size() >= 5) { k.eqLo = Clampf(ToF(w[2]), -30.f, 6.f); k.eqMid = Clampf(ToF(w[3]), -30.f, 6.f); k.eqHi = Clampf(ToF(w[4]), -30.f, 6.f); return true; }
        if (c == "DFILT") { k.filt = Clampf(v, -1.f, 1.f); return true; }
        if (c == "DLOOP") { k.SetLoop(v); return true; }
        if (c == "DSYNC") { k.SyncTo(deck[1 - d]); return true; }
        if (c == "DEJECT") { k.Eject(); return true; }
      }
    }
    return false;
  }
  static int ChanIndex(const std::string& n) {
    const std::string u = Upper(n);
    if (u == "VOICE") return kVoice;
    if (u == "SFX") return kSfx;
    if (u == "MUSIC") return kMusic;
    if (u == "MASTER") return kMaster;
    return -1;
  }

  // ---------------- rendu : appele par le thread de sortie ; out = stereo entrelace
  // Chaque source (voix, soundboard, musique) est melangee a plat dans sa tranche, puis la tranche applique egaliseur, fader (muet / solo) et balance
  // avant d'arriver au maitre, qui a la meme chaine puis le limiteur.
  void Render(float* out, int frames) {
    const size_t nf = (size_t)frames;
    mixL_.assign(nf, 0.f); mixR_.assign(nf, 0.f);
    for (int i = 0; i < 3; i++) { sl_[i].assign(nf, 0.f); sr_[i].assign(nf, 0.f); }
    anySolo_ = ch[kVoice].solo.load() || ch[kSfx].solo.load() || ch[kMusic].solo.load();

    // voix
    {
      size_t avail = voiceRing.Size();
      if (!primed_ && avail >= nf + 240) primed_ = true;
      if (primed_) {
        if (avail > kMaxFill) { voiceRing.Drop(avail - kKeepFill); avail = kKeepFill; }   // le micro et la sortie n'ont pas la meme horloge : on rattrape
        vbuf_.assign(nf, 0.f);
        const size_t got = voiceRing.Read(vbuf_.data(), nf);
        if (got < nf) primed_ = false;   // panne de donnees : on refait un tampon d'amorcage
        for (size_t i = 0; i < got; i++) { sl_[kVoice][i] = vbuf_[i]; sr_[kVoice][i] = vbuf_[i]; }
      }
    }
    // soundboard
    {
      std::lock_guard<std::mutex> l(sm_);
      int active = 0;
      float* L = sl_[kSfx].data();
      float* R = sr_[kSfx].data();
      for (auto& v : voices_) {
        if (!v.snd) continue;
        const float* d = v.snd->data.data();
        const double n = (double)v.snd->frames();
        bool done = false;
        for (int i = 0; i < frames; i++) {
          if (v.pos >= n) { if (v.loop) v.pos = std::fmod(v.pos, n); else { done = true; break; } }
          if (v.stopping) { v.env -= 1.f / 480.f; if (v.env <= 0) { done = true; break; } }
          else if (v.env < 1.f) v.env = std::min(1.f, v.env + 1.f / 96.f);
          const size_t i0 = (size_t)v.pos;
          size_t i1 = i0 + 1;
          if (i1 >= (size_t)n) i1 = v.loop ? 0 : i0;
          const float f = (float)(v.pos - (double)i0);
          const float a = v.vol * v.env;
          L[i] += (d[2 * i0] * (1 - f) + d[2 * i1] * f) * a * v.gl;
          R[i] += (d[2 * i0 + 1] * (1 - f) + d[2 * i1 + 1] * f) * a * v.gr;
          v.pos += v.rate;
        }
        if (done) v.snd.reset(); else active++;
      }
      activeVoices = active;
    }
    // musique
    if (!musicPaused.load()) {
      mbuf_.assign(nf * 2, 0.f);
      const size_t got = musicRing.Read(mbuf_.data(), nf);
      musicConsumed += got;
      for (size_t i = 0; i < got; i++) { sl_[kMusic][i] = mbuf_[2 * i]; sr_[kMusic][i] = mbuf_[2 * i + 1]; }
    }
    // platines -> tranche MUSIC
    {
      float ga, gb;
      CrossGains(ga, gb);
      deck[0].Render(sl_[kMusic].data(), sr_[kMusic].data(), frames, ga);
      deck[1].Render(sl_[kMusic].data(), sr_[kMusic].data(), frames, gb);
    }
    // tranches -> maitre
    float pk[3] = {0, 0, 0};
    for (int c = 0; c < 3; c++) {
      pk[c] = Strip(c, sl_[c].data(), sr_[c].data(), frames);
      for (size_t i = 0; i < nf; i++) { mixL_[i] += sl_[c][i]; mixR_[i] += sr_[c][i]; }
    }
    Strip(kMaster, mixL_.data(), mixR_.data(), frames);
    const bool lim = limiter.load();
    float pl = 0, pr = 0;
    for (int i = 0; i < frames; i++) {
      const float l2 = lim ? SoftLimit(mixL_[(size_t)i]) : Clampf(mixL_[(size_t)i], -1.f, 1.f);
      const float r2 = lim ? SoftLimit(mixR_[(size_t)i]) : Clampf(mixR_[(size_t)i], -1.f, 1.f);
      out[2 * i] = l2; out[2 * i + 1] = r2;
      // niveau du maitre mesure AVANT le limiteur : au-dessus de 1.0 (0 dB) le signal aurait sature, le voyant rouge de la console s'allume
      pl = std::max(pl, std::fabs(mixL_[(size_t)i])); pr = std::max(pr, std::fabs(mixR_[(size_t)i]));
      outScope.Push(0.5f * (l2 + r2));
    }
    peakVoice = std::max(pk[kVoice], peakVoice.load() * 0.85f);
    peakSfx = std::max(pk[kSfx], peakSfx.load() * 0.85f);
    peakMusic = std::max(pk[kMusic], peakMusic.load() * 0.85f);
    peakL = std::max(pl, peakL.load() * 0.85f);
    peakR = std::max(pr, peakR.load() * 0.85f);
  }

  // Gain reel d'une tranche : fader, muet, et solo (si une des trois sources est en solo, les autres se taisent ; le maitre n'est jamais concerne).
  float EffectiveGain(int c) const {
    const Channel& k = ch[c];
    if (k.mute.load()) return 0.f;
    if (c != kMaster && anySolo_ && !k.solo.load()) return 0.f;
    return k.vol.load();
  }

  static float SoftLimit(float x) {
    const float ax = std::fabs(x);
    if (ax <= 0.7f) return x;
    const float y = 0.7f + 0.3f * std::tanh((ax - 0.7f) / 0.3f);
    return x < 0 ? -y : y;
  }
  size_t VoiceBuffered() { return voiceRing.Size(); }

  // Traite une tranche en place (egaliseur -> fader -> balance) ; renvoie le niveau de crete apres traitement.
  float Strip(int c, float* L, float* R, int n) {
    StripState& st = strip_[c];
    st.Update(ch[c]);
    const float g = EffectiveGain(c), pan = ch[c].pan.load();
    const float gl = std::min(1.f, 1.f - pan) * g, gr = std::min(1.f, 1.f + pan) * g;
    float pk = 0;
    for (int i = 0; i < n; i++) {
      float l = L[i], r = R[i];
      if (st.eqOn) {
        l = st.hi[0].Process(st.mid[0].Process(st.lo[0].Process(l)));
        r = st.hi[1].Process(st.mid[1].Process(st.lo[1].Process(r)));
      }
      l *= gl; r *= gr;
      L[i] = l; R[i] = r;
      pk = std::max(pk, std::max(std::fabs(l), std::fabs(r)));
    }
    return pk;
  }

 private:
  struct Voice {
    std::shared_ptr<const Sound> snd;
    int slot = -1;
    double pos = 0, rate = 1;
    float vol = 1, gl = 1, gr = 1, env = 0;
    bool loop = false, stopping = false;
    unsigned long long seq = 0;
  };
  static constexpr size_t kMaxFill = 2400, kKeepFill = 960;   // 50 ms / 20 ms
  std::mutex sm_, pm_;
  std::map<int, std::shared_ptr<const Sound>> sounds_;
  Voice voices_[kMaxVoices];
  unsigned long long seq_ = 0;
  VoiceChain chain_;
  VoiceParams pending_;
  std::atomic<bool> dirty_{true};
  bool primed_ = false;
  std::vector<float> proc_, mixL_, mixR_, vbuf_, mbuf_;
  std::vector<float> sl_[3], sr_[3];
  StripState strip_[kChannels];
  bool anySolo_ = false;
};

}  // namespace ar6audio
