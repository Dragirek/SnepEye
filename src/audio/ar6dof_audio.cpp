// ar6dof_audio.exe : moteur audio d'AR6DoF. Petit programme separe, lance par Unity seulement quand le son est active.
//  - micro (WASAPI) -> effets de voix (audio_dsp.h) -> enceintes externes choisies (WASAPI), sans rien melanger du son de Windows ;
//  - soundboard polyphonique (mp3 / wav / aac / flac / wma... decodes par Media Foundation, deja dans Windows) ;
//  - lecteur de musique en flux ;
//  - niveaux et spectre envoyes a Unity (seulement quand il les demande).
// Commandes : UDP 127.0.0.1:9890 (texte, une ou plusieurs lignes). Niveaux : binaire vers 9891. Evenements : texte vers 9892.
// Options : --parent <pid> (s'arrete quand Unity s'arrete).
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <propsys.h>
#include <propidl.h>
#include <avrt.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <wrl/client.h>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include "audio_engine.h"

using Microsoft::WRL::ComPtr;
using namespace ar6audio;

static const int kPortCmd = 9890, kPortMeter = 9891, kPortEvent = 9892;

static Mixer g_mix;
static std::atomic<bool> g_quit{false};
static SOCKET g_tx = INVALID_SOCKET;   // envoi (niveaux + evenements)
static std::atomic<long long> g_meterUntil{0};

static long long NowMs() { return (long long)std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count(); }
static std::string Narrow(const wchar_t* w) {
  if (!w || !*w) return std::string();
  const int n = WideCharToMultiByte(CP_UTF8, 0, w, -1, nullptr, 0, nullptr, nullptr);
  std::string s((size_t)(n > 0 ? n - 1 : 0), '\0');
  if (n > 1) WideCharToMultiByte(CP_UTF8, 0, w, -1, &s[0], n, nullptr, nullptr);
  return s;
}
static std::wstring Widen(const std::string& s) {
  if (s.empty()) return std::wstring();
  const int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), nullptr, 0);
  std::wstring w((size_t)n, L'\0');
  MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), &w[0], n);
  return w;
}
static void SendTo(int port, const void* d, int n) {
  if (g_tx == INVALID_SOCKET) return;
  sockaddr_in a{};
  a.sin_family = AF_INET;
  a.sin_port = htons((u_short)port);
  inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
  sendto(g_tx, (const char*)d, n, 0, (sockaddr*)&a, sizeof(a));
}
static void Emit(const std::string& s) { SendTo(kPortEvent, s.data(), (int)s.size()); }
static std::string Hex(HRESULT hr) { char b[24]; std::snprintf(b, sizeof(b), "0x%08lX", (unsigned long)hr); return b; }

// ------------------------------------------------------------------ peripheriques
// Constantes ecrites ici pour ne pas dependre de ksmedia.h / functiondiscoverykeys_devpkey.h (qui exigent des bibliotheques en plus)
static const GUID kSubtypeFloat = {0x00000003, 0x0000, 0x0010, {0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71}};
static const PROPERTYKEY kKeyFriendlyName = {{0xa45c254e, 0xdf1c, 0x4efd, {0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0}}, 14};

static std::mutex g_cfgMx;
static std::string g_inId = "off", g_outId = "off";
static std::atomic<int> g_inGen{0}, g_outGen{0};
static HANDLE g_inEvt = nullptr, g_outEvt = nullptr;

static std::string GetCfg(const std::string& which) { std::lock_guard<std::mutex> l(g_cfgMx); return which == "in" ? g_inId : g_outId; }

static bool OpenDevice(IMMDeviceEnumerator* en, EDataFlow flow, const std::string& id, ComPtr<IMMDevice>& dev) {
  if (id == "default") return SUCCEEDED(en->GetDefaultAudioEndpoint(flow, eConsole, &dev));
  return SUCCEEDED(en->GetDevice(Widen(id).c_str(), &dev));
}
static std::string DeviceName(IMMDevice* d) {
  ComPtr<IPropertyStore> ps;
  if (FAILED(d->OpenPropertyStore(STGM_READ, &ps))) return "?";
  PROPVARIANT v; PropVariantInit(&v);
  std::string n = "?";
  if (SUCCEEDED(ps->GetValue(kKeyFriendlyName, &v)) && v.vt == VT_LPWSTR) n = Narrow(v.pwszVal);
  PropVariantClear(&v);
  return n;
}
static void ListDevices() {
  ComPtr<IMMDeviceEnumerator> en;
  if (FAILED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&en)))) { Emit("ERR enumerateur"); return; }
  Emit("DEVBEGIN");
  for (int pass = 0; pass < 2; pass++) {
    const EDataFlow flow = pass == 0 ? eCapture : eRender;
    std::string defId;
    ComPtr<IMMDevice> def;
    if (SUCCEEDED(en->GetDefaultAudioEndpoint(flow, eConsole, &def))) { LPWSTR w = nullptr; if (SUCCEEDED(def->GetId(&w))) { defId = Narrow(w); CoTaskMemFree(w); } }
    ComPtr<IMMDeviceCollection> col;
    if (FAILED(en->EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, &col))) continue;
    UINT n = 0; col->GetCount(&n);
    for (UINT i = 0; i < n; i++) {
      ComPtr<IMMDevice> d;
      if (FAILED(col->Item(i, &d))) continue;
      LPWSTR w = nullptr;
      if (FAILED(d->GetId(&w))) continue;
      const std::string id = Narrow(w);
      CoTaskMemFree(w);
      Emit(std::string("DEV\t") + (pass == 0 ? "i" : "o") + "\t" + (id == defId ? "1" : "0") + "\t" + id + "\t" + DeviceName(d.Get()));
    }
  }
  Emit("DEVEND");
}

static WAVEFORMATEXTENSIBLE MakeFmt() {
  WAVEFORMATEXTENSIBLE f{};
  f.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
  f.Format.nChannels = 2;
  f.Format.nSamplesPerSec = kRate;
  f.Format.wBitsPerSample = 32;
  f.Format.nBlockAlign = 8;
  f.Format.nAvgBytesPerSec = kRate * 8;
  f.Format.cbSize = 22;
  f.Samples.wValidBitsPerSample = 32;
  f.dwChannelMask = 3;   // avant gauche + avant droit
  f.SubFormat = kSubtypeFloat;
  return f;
}

// ------------------------------------------------------------------ capture (micro)
static HRESULT RunCapture(const std::string& id, int gen) {
  ComPtr<IMMDeviceEnumerator> en;
  HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&en));
  if (FAILED(hr)) return hr;
  ComPtr<IMMDevice> dev;
  if (!OpenDevice(en.Get(), eCapture, id, dev)) return E_FAIL;
  ComPtr<IAudioClient> ac;
  hr = dev->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void**)&ac);
  if (FAILED(hr)) return hr;
  WAVEFORMATEXTENSIBLE wf = MakeFmt();
  REFERENCE_TIME defP = 0, minP = 0;
  ac->GetDevicePeriod(&defP, &minP);
  hr = ac->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                      defP, 0, (WAVEFORMATEX*)&wf, nullptr);
  if (FAILED(hr)) return hr;
  HANDLE evt = CreateEventW(nullptr, FALSE, FALSE, nullptr);
  ac->SetEventHandle(evt);
  ComPtr<IAudioCaptureClient> cap;
  hr = ac->GetService(__uuidof(IAudioCaptureClient), (void**)&cap);
  if (FAILED(hr)) { CloseHandle(evt); return hr; }
  hr = ac->Start();
  if (FAILED(hr)) { CloseHandle(evt); return hr; }
  Emit("IN ok");
  std::vector<float> mono;
  while (!g_quit && g_inGen == gen) {
    const DWORD w = WaitForSingleObject(evt, 200);
    if (w != WAIT_OBJECT_0) continue;
    UINT32 pk = 0;
    while (SUCCEEDED(cap->GetNextPacketSize(&pk)) && pk > 0) {
      BYTE* data = nullptr; UINT32 n = 0; DWORD fl = 0;
      hr = cap->GetBuffer(&data, &n, &fl, nullptr, nullptr);
      if (FAILED(hr)) break;
      mono.assign(n, 0.f);
      if (!(fl & AUDCLNT_BUFFERFLAGS_SILENT)) {
        const float* f = (const float*)data;
        for (UINT32 i = 0; i < n; i++) mono[i] = 0.5f * (f[2 * i] + f[2 * i + 1]);
      }
      g_mix.PushCapture(mono.data(), (int)n);
      cap->ReleaseBuffer(n);
    }
    if (hr == AUDCLNT_E_DEVICE_INVALIDATED) break;
  }
  ac->Stop();
  CloseHandle(evt);
  return hr == AUDCLNT_E_DEVICE_INVALIDATED ? hr : S_OK;
}

static void CaptureThread() {
  CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  DWORD task = 0;
  HANDLE mm = AvSetMmThreadCharacteristicsW(L"Pro Audio", &task);
  while (!g_quit) {
    const int gen = g_inGen;
    const std::string id = GetCfg("in");
    if (id == "off") { WaitForSingleObject(g_inEvt, 500); continue; }
    const HRESULT hr = RunCapture(id, gen);
    if (g_quit) break;
    if (g_inGen != gen) continue;   // l'utilisateur a change de micro
    Emit("IN err " + Hex(hr));
    WaitForSingleObject(g_inEvt, 2000);   // peripherique absent / debranche : on reessaie
  }
  if (mm) AvRevertMmThreadCharacteristics(mm);
  CoUninitialize();
}

// ------------------------------------------------------------------ sortie (enceintes externes)
static HRESULT RunRender(const std::string& id, int gen) {
  ComPtr<IMMDeviceEnumerator> en;
  HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&en));
  if (FAILED(hr)) return hr;
  ComPtr<IMMDevice> dev;
  if (!OpenDevice(en.Get(), eRender, id, dev)) return E_FAIL;
  ComPtr<IAudioClient> ac;
  hr = dev->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void**)&ac);
  if (FAILED(hr)) return hr;
  WAVEFORMATEXTENSIBLE wf = MakeFmt();
  REFERENCE_TIME defP = 0, minP = 0;
  ac->GetDevicePeriod(&defP, &minP);
  hr = ac->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                      defP * 2, 0, (WAVEFORMATEX*)&wf, nullptr);
  if (FAILED(hr)) return hr;
  UINT32 bufFrames = 0;
  ac->GetBufferSize(&bufFrames);
  HANDLE evt = CreateEventW(nullptr, FALSE, FALSE, nullptr);
  ac->SetEventHandle(evt);
  ComPtr<IAudioRenderClient> rc;
  hr = ac->GetService(__uuidof(IAudioRenderClient), (void**)&rc);
  if (FAILED(hr)) { CloseHandle(evt); return hr; }
  BYTE* p = nullptr;
  if (SUCCEEDED(rc->GetBuffer(bufFrames, &p))) rc->ReleaseBuffer(bufFrames, AUDCLNT_BUFFERFLAGS_SILENT);   // amorcage en silence
  hr = ac->Start();
  if (FAILED(hr)) { CloseHandle(evt); return hr; }
  Emit("OUT ok");
  while (!g_quit && g_outGen == gen) {
    const DWORD w = WaitForSingleObject(evt, 200);
    if (w != WAIT_OBJECT_0) continue;
    UINT32 pad = 0;
    hr = ac->GetCurrentPadding(&pad);
    if (FAILED(hr)) break;
    const UINT32 n = bufFrames - pad;
    if (n == 0) continue;
    hr = rc->GetBuffer(n, &p);
    if (FAILED(hr)) break;
    g_mix.Render((float*)p, (int)n);
    rc->ReleaseBuffer(n, 0);
  }
  ac->Stop();
  CloseHandle(evt);
  return hr == AUDCLNT_E_DEVICE_INVALIDATED ? hr : S_OK;
}

static void RenderThread() {
  CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  DWORD task = 0;
  HANDLE mm = AvSetMmThreadCharacteristicsW(L"Pro Audio", &task);
  while (!g_quit) {
    const int gen = g_outGen;
    const std::string id = GetCfg("out");
    if (id == "off") { WaitForSingleObject(g_outEvt, 500); continue; }
    const HRESULT hr = RunRender(id, gen);
    if (g_quit) break;
    if (g_outGen != gen) continue;
    Emit("OUT err " + Hex(hr));
    WaitForSingleObject(g_outEvt, 2000);
  }
  if (mm) AvRevertMmThreadCharacteristics(mm);
  CoUninitialize();
}

// ------------------------------------------------------------------ decodage de fichiers (Media Foundation) -> stereo 48 kHz flottant
class FileDecoder {
 public:
  bool Open(const std::wstring& path) {
    HRESULT hr = MFCreateSourceReaderFromURL(path.c_str(), nullptr, &rd_);
    if (FAILED(hr)) return false;
    rd_->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
    rd_->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, TRUE);
    ComPtr<IMFMediaType> t;
    MFCreateMediaType(&t);
    t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    t->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_Float);
    hr = rd_->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, nullptr, t.Get());
    if (FAILED(hr)) return false;
    ComPtr<IMFMediaType> cur;
    if (FAILED(rd_->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, &cur))) return false;
    UINT32 c = 2, r = 48000;
    cur->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &c);
    cur->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &r);
    ch_ = (int)c; rate_ = (int)r;
    if (ch_ < 1 || rate_ < 8000) return false;
    PROPVARIANT pv; PropVariantInit(&pv);
    if (SUCCEEDED(rd_->GetPresentationAttribute((DWORD)MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &pv)) && pv.vt == VT_UI8) dur_ = (double)pv.uhVal.QuadPart / 1e7;
    PropVariantClear(&pv);
    return true;
  }
  double Duration() const { return dur_; }
  size_t Read(float* out, size_t frames) {
    const double step = (double)rate_ / (double)kRate;
    size_t o = 0;
    while (o < frames) {
      while (nat_.size() / 2 < natR_ + 2 && !eof_) FillNative();
      const size_t avail = nat_.size() / 2;
      if (natR_ >= avail) break;
      const size_t i1 = natR_ + 1 < avail ? natR_ + 1 : natR_;
      const float f = (float)frac_;
      out[2 * o] = nat_[2 * natR_] * (1 - f) + nat_[2 * i1] * f;
      out[2 * o + 1] = nat_[2 * natR_ + 1] * (1 - f) + nat_[2 * i1 + 1] * f;
      o++;
      frac_ += step;
      const size_t adv = (size_t)frac_;
      frac_ -= (double)adv;
      natR_ += adv;
      if (natR_ > 16384) {
        const size_t cut = std::min(natR_, nat_.size() / 2);
        nat_.erase(nat_.begin(), nat_.begin() + (std::ptrdiff_t)(cut * 2));
        natR_ -= cut;
      }
    }
    return o;
  }
  bool Seek(double sec) {
    PROPVARIANT pv; PropVariantInit(&pv);
    pv.vt = VT_I8;
    pv.hVal.QuadPart = (LONGLONG)(std::max(0.0, sec) * 1e7);
    const HRESULT hr = rd_->SetCurrentPosition(GUID_NULL, pv);
    PropVariantClear(&pv);
    nat_.clear(); natR_ = 0; frac_ = 0; eof_ = false;
    return SUCCEEDED(hr);
  }
 private:
  void FillNative() {
    DWORD actual = 0, flags = 0; LONGLONG ts = 0;
    ComPtr<IMFSample> s;
    const HRESULT hr = rd_->ReadSample((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, &actual, &flags, &ts, &s);
    if (FAILED(hr) || (flags & MF_SOURCE_READERF_ENDOFSTREAM) || (flags & MF_SOURCE_READERF_ERROR)) { eof_ = true; if (!s) return; }
    if (!s) return;
    ComPtr<IMFMediaBuffer> b;
    if (FAILED(s->ConvertToContiguousBuffer(&b))) return;
    BYTE* p = nullptr; DWORD mx = 0, cur = 0;
    if (FAILED(b->Lock(&p, &mx, &cur))) return;
    const size_t n = cur / (sizeof(float) * (size_t)ch_);
    const float* f = (const float*)p;
    const size_t base = nat_.size();
    nat_.resize(base + n * 2);
    for (size_t i = 0; i < n; i++) {
      const float l = f[i * (size_t)ch_], r = ch_ > 1 ? f[i * (size_t)ch_ + 1] : l;
      nat_[base + 2 * i] = l; nat_[base + 2 * i + 1] = r;
    }
    b->Unlock();
  }
  ComPtr<IMFSourceReader> rd_;
  int ch_ = 2, rate_ = 48000;
  double dur_ = 0, frac_ = 0;
  std::vector<float> nat_;
  size_t natR_ = 0;
  bool eof_ = false;
};

// ------------------------------------------------------------------ chargement des sons du soundboard (un seul thread, a la file)
struct LoadJob { int slot; std::string path; };
static std::mutex g_loadMx;
static std::condition_variable g_loadCv;
static std::deque<LoadJob> g_loadQ;

static void LoaderThread() {
  CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  while (!g_quit) {
    LoadJob job;
    {
      std::unique_lock<std::mutex> l(g_loadMx);
      g_loadCv.wait_for(l, std::chrono::milliseconds(300), [] { return !g_loadQ.empty() || g_quit; });
      if (g_loadQ.empty()) continue;
      job = g_loadQ.front(); g_loadQ.pop_front();
    }
    FileDecoder d;
    auto snd = std::make_shared<Sound>();
    bool ok = d.Open(Widen(job.path));
    if (ok) {
      const size_t cap = (size_t)kRate * 120;   // 2 minutes maximum par son (pour les longs morceaux : lecteur de musique)
      std::vector<float> chunk(8192 * 2);
      while (snd->frames() < cap) {
        const size_t n = d.Read(chunk.data(), 8192);
        if (n == 0) break;
        snd->data.insert(snd->data.end(), chunk.begin(), chunk.begin() + (std::ptrdiff_t)(n * 2));
      }
      ok = snd->frames() > 1;
    }
    if (ok) {
      const double sec = snd->seconds();
      g_mix.SetSound(job.slot, snd);
      Emit("SND " + std::to_string(job.slot) + " ok " + std::to_string((int)(sec * 1000)));
    } else Emit("SND " + std::to_string(job.slot) + " err");
  }
  CoUninitialize();
}

// ------------------------------------------------------------------ lecteur de musique (flux)
static std::mutex g_musMx;
static std::string g_musPath;
static std::atomic<int> g_musGen{0}, g_musSeekGen{0};
static std::atomic<double> g_musSeek{0};

static void MusicThread() {
  CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  FileDecoder dec;
  bool open = false;
  int gen = 0, sgen = 0;
  std::vector<float> chunk(4096 * 2);
  while (!g_quit) {
    if (gen != g_musGen) {
      gen = g_musGen;
      std::string path; { std::lock_guard<std::mutex> l(g_musMx); path = g_musPath; }
      g_mix.musicActive = false;
      g_mix.MusicReset(0);
      open = false;
      if (!path.empty()) {
        dec = FileDecoder();
        open = dec.Open(Widen(path));
        if (open) { g_mix.musicDur = dec.Duration(); g_mix.musicActive = true; Emit("MUSIC ok " + std::to_string((int)(dec.Duration() * 1000))); }
        else Emit("MUSIC err");
      }
      sgen = g_musSeekGen;
    }
    if (open && sgen != g_musSeekGen) {
      sgen = g_musSeekGen;
      const double t = g_musSeek;
      dec.Seek(t);
      g_mix.MusicReset(t);
    }
    if (!open || !g_mix.musicActive) { Sleep(30); continue; }
    if (g_mix.musicRing.Free() < 4096) { Sleep(15); continue; }
    const size_t n = dec.Read(chunk.data(), 4096);
    if (n > 0) { g_mix.musicRing.Write(chunk.data(), n); continue; }
    if (g_mix.musicLoop) { dec.Seek(0); continue; }   // la position est ramenee au debut par le modulo de MusicPosition
    // fin de la piste : on laisse le tampon se vider, puis on previent Unity
    while (!g_quit && g_musGen == gen && g_mix.musicRing.Size() > 0) Sleep(30);
    if (g_musGen == gen) { g_mix.musicActive = false; open = false; Emit("MUSIC end"); }
  }
  CoUninitialize();
}

// ------------------------------------------------------------------ niveaux envoyes a Unity (seulement si Unity les demande)
#pragma pack(push, 1)
struct MeterPacket {
  unsigned magic = 0x31445541;   // 'AUD1'
  unsigned short ver = 1, flags = 0;
  unsigned seq = 0;
  float peak[6];                 // micro, voix, soundboard, musique, gauche, droite
  float musicPos, musicDur;
  unsigned voices, reserved;
  float specIn[kBands], specOut[kBands];
};
#pragma pack(pop)

static std::atomic<bool> g_inRunning{false}, g_outRunning{false};

static void MeterThread() {
  unsigned seq = 0;
  float a[1024];
  while (!g_quit) {
    Sleep(50);
    if (NowMs() > g_meterUntil) continue;
    MeterPacket m;
    m.seq = seq++;
    m.peak[0] = g_mix.peakIn; m.peak[1] = g_mix.peakVoice; m.peak[2] = g_mix.peakSfx; m.peak[3] = g_mix.peakMusic;
    m.peak[4] = g_mix.peakL; m.peak[5] = g_mix.peakR;
    m.musicPos = (float)g_mix.MusicPosition(); m.musicDur = (float)g_mix.musicDur.load();
    m.voices = (unsigned)g_mix.activeVoices.load(); m.reserved = 0;
    m.flags = (unsigned short)((g_mix.gateOpen ? 4 : 0) | (g_mix.musicActive ? 8 : 0) | (g_mix.musicPaused ? 16 : 0));
    g_mix.inScope.Snapshot(a);  Spectrum32(a, m.specIn);
    g_mix.outScope.Snapshot(a); Spectrum32(a, m.specOut);
    SendTo(kPortMeter, &m, (int)sizeof(m));
  }
}

// ------------------------------------------------------------------ commandes
static void SetDevice(const std::string& which, const std::string& id) {
  {
    std::lock_guard<std::mutex> l(g_cfgMx);
    (which == "in" ? g_inId : g_outId) = id;
  }
  if (which == "in") { g_inGen++; SetEvent(g_inEvt); } else { g_outGen++; SetEvent(g_outEvt); }
}

static void HandleLine(const std::string& line) {
  const auto w = SplitWords(line);
  if (w.empty()) return;
  if (g_mix.Handle(line)) return;
  const std::string c = Upper(w[0]);
  if (c == "IN" && w.size() >= 2) SetDevice("in", w[1]);
  else if (c == "OUT" && w.size() >= 2) SetDevice("out", w[1]);
  else if (c == "DEVLIST") ListDevices();
  else if (c == "METER") g_meterUntil = NowMs() + 3000;
  else if (c == "PING") Emit("PONG");
  else if (c == "QUIT") g_quit = true;
  else if (c == "LOAD") {
    const auto p = SplitWords(line, 3);
    if (p.size() >= 3) { std::lock_guard<std::mutex> l(g_loadMx); g_loadQ.push_back({(int)ToF(p[1], -1), p[2]}); g_loadCv.notify_one(); }
  } else if (c == "MUSIC") {
    const auto p = SplitWords(line, 2);
    { std::lock_guard<std::mutex> l(g_musMx); g_musPath = p.size() >= 2 ? p[1] : std::string(); }
    g_mix.musicPaused = false;
    g_musGen++;
  } else if (c == "MSTOP") { { std::lock_guard<std::mutex> l(g_musMx); g_musPath.clear(); } g_musGen++; }
  else if (c == "MPAUSE" && w.size() >= 2) g_mix.musicPaused = ToF(w[1]) > 0.5f;
  else if (c == "MLOOP" && w.size() >= 2) g_mix.musicLoop = ToF(w[1]) > 0.5f;
  else if (c == "MSEEK" && w.size() >= 2) { g_musSeek = ToF(w[1]); g_musSeekGen++; }
}

int main(int argc, char** argv) {
  DWORD parentPid = 0;
  for (int i = 1; i + 1 < argc; i++) if (std::string(argv[i]) == "--parent") parentPid = (DWORD)atoi(argv[i + 1]);

  WSADATA wsa;
  WSAStartup(MAKEWORD(2, 2), &wsa);
  CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  MFStartup(MF_VERSION, MFSTARTUP_LITE);
  g_tx = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);

  SOCKET rx = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
  sockaddr_in a{};
  a.sin_family = AF_INET; a.sin_port = htons((u_short)kPortCmd);
  inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
  if (bind(rx, (sockaddr*)&a, sizeof(a)) != 0) { std::printf("ar6dof_audio : port %d deja utilise (une autre instance tourne deja)\n", kPortCmd); return 2; }
  DWORD to = 250;
  setsockopt(rx, SOL_SOCKET, SO_RCVTIMEO, (const char*)&to, sizeof(to));

  g_inEvt = CreateEventW(nullptr, FALSE, FALSE, nullptr);
  g_outEvt = CreateEventW(nullptr, FALSE, FALSE, nullptr);

  std::thread tIn(CaptureThread), tOut(RenderThread), tLoad(LoaderThread), tMus(MusicThread), tMeter(MeterThread);
  std::thread tParent;
  if (parentPid) {
    tParent = std::thread([parentPid] {
      HANDLE h = OpenProcess(SYNCHRONIZE, FALSE, parentPid);
      if (!h) { g_quit = true; return; }
      while (!g_quit) { if (WaitForSingleObject(h, 500) == WAIT_OBJECT_0) { g_quit = true; break; } }
      CloseHandle(h);
    });
  }
  Emit("READY");
  std::printf("ar6dof_audio : pret (commandes UDP %d)\n", kPortCmd);

  std::vector<char> buf(8192);
  while (!g_quit) {
    const int n = recvfrom(rx, buf.data(), (int)buf.size() - 1, 0, nullptr, nullptr);
    if (n <= 0) continue;
    std::string s(buf.data(), (size_t)n), line;
    size_t i = 0;
    while (i <= s.size()) {
      const size_t e = s.find('\n', i);
      line = s.substr(i, e == std::string::npos ? std::string::npos : e - i);
      if (!line.empty()) HandleLine(line);
      if (e == std::string::npos) break;
      i = e + 1;
    }
  }
  g_quit = true;
  g_loadCv.notify_all();
  SetEvent(g_inEvt); SetEvent(g_outEvt);
  tIn.join(); tOut.join(); tLoad.join(); tMus.join(); tMeter.join();
  if (tParent.joinable()) tParent.join();
  MFShutdown();
  closesocket(rx); closesocket(g_tx);
  WSACleanup();
  return 0;
}
