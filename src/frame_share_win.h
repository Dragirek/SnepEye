#pragma once
// Enveloppe Windows de la mémoire partagée (voir frame_share_core.h).
#include <windows.h>

#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>

#include "frame_share_core.h"

class FrameShare {
 public:
  ~FrameShare() { Close(); }

  // Crée la mémoire pour des images du même format que `sample` (BGR 8 bits). Elles sont publiées en BGRA.
  bool Open(const char* name, const cv::Mat& sample) {
    if (sample.empty() || sample.type() != CV_8UC3) return false;
    w_ = sample.cols;
    h_ = sample.rows;
    size_t total = fshare::TotalBytes(w_, h_, 4);
    map_ = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, (DWORD)((unsigned long long)total >> 32),
                              (DWORD)(total & 0xFFFFFFFFu), name);
    if (!map_) return false;
    base_ = static_cast<uint8_t*>(MapViewOfFile(map_, FILE_MAP_ALL_ACCESS, 0, 0, 0));
    if (!base_) { Close(); return false; }
    fshare::Init(base_, w_, h_, 4);
    QueryPerformanceFrequency(&freq_);
    return true;
  }
  bool IsOpen() const { return base_ != nullptr; }
  bool Matches(const cv::Mat& f) const { return f.type() == CV_8UC3 && f.cols == w_ && f.rows == h_; }

  // La conversion BGR -> BGRA écrit directement dans la mémoire partagée (une seule passe sur les pixels).
  void Write(const cv::Mat& bgr) {
    uint64_t id = ++id_;
    uint8_t* dst = fshare::BeginWrite(base_, id);
    cv::Mat out(h_, w_, CV_8UC4, dst);
    cv::cvtColor(bgr, out, cv::COLOR_BGR2BGRA);
    LARGE_INTEGER t;
    QueryPerformanceCounter(&t);  // horloge système commune aux deux programmes
    fshare::EndWrite(base_, id, (double)t.QuadPart / (double)freq_.QuadPart);
  }

  void Close() {
    if (base_) UnmapViewOfFile(base_);
    if (map_) CloseHandle(map_);
    base_ = nullptr;
    map_ = nullptr;
  }

 private:
  HANDLE map_ = nullptr;
  uint8_t* base_ = nullptr;
  int w_ = 0, h_ = 0;
  uint64_t id_ = 0;
  LARGE_INTEGER freq_{};
};
