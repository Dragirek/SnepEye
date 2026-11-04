#pragma once
// Aperçu des caméras pour l'application et pour Unity : mémoire partagée "Local\ar6dof_preview" (800x300 BGRA, même protocole
// que l'image du passthrough, voir frame_share_core.h). RIEN n'est calculé ni copié tant que personne n'a demandé l'aperçu :
// le client envoie "PV <source>" (UDP 9877) au moins une fois par seconde ; sans nouvelle demande pendant 3 s, l'aperçu s'arrête seul.
#include <windows.h>

#include <atomic>
#include <chrono>
#include <mutex>
#include <string>

#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>

#include "frame_share_core.h"

namespace ar6pv {

constexpr int kW = 800, kH = 300;
enum Src { kNone = 0, kStereo = 1, kEyes = 2, kMouth = 3, kImu = 4, kTrack = 5 };

inline double Now() {
  using namespace std::chrono;
  return duration<double>(steady_clock::now().time_since_epoch()).count();
}

inline std::atomic<int>& SrcAtom() { static std::atomic<int> a{0}; return a; }
inline std::atomic<double>& UntilAtom() { static std::atomic<double> a{0}; return a; }

// Appelée à la réception de "PV <source>" (0 = arrêter).
inline void Request(int src) {
  SrcAtom() = src;
  UntilAtom() = src ? Now() + 3.0 : 0.0;
}

// Source demandée en ce moment (kNone si personne ne regarde ou si la demande a expiré).
inline int Wanted() { return Now() > UntilAtom().load() ? (int)kNone : SrcAtom().load(); }

class Writer {
 public:
  ~Writer() { Close(); }

  // Image BGR / gris / BGRA quelconque : réduite pour tenir dans 800x300 (bandes sombres si le format est différent).
  void Publish(const cv::Mat& img) {
    if (img.empty() || !Open()) return;
    std::lock_guard<std::mutex> l(m_);
    cv::Mat bgr;
    if (img.channels() == 1) cv::cvtColor(img, bgr, cv::COLOR_GRAY2BGR);
    else if (img.channels() == 4) cv::cvtColor(img, bgr, cv::COLOR_BGRA2BGR);
    else bgr = img;
    const double s = std::min((double)kW / bgr.cols, (double)kH / bgr.rows);
    const int nw = std::max(1, (int)(bgr.cols * s)), nh = std::max(1, (int)(bgr.rows * s));
    cv::Mat small;
    cv::resize(bgr, small, cv::Size(nw, nh), 0, 0, s < 1.0 ? cv::INTER_AREA : cv::INTER_LINEAR);
    canvas_.setTo(cv::Scalar(26, 26, 26));
    small.copyTo(canvas_(cv::Rect((kW - nw) / 2, (kH - nh) / 2, nw, nh)));
    Write();
  }

  // Message à la place de l'image (caméra occupée, désactivée...). ASCII seulement (police OpenCV).
  void PublishText(const std::string& text) {
    if (!Open()) return;
    std::lock_guard<std::mutex> l(m_);
    canvas_.setTo(cv::Scalar(26, 26, 26));
    int base = 0;
    const cv::Size ts = cv::getTextSize(text, cv::FONT_HERSHEY_SIMPLEX, 0.7, 1, &base);
    cv::putText(canvas_, text, cv::Point(std::max(10, (kW - ts.width) / 2), kH / 2), cv::FONT_HERSHEY_SIMPLEX, 0.7, cv::Scalar(170, 170, 170), 1, cv::LINE_AA);
    Write();
  }

  void Close() {
    if (base_) UnmapViewOfFile(base_);
    if (map_) CloseHandle(map_);
    base_ = nullptr;
    map_ = nullptr;
  }

 private:
  bool Open() {
    if (base_) return true;
    const size_t total = fshare::TotalBytes(kW, kH, 4);
    map_ = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, (DWORD)((unsigned long long)total >> 32), (DWORD)(total & 0xFFFFFFFFu),
                              "Local\\ar6dof_preview");
    if (!map_) return false;
    base_ = static_cast<uint8_t*>(MapViewOfFile(map_, FILE_MAP_ALL_ACCESS, 0, 0, 0));
    if (!base_) { Close(); return false; }
    fshare::Init(base_, kW, kH, 4);
    QueryPerformanceFrequency(&freq_);
    canvas_.create(kH, kW, CV_8UC3);
    return true;
  }

  void Write() {
    const uint64_t id = ++id_;
    uint8_t* dst = fshare::BeginWrite(base_, id);
    cv::Mat out(kH, kW, CV_8UC4, dst);
    cv::cvtColor(canvas_, out, cv::COLOR_BGR2BGRA);
    LARGE_INTEGER t;
    QueryPerformanceCounter(&t);
    const double now = (double)t.QuadPart / (double)freq_.QuadPart;
    fshare::EndWrite(base_, id, now, now);
  }

  std::mutex m_;
  HANDLE map_ = nullptr;
  uint8_t* base_ = nullptr;
  uint64_t id_ = 0;
  LARGE_INTEGER freq_{};
  cv::Mat canvas_;
};

inline Writer& Shared() { static Writer w; return w; }

}  // namespace ar6pv
