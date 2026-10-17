#pragma once
// Enveloppe Windows de la mémoire partagée (voir frame_share_core.h).
#include <windows.h>

#include <opencv2/core.hpp>

#include "frame_share_core.h"

class FrameShare {
 public:
  ~FrameShare() { Close(); }

  // Crée la mémoire pour des images du même format que `sample` (BGR 8 bits, largeur multiple de 4).
  bool Open(const char* name, const cv::Mat& sample) {
    if (sample.empty() || sample.type() != CV_8UC3 || sample.cols % 4 != 0) return false;
    w_ = sample.cols;
    h_ = sample.rows;
    size_t total = fshare::TotalBytes(w_, h_, 3);
    map_ = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, (DWORD)((unsigned long long)total >> 32),
                              (DWORD)(total & 0xFFFFFFFFu), name);
    if (!map_) return false;
    base_ = static_cast<uint8_t*>(MapViewOfFile(map_, FILE_MAP_ALL_ACCESS, 0, 0, 0));
    if (!base_) { Close(); return false; }
    fshare::Init(base_, w_, h_, 3);
    QueryPerformanceFrequency(&freq_);
    return true;
  }
  bool IsOpen() const { return base_ != nullptr; }
  bool Matches(const cv::Mat& f) const { return f.type() == CV_8UC3 && f.cols == w_ && f.rows == h_ && f.isContinuous(); }

  void Write(const cv::Mat& f) {
    LARGE_INTEGER t;
    QueryPerformanceCounter(&t);  // horloge système commune aux deux programmes
    fshare::Write(base_, f.data, ++id_, (double)t.QuadPart / (double)freq_.QuadPart);
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
