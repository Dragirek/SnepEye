// mf_camera.h : lecteur de caméra Media Foundation à faible latence, MJPEG décodé par libjpeg-turbo (via OpenCV).
//
// Pourquoi : le backend MSMF d'OpenCV insère un décodeur MJPEG de Windows (lent sur 3200x1200) et un convertisseur de couleurs, puis
// recopie l'image. Ici on demande à la caméra son type natif MJPEG, on reçoit le JPEG compressé tel quel (aucun décodeur ni convertisseur
// inséré : MF_READWRITE_DISABLE_CONVERTERS), on le décode avec libjpeg-turbo (SIMD) directement en BGR. Mode "faible latence" de Media
// Foundation activé. Si quoi que ce soit échoue (caméra sans MJPEG à cette taille, JPEG non lisible...), main.cpp revient à OpenCV.
#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <objbase.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>

#include <chrono>
#include <cmath>
#include <string>

#include <opencv2/imgcodecs.hpp>

namespace ar6mf {

class Camera {
 public:
  Camera() {}
  ~Camera() { Close(); }
  Camera(const Camera&) = delete;
  Camera& operator=(const Camera&) = delete;

  bool IsOpen() const { return reader_ != nullptr; }

  // index = même numérotation que la liste de caméras d'OpenCV (MSMF). W x H : taille totale demandée, fps : cadence demandée.
  bool Open(int index, int W, int H, int fps, std::string* err) {
    Close();
    auto fail = [&](const std::string& m) { if (err) *err = m; Close(); return false; };
    HRESULT co = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    (void)co;   // S_FALSE / RPC_E_CHANGED_MODE : le fil est déjà initialisé, sans conséquence
    if (FAILED(MFStartup(MF_VERSION))) return fail("MFStartup a echoue");
    started_ = true;

    Microsoft::WRL::ComPtr<IMFAttributes> attr;
    if (FAILED(MFCreateAttributes(&attr, 1))) return fail("MFCreateAttributes");
    attr->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
    IMFActivate** devs = nullptr;
    UINT32 count = 0;
    HRESULT hr = MFEnumDeviceSources(attr.Get(), &devs, &count);
    if (FAILED(hr)) return fail("liste des cameras Media Foundation indisponible");
    if (index < 0 || (UINT32)index >= count) {
      for (UINT32 i = 0; i < count; i++) devs[i]->Release();
      CoTaskMemFree(devs);
      return fail("camera " + std::to_string(index) + " absente de Media Foundation");
    }
    hr = devs[index]->ActivateObject(IID_PPV_ARGS(&source_));
    for (UINT32 i = 0; i < count; i++) devs[i]->Release();
    CoTaskMemFree(devs);
    if (FAILED(hr) || !source_) return fail("ouverture de la camera impossible (deja utilisee ?)");

    Microsoft::WRL::ComPtr<IMFAttributes> ra;
    if (FAILED(MFCreateAttributes(&ra, 4))) return fail("MFCreateAttributes (lecteur)");
    ra->SetUINT32(MF_READWRITE_DISABLE_CONVERTERS, TRUE);   // on veut le JPEG brut : ni décodeur ni convertisseur de Windows
    ra->SetUINT32(MF_LOW_LATENCY, TRUE);
    hr = MFCreateSourceReaderFromMediaSource(source_.Get(), ra.Get(), &reader_);
    if (FAILED(hr) || !reader_) return fail("lecteur Media Foundation impossible");

    // type natif MJPEG de la bonne taille, cadence la plus proche de la demande
    Microsoft::WRL::ComPtr<IMFMediaType> best;
    double bestDiff = 1e9, bestFps = 0;
    for (DWORD i = 0;; i++) {
      Microsoft::WRL::ComPtr<IMFMediaType> mt;
      hr = reader_->GetNativeMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, i, &mt);
      if (FAILED(hr)) break;   // MF_E_NO_MORE_TYPES : fin de la liste
      GUID sub = {};
      if (FAILED(mt->GetGUID(MF_MT_SUBTYPE, &sub)) || !IsEqualGUID(sub, MFVideoFormat_MJPG)) continue;
      UINT32 w = 0, h = 0;
      if (FAILED(MFGetAttributeSize(mt.Get(), MF_MT_FRAME_SIZE, &w, &h)) || (int)w != W || (int)h != H) continue;
      UINT32 n = 0, d = 1;
      MFGetAttributeRatio(mt.Get(), MF_MT_FRAME_RATE, &n, &d);
      double f = d ? (double)n / (double)d : 0.0;
      double diff = std::fabs(f - (double)fps);
      if (diff < bestDiff) { bestDiff = diff; bestFps = f; best = mt; }
    }
    if (!best) return fail("la camera ne propose pas de MJPEG en " + std::to_string(W) + "x" + std::to_string(H));
    reader_->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
    reader_->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE);
    if (FAILED(reader_->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, nullptr, best.Get())))
      return fail("mode MJPEG refuse par la camera");
    fps_ = bestFps;
    return true;
  }

  double ModeFps() const { return fps_; }

  void Close() {
    reader_.Reset();
    if (source_) { source_->Shutdown(); source_.Reset(); }
    if (started_) { MFShutdown(); started_ = false; }
  }

  // Lit et décode l'image suivante. true = lecture correcte (bgr peut rester vide si l'image n'était pas décodable) ; false = flux arrêté / erreur.
  bool Read(cv::Mat& bgr, double* decodeMs = nullptr) {
    bgr.release();
    if (!reader_) return false;
    static thread_local bool comInit = false;
    if (!comInit) { comInit = true; CoInitializeEx(nullptr, COINIT_MULTITHREADED); }
    DWORD streamIndex = 0, flags = 0;
    LONGLONG ts = 0;
    Microsoft::WRL::ComPtr<IMFSample> sample;
    HRESULT hr = reader_->ReadSample((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, &streamIndex, &flags, &ts, &sample);
    if (FAILED(hr)) return false;
    if (flags & (MF_SOURCE_READERF_ERROR | MF_SOURCE_READERF_ENDOFSTREAM)) return false;
    if (!sample) return true;   // trou dans le flux : rien à décoder cette fois
    Microsoft::WRL::ComPtr<IMFMediaBuffer> buf;
    if (FAILED(sample->ConvertToContiguousBuffer(&buf)) || !buf) return true;
    BYTE* p = nullptr;
    DWORD maxLen = 0, curLen = 0;
    if (FAILED(buf->Lock(&p, &maxLen, &curLen)) || !p || curLen < 4) return true;
    auto t0 = std::chrono::steady_clock::now();
    cv::Mat raw(1, (int)curLen, CV_8UC1, p);   // pas de copie : décodé directement depuis le tampon de la caméra
    try { bgr = cv::imdecode(raw, cv::IMREAD_COLOR); } catch (...) { bgr.release(); }
    buf->Unlock();
    if (decodeMs) *decodeMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - t0).count();
    return true;
  }

  // Essai au démarrage : au moins 'need' images décodées sur 'tries' lectures. Sinon on revient à OpenCV.
  bool SelfTest(int tries, int need, std::string* err) {
    int ok = 0;
    cv::Mat f;
    for (int i = 0; i < tries && ok < need; i++) {
      if (!Read(f)) { if (err) *err = "lecture impossible"; return false; }
      if (!f.empty() && f.type() == CV_8UC3) ok++;
    }
    if (ok < need) { if (err) *err = "JPEG de la camera non decodable"; return false; }
    return true;
  }

 private:
  Microsoft::WRL::ComPtr<IMFMediaSource> source_;
  Microsoft::WRL::ComPtr<IMFSourceReader> reader_;
  bool started_ = false;
  double fps_ = 0;
};

}  // namespace ar6mf
