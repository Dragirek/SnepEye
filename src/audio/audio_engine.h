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

struct Channel {
  std::atomic<float> vol{1.0f};
  std::atomic<bool> mute{false};
  float Gain() const { return mute.load() ? 0.f : vol.load(); }
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

  // niveaux (0..1+), decroissance rapide : lus par le thread qui envoie les niveaux
  std::atomic<float> peakIn{0}, peakVoice{0}, peakSfx{0}, peakMusic{0}, peakL{0}, peakR{0};
  std::atomic<bool> gateOpen{true};
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
    if (c == "PLAY" && w.size() >= 2) {
      Play((int)ToF(w[1], -1), w.size() > 2 ? ToF(w[2], 1) : 1.f, w.size() > 3 ? ToF(w[3]) : 0.f, w.size() > 4 ? ToF(w[4], 1) : 1.f, w.size() > 5 && ToF(w[5]) > 0.5f);
      return true;
    }
    if (c == "STOP" && w.size() >= 2) { Stop((int)ToF(w[1], -1)); return true; }
    if (c == "STOPALL") { StopAll(); return true; }
    if (c == "UNLOAD" && w.size() >= 2) { Unload((int)ToF(w[1], -1)); return true; }
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
  void Render(float* out, int frames) {
    mixL_.assign((size_t)frames, 0.f); mixR_.assign((size_t)frames, 0.f);
    float pv = 0, ps = 0, pm = 0;

    // voix
    {
      const size_t need = (size_t)frames;
      size_t avail = voiceRing.Size();
      if (!primed_ && avail >= need + 240) primed_ = true;
      if (primed_) {
        if (avail > kMaxFill) { voiceRing.Drop(avail - kKeepFill); avail = kKeepFill; }   // le micro et la sortie n'ont pas la meme horloge : on rattrape
        vbuf_.assign(need, 0.f);
        const size_t got = voiceRing.Read(vbuf_.data(), need);
        if (got < need) primed_ = false;   // panne de donnees : on refait un tampon d'amorcage
        const float g = ch[kVoice].Gain();
        for (size_t i = 0; i < got; i++) { const float s = vbuf_[i] * g; mixL_[i] += s; mixR_[i] += s; pv = std::max(pv, std::fabs(s)); }
      }
    }
    // soundboard
    {
      std::lock_guard<std::mutex> l(sm_);
      int active = 0;
      const float g = ch[kSfx].Gain();
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
          const float a = v.vol * v.env * g;
          const float l2 = (d[2 * i0] * (1 - f) + d[2 * i1] * f) * a * v.gl;
          const float r2 = (d[2 * i0 + 1] * (1 - f) + d[2 * i1 + 1] * f) * a * v.gr;
          mixL_[(size_t)i] += l2; mixR_[(size_t)i] += r2;
          ps = std::max(ps, std::max(std::fabs(l2), std::fabs(r2)));
          v.pos += v.rate;
        }
        if (done) v.snd.reset(); else active++;
      }
      activeVoices = active;
    }
    // musique
    if (!musicPaused.load()) {
      mbuf_.assign((size_t)frames * 2, 0.f);
      const size_t got = musicRing.Read(mbuf_.data(), (size_t)frames);
      musicConsumed += got;
      const float g = ch[kMusic].Gain();
      for (size_t i = 0; i < got; i++) {
        const float l2 = mbuf_[2 * i] * g, r2 = mbuf_[2 * i + 1] * g;
        mixL_[i] += l2; mixR_[i] += r2;
        pm = std::max(pm, std::max(std::fabs(l2), std::fabs(r2)));
      }
    }
    // maitre + limiteur doux
    const float gm = ch[kMaster].Gain();
    float pl = 0, pr = 0;
    for (int i = 0; i < frames; i++) {
      const float l2 = SoftLimit(mixL_[(size_t)i] * gm), r2 = SoftLimit(mixR_[(size_t)i] * gm);
      out[2 * i] = l2; out[2 * i + 1] = r2;
      pl = std::max(pl, std::fabs(l2)); pr = std::max(pr, std::fabs(r2));
      outScope.Push(0.5f * (l2 + r2));
    }
    peakVoice = std::max(pv, peakVoice.load() * 0.85f);
    peakSfx = std::max(ps, peakSfx.load() * 0.85f);
    peakMusic = std::max(pm, peakMusic.load() * 0.85f);
    peakL = std::max(pl, peakL.load() * 0.85f);
    peakR = std::max(pr, peakR.load() * 0.85f);
  }

  static float SoftLimit(float x) {
    const float ax = std::fabs(x);
    if (ax <= 0.7f) return x;
    const float y = 0.7f + 0.3f * std::tanh((ax - 0.7f) / 0.3f);
    return x < 0 ? -y : y;
  }
  size_t VoiceBuffered() { return voiceRing.Size(); }

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
};

}  // namespace ar6audio
