// ar6dof_screen.dll : capture d'écran 100 % GPU pour Unity (remplace uWindowCapture).
//
//  - Un thread par écran virtuel utilise DXGI Desktop Duplication : il n'est réveillé que quand l'écran change
//    (zéro CPU sur un bureau immobile) et copie l'image dans une texture partagée, GPU -> GPU, sans jamais la lire côté CPU.
//  - Côté Unity, un évènement de rendu (GL.IssuePluginEvent) copie cette texture partagée dans la RenderTexture de l'écran,
//    sur le thread de rendu d'Unity. Aucun en-tête Unity n'est nécessaire : le périphérique D3D11 d'Unity est retrouvé à
//    partir du pointeur de sa texture (GetNativeTexturePtr).
//  - Limites : même carte graphique pour Unity et l'écran capturé ; bureau en SDR (B8G8R8A8) ; le curseur n'est pas dessiné.
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;
#define A6S_API extern "C" __declspec(dllexport)

namespace {

constexpr int kSlots = 4;

struct MonInfo {
  UINT adapter, output;
  RECT rc;
};

std::vector<MonInfo> EnumMonitors() {
  std::vector<MonInfo> v;
  ComPtr<IDXGIFactory1> f;
  if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&f)))) return v;
  ComPtr<IDXGIAdapter1> ad;
  for (UINT a = 0; f->EnumAdapters1(a, &ad) != DXGI_ERROR_NOT_FOUND; a++) {
    ComPtr<IDXGIOutput> out;
    for (UINT o = 0; ad->EnumOutputs(o, &out) != DXGI_ERROR_NOT_FOUND; o++) {
      DXGI_OUTPUT_DESC d;
      if (SUCCEEDED(out->GetDesc(&d)) && d.AttachedToDesktop) v.push_back({a, o, d.DesktopCoordinates});
      out.Reset();
    }
    ad.Reset();
  }
  return v;
}

struct Slot {
  std::thread th;
  std::atomic<bool> run{false};
  std::atomic<int> monitor{-1}, maxFps{60}, w{0}, h{0};
  std::atomic<int> status{0};   // 0 arrêté, 1 démarrage, 2 capture en cours, 3 écran inaccessible, 4 format non géré (HDR ?)
  std::atomic<bool> active{true};
  std::atomic<uint64_t> frames{0};
  std::atomic<HANDLE> shared{nullptr};
  std::atomic<int> gen{0};
  // thread de rendu d'Unity uniquement :
  void* unityTex = nullptr;
  ComPtr<ID3D11Texture2D> opened;
  ComPtr<IDXGIKeyedMutex> keyed;
  int openedGen = -1;
  ID3D11Device* openedDev = nullptr;
  uint64_t lastCopied = 0;
};
Slot g_slots[kSlots];

void SleepMs(int ms) { std::this_thread::sleep_for(std::chrono::milliseconds(ms)); }

// Une session de capture : renvoie quand il faut recréer la duplication (écran changé, accès perdu...) ou à l'arrêt.
void CaptureSession(Slot& s) {
  auto mons = EnumMonitors();
  int mi = s.monitor;
  if (mi < 0 || mi >= (int)mons.size()) { s.status = 3; SleepMs(500); return; }
  ComPtr<IDXGIFactory1> f;
  ComPtr<IDXGIAdapter1> ad;
  if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&f))) || FAILED(f->EnumAdapters1(mons[mi].adapter, &ad))) { s.status = 3; SleepMs(500); return; }
  ComPtr<IDXGIOutput> out;
  ComPtr<IDXGIOutput1> out1;
  if (FAILED(ad->EnumOutputs(mons[mi].output, &out)) || FAILED(out.As(&out1))) { s.status = 3; SleepMs(500); return; }

  ComPtr<ID3D11Device> dev;
  ComPtr<ID3D11DeviceContext> ctx;
  if (FAILED(D3D11CreateDevice(ad.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &dev, nullptr, &ctx))) {
    s.status = 3; SleepMs(500); return;
  }
  ComPtr<IDXGIOutputDuplication> dup;
  if (FAILED(out1->DuplicateOutput(dev.Get(), &dup))) { s.status = 3; SleepMs(700); return; }
  DXGI_OUTDUPL_DESC dd;
  dup->GetDesc(&dd);
  if (dd.ModeDesc.Format != DXGI_FORMAT_B8G8R8A8_UNORM) { s.status = 4; SleepMs(1000); return; }
  const UINT W = dd.ModeDesc.Width, H = dd.ModeDesc.Height;

  // Texture partagée avec Unity (protégée par un keyed mutex)
  D3D11_TEXTURE2D_DESC td = {};
  td.Width = W; td.Height = H; td.MipLevels = 1; td.ArraySize = 1;
  td.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
  td.SampleDesc.Count = 1;
  td.Usage = D3D11_USAGE_DEFAULT;
  td.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
  td.MiscFlags = D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
  ComPtr<ID3D11Texture2D> shared;
  if (FAILED(dev->CreateTexture2D(&td, nullptr, &shared))) { s.status = 3; SleepMs(500); return; }
  ComPtr<IDXGIKeyedMutex> km;
  ComPtr<IDXGIResource> res;
  HANDLE hs = nullptr;
  if (FAILED(shared.As(&km)) || FAILED(shared.As(&res)) || FAILED(res->GetSharedHandle(&hs)) || !hs) { s.status = 3; SleepMs(500); return; }

  s.w = (int)W; s.h = (int)H;
  s.shared = hs;
  s.gen++;
  s.status = 2;

  auto next = std::chrono::steady_clock::now();
  while (s.run && s.monitor == mi) {
    // Cadence : on attend AVANT de demander l'image, pour toujours capturer l'état le plus récent
    int fps = std::max(5, std::min(240, s.maxFps.load()));
    next += std::chrono::microseconds(1000000 / fps);
    auto now = std::chrono::steady_clock::now();
    if (next > now) std::this_thread::sleep_until(next); else next = now;
    if (!s.active) { SleepMs(30); continue; }

    DXGI_OUTDUPL_FRAME_INFO fi;
    ComPtr<IDXGIResource> frame;
    HRESULT hr = dup->AcquireNextFrame(100, &fi, &frame);
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) continue;   // bureau immobile : rien à faire
    if (FAILED(hr)) break;                         // accès perdu (changement de résolution, verrouillage...) : on recrée tout
    if (fi.LastPresentTime.QuadPart != 0) {        // 0 = seul le curseur a bougé
      ComPtr<ID3D11Texture2D> tex;
      if (SUCCEEDED(frame.As(&tex)) && km->AcquireSync(0, 8) == S_OK) {
        ctx->CopyResource(shared.Get(), tex.Get());
        km->ReleaseSync(0);
        ctx->Flush();
        s.frames++;
      }
    }
    dup->ReleaseFrame();
  }
  s.shared = nullptr;
}

void CaptureThread(int si) {
  Slot& s = g_slots[si];
  while (s.run) {
    s.status = 1;
    CaptureSession(s);
  }
  s.status = 0;
}

void StopSlot(Slot& s) {
  s.run = false;
  if (s.th.joinable()) s.th.join();
  s.w = 0; s.h = 0; s.frames = 0;
}

// Évènement de rendu : appelé par Unity sur son thread de rendu (GL.IssuePluginEvent(func, slot)).
void __stdcall OnRender(int si) {
  if (si < 0 || si >= kSlots) return;
  Slot& s = g_slots[si];
  HANDLE h = s.shared.load();
  if (!s.unityTex || !h) return;
  ID3D11Texture2D* dst = reinterpret_cast<ID3D11Texture2D*>(s.unityTex);
  ComPtr<ID3D11Device> dev;
  dst->GetDevice(&dev);
  if (!dev) return;
  ComPtr<ID3D11DeviceContext> ctx;
  dev->GetImmediateContext(&ctx);
  int g = s.gen.load();
  if (s.openedGen != g || s.openedDev != dev.Get()) {
    s.opened.Reset();
    s.keyed.Reset();
    if (FAILED(dev->OpenSharedResource(h, IID_PPV_ARGS(&s.opened)))) return;
    if (FAILED(s.opened.As(&s.keyed))) { s.opened.Reset(); return; }
    s.openedGen = g;
    s.openedDev = dev.Get();
    s.lastCopied = 0;
  }
  uint64_t f = s.frames.load();
  if (f == s.lastCopied) return;                    // rien de nouveau : aucun travail
  if (s.keyed->AcquireSync(0, 0) != S_OK) return;   // le thread de capture écrit : on réessaiera à la prochaine image
  D3D11_TEXTURE2D_DESC sd, dd;
  s.opened->GetDesc(&sd);
  dst->GetDesc(&dd);
  D3D11_BOX box = {0, 0, 0, std::min(sd.Width, dd.Width), std::min(sd.Height, dd.Height), 1};
  ctx->CopySubresourceRegion(dst, 0, 0, 0, 0, s.opened.Get(), 0, &box);
  s.keyed->ReleaseSync(0);
  s.lastCopied = f;
}

}  // namespace

A6S_API int A6S_MonitorCount() { return (int)EnumMonitors().size(); }

// rect = {gauche, haut, droite, bas} en pixels du bureau virtuel Windows. Renvoie 0 si l'indice est invalide.
A6S_API int A6S_MonitorRect(int i, int* rect4) {
  auto m = EnumMonitors();
  if (i < 0 || i >= (int)m.size() || !rect4) return 0;
  rect4[0] = m[i].rc.left; rect4[1] = m[i].rc.top; rect4[2] = m[i].rc.right; rect4[3] = m[i].rc.bottom;
  return 1;
}

A6S_API void A6S_Start(int slot, int monitor, int maxFps) {
  if (slot < 0 || slot >= kSlots) return;
  Slot& s = g_slots[slot];
  StopSlot(s);
  s.monitor = monitor;
  s.maxFps = maxFps;
  s.active = true;
  s.run = true;
  s.th = std::thread(CaptureThread, slot);
}

A6S_API void A6S_Stop(int slot) { if (slot >= 0 && slot < kSlots) StopSlot(g_slots[slot]); }
A6S_API void A6S_StopAll() { for (int i = 0; i < kSlots; i++) StopSlot(g_slots[i]); }
A6S_API void A6S_SetActive(int slot, int active) { if (slot >= 0 && slot < kSlots) g_slots[slot].active = active != 0; }
A6S_API void A6S_SetFps(int slot, int fps) { if (slot >= 0 && slot < kSlots) g_slots[slot].maxFps = fps; }
A6S_API void A6S_SetTarget(int slot, void* unityTexturePtr) { if (slot >= 0 && slot < kSlots) g_slots[slot].unityTex = unityTexturePtr; }
A6S_API int A6S_GetWidth(int slot) { return (slot >= 0 && slot < kSlots) ? g_slots[slot].w.load() : 0; }
A6S_API int A6S_GetHeight(int slot) { return (slot >= 0 && slot < kSlots) ? g_slots[slot].h.load() : 0; }
A6S_API int A6S_GetStatus(int slot) { return (slot >= 0 && slot < kSlots) ? g_slots[slot].status.load() : 0; }
A6S_API long long A6S_GetFrames(int slot) { return (slot >= 0 && slot < kSlots) ? (long long)g_slots[slot].frames.load() : 0; }
A6S_API void* A6S_GetRenderEventFunc() { return reinterpret_cast<void*>(&OnRender); }
