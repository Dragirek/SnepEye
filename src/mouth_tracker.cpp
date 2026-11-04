// Mouth tracking : une caméra, détection de la cavité sombre de la bouche, étalonnage par expressions, UDP vers Unity.
// Les calculs sont dans mouth_math.h. Rien ne tourne tant que la caméra n'est pas activée (commande MOUTH 1 ou startEnabled).
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <fstream>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

#include <opencv2/imgproc.hpp>
#include <opencv2/videoio.hpp>

#include "eye_math.h"      // OneEuro, SplitWords
#include "mouth_math.h"
#include "mouth_tracker.h"
#include "udp_sender.h"

namespace ar6mouth {
namespace {

constexpr int kPortOut = 9886, kPortCmd = 9887, kPortEvent = 9888;
constexpr int kWork = 160;   // largeur de travail (pixels) : la détection se fait sur une image minuscule

double NowS() {
  using namespace std::chrono;
  return duration<double>(steady_clock::now().time_since_epoch()).count();
}

std::string Trim(const std::string& s) {
  size_t a = s.find_first_not_of(" \t\r\n"), b = s.find_last_not_of(" \t\r\n");
  return a == std::string::npos ? std::string() : s.substr(a, b - a + 1);
}

struct Tunables {
  double darkRatio = 0.45;   // la cavité = pixels plus sombres que ce rapport x la luminosité "peau" (75e centile)
  double minArea = 0.0015;   // aire mini de la cavité (part de l'image)
  double maxArea = 0.45;     // aire maxi
  double smooth = 3.0;       // OneEuro minCutoff (Hz)
  double reactivity = 0.08;  // OneEuro beta
  double roiX = 0, roiY = 0, roiW = 1, roiH = 1;   // zone analysée (parts de l'image)
  std::string fourcc = "auto";
};

const char* kDefaultTunables =
    "# Reglages du mouth tracking (ar6dof_probe relit ce fichier au demarrage ; supprime-le pour revenir aux valeurs par defaut)\n"
    "dark_ratio=0.45   # cavite = pixels plus sombres que ce rapport x luminosite de la peau : monte si l'interieur de la bouche est mal detecte\n"
    "min_area=0.0015   # aire mini de la cavite (part de l'image)\n"
    "max_area=0.45     # aire maxi\n"
    "smooth=3.0        # Hz : plus bas = plus stable, plus haut = plus nerveux\n"
    "reactivity=0.08   # plus haut = moins de retard sur les mouvements rapides\n"
    "roi=0,0,1,1       # zone analysee : x,y,largeur,hauteur en parts de l'image (exclut les joues, le nez...)\n"
    "fourcc=auto       # auto | mjpg | yuy2 | none\n";

Tunables LoadTunables(const std::string& path, const LogFn& log) {
  Tunables t;
  std::ifstream f(path);
  if (!f) {
    std::ofstream w(path);
    if (w) { w << kDefaultTunables; log("[bouche] reglages ecrits : " + path); }
    return t;
  }
  std::string line;
  while (std::getline(f, line)) {
    size_t h = line.find('#');
    if (h != std::string::npos) line = line.substr(0, h);
    size_t e = line.find('=');
    if (e == std::string::npos) continue;
    std::string k = Trim(line.substr(0, e)), v = Trim(line.substr(e + 1));
    try {
      if (k == "dark_ratio") t.darkRatio = std::stod(v);
      else if (k == "min_area") t.minArea = std::stod(v);
      else if (k == "max_area") t.maxArea = std::stod(v);
      else if (k == "smooth") t.smooth = std::stod(v);
      else if (k == "reactivity") t.reactivity = std::stod(v);
      else if (k == "fourcc") t.fourcc = v;
      else if (k == "roi") {
        for (char& c : v) if (c == ',') c = ' ';
        std::vector<std::string> w = eye::SplitWords(v);
        if (w.size() == 4) { t.roiX = std::stod(w[0]); t.roiY = std::stod(w[1]); t.roiW = std::stod(w[2]); t.roiH = std::stod(w[3]); }
      }
    } catch (...) { log("[bouche] valeur invalide ignoree : " + k + "=" + v); }
  }
  return t;
}

// Détection : cavité sombre la plus grande de la zone, hors bords latéraux (ombres du visage).
struct Detection {
  mouth::Feature f;
  cv::Rect box;   // dans l'image de travail
};

Detection Detect(const cv::Mat& gray, const Tunables& tn) {
  Detection d;
  cv::Mat g;
  cv::GaussianBlur(gray, g, cv::Size(5, 5), 0);
  int hist[256] = {0};
  for (int y = 0; y < g.rows; y++) {
    const uchar* p = g.ptr<uchar>(y);
    for (int x = 0; x < g.cols; x++) hist[p[x]]++;
  }
  const int total = g.rows * g.cols;
  int acc = 0, p75 = 255;
  for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc >= total * 0.75) { p75 = i; break; } }
  const int thr = std::max(8, (int)(p75 * tn.darkRatio));
  cv::Mat mask;
  cv::threshold(g, mask, thr, 255, cv::THRESH_BINARY_INV);
  cv::morphologyEx(mask, mask, cv::MORPH_OPEN, cv::getStructuringElement(cv::MORPH_ELLIPSE, cv::Size(3, 3)));
  cv::Mat labels, stats, cent;
  const int n = cv::connectedComponentsWithStats(mask, labels, stats, cent, 8, CV_32S);
  int best = -1;
  double bestArea = 0;
  const double imgArea = (double)g.cols * g.rows;
  for (int i = 1; i < n; i++) {
    const int x = stats.at<int>(i, cv::CC_STAT_LEFT), w = stats.at<int>(i, cv::CC_STAT_WIDTH);
    const double a = stats.at<int>(i, cv::CC_STAT_AREA);
    if (x <= 0 || x + w >= g.cols) continue;                 // touche un bord latéral : ombre du visage, pas la bouche
    if (a / imgArea < tn.minArea || a / imgArea > tn.maxArea) continue;
    if (a > bestArea) { bestArea = a; best = i; }
  }
  if (best < 0) return d;
  d.f.found = true;
  d.f.area = bestArea / imgArea;
  d.f.width = stats.at<int>(best, cv::CC_STAT_WIDTH) / (double)g.cols;
  d.f.height = stats.at<int>(best, cv::CC_STAT_HEIGHT) / (double)g.rows;
  d.f.cx = cent.at<double>(best, 0) / g.cols;
  d.f.cy = cent.at<double>(best, 1) / g.rows;
  d.box = cv::Rect(stats.at<int>(best, cv::CC_STAT_LEFT), stats.at<int>(best, cv::CC_STAT_TOP), stats.at<int>(best, cv::CC_STAT_WIDTH),
                   stats.at<int>(best, cv::CC_STAT_HEIGHT));
  return d;
}

class Tracker {
 public:
  Tracker(const Options& o, LogFn log) : opt_(o), log_(std::move(log)) {}

  bool Begin() {
    if (opt_.cam < 0) { log_("[bouche] aucune camera de bouche configuree"); return false; }
    tun_ = LoadTunables(opt_.dataDir + "\\ar6dof_mouth.txt", log_);
    fx_.minCutoff = fw_.minCutoff = fs_.minCutoff = fv_.minCutoff = tun_.smooth;
    fx_.beta = fw_.beta = fs_.beta = fv_.beta = tun_.reactivity;
    LoadCalibration();
    if (!out_.Open("127.0.0.1", kPortOut)) log_("[bouche] UDP 9886 indisponible");
    if (!ev_.Open("127.0.0.1", kPortEvent)) log_("[bouche] UDP 9888 indisponible");
    enabled_ = opt_.startEnabled;
    worker_ = std::thread([this] { Worker(); });
    cmdTh_ = std::thread([this] { CmdLoop(); });
    log_(std::string("[bouche] mouth tracking pret (") + (enabled_ ? "camera ouverte" : "en veille : commande MOUTH 1 pour demarrer") + "), cadence visee " +
         std::to_string(opt_.fps) + " fps");
    return true;
  }

  void End() {
    stop_ = true;
    { std::lock_guard<std::mutex> l(cvMu_); }
    cv_.notify_all();
    if (worker_.joinable()) worker_.join();
    if (cmdTh_.joinable()) cmdTh_.join();
    cap_.release();
  }

  bool Preview(cv::Mat* out) {
    previewReq_ = NowS();
    cv::Mat t(240, 320, CV_8UC3, cv::Scalar(30, 30, 30));
    {
      std::lock_guard<std::mutex> l(pm_);
      if (!prevGray_.empty() && camState_ == 1) {
        cv::Mat big;
        cv::resize(prevGray_, big, cv::Size(320, 240), 0, 0, cv::INTER_LINEAR);
        cv::cvtColor(big, t, cv::COLOR_GRAY2BGR);
        if (prevDet_.f.found) {
          const double s = 320.0 / prevGray_.cols;
          cv::rectangle(t, cv::Rect((int)(prevDet_.box.x * s), (int)(prevDet_.box.y * s), (int)(prevDet_.box.width * s), (int)(prevDet_.box.height * s)),
                        cv::Scalar(0, 255, 0), 1, cv::LINE_AA);
        }
        // zone analysée
        cv::rectangle(t, cv::Rect((int)(tun_.roiX * 320), (int)(tun_.roiY * 240), (int)(tun_.roiW * 320), (int)(tun_.roiH * 240)), cv::Scalar(120, 120, 120), 1);
      } else {
        cv::putText(t, enabled_ ? "attente de la camera..." : "bouche : en veille", cv::Point(10, 120), cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(160, 160, 160), 1, cv::LINE_AA);
      }
    }
    mouth::Out o;
    { std::lock_guard<std::mutex> l(m_); o = lastOut_; }
    char buf[120];
    if (o.valid) std::snprintf(buf, sizeof(buf), "ouverture %.2f  large %+.2f  cote %+.2f", o.jawOpen, o.wide, o.side);
    else std::snprintf(buf, sizeof(buf), "%s", GetCal().Valid() ? "bouche" : "pas encore etalonne");
    cv::putText(t, buf, cv::Point(6, 16), cv::FONT_HERSHEY_SIMPLEX, 0.45, cv::Scalar(255, 255, 0), 1, cv::LINE_AA);
    *out = t;
    return true;
  }

 private:
  // ------------------------------------------------ caméra
  bool TryOpen(int api, const std::string& fourcc) {
    cap_.release();
    if (!cap_.open(opt_.cam, api)) return false;
    if (fourcc == "mjpg") cap_.set(cv::CAP_PROP_FOURCC, cv::VideoWriter::fourcc('M', 'J', 'P', 'G'));
    else if (fourcc == "yuy2") cap_.set(cv::CAP_PROP_FOURCC, cv::VideoWriter::fourcc('Y', 'U', 'Y', '2'));
    cap_.set(cv::CAP_PROP_FRAME_WIDTH, 640);
    cap_.set(cv::CAP_PROP_FRAME_HEIGHT, 480);
    cap_.set(cv::CAP_PROP_FPS, std::max(30, opt_.fps));
    cv::Mat f;
    for (int i = 0; i < 8; i++) if (cap_.read(f) && !f.empty()) return true;
    cap_.release();
    return false;
  }

  bool OpenCamera() {
    const int api = opt_.backend == 0 ? cv::CAP_MSMF : cv::CAP_DSHOW;
    bool ok;
    if (tun_.fourcc == "auto") ok = TryOpen(api, "mjpg") || TryOpen(api, "none");
    else ok = TryOpen(api, tun_.fourcc);
    if (ok) { fx_.Reset(); fw_.Reset(); fs_.Reset(); fv_.Reset(); }
    return ok;
  }

  void Worker() {
    cv::Mat frame, small, gray;
    int fail = 0;
    bool warned = false;
    double lastProc = 0;
    const double minInterval = 0.9 / std::max(5, opt_.fps);
    while (!stop_) {
      if (!enabled_) {
        if (cap_.isOpened()) { cap_.release(); camState_ = 0; log_("[bouche] camera arretee"); }
        std::unique_lock<std::mutex> l(cvMu_);
        cv_.wait_for(l, std::chrono::milliseconds(500), [this] { return stop_ || enabled_; });
        continue;
      }
      if (!cap_.isOpened()) {
        if (!OpenCamera()) {
          if (!warned) { log_("[bouche] camera (numero " + std::to_string(opt_.cam) + ") introuvable ou muette : je reessaie toutes les 3 s"); warned = true; }
          std::unique_lock<std::mutex> l(cvMu_);
          cv_.wait_for(l, std::chrono::seconds(3), [this] { return stop_ || !enabled_; });
          continue;
        }
        warned = false;
        fail = 0;
        camState_ = 1;
        {
          std::lock_guard<std::mutex> l(calMu_);
          if (calPhase_ != 0 && calFrom_ > NowS() + 100.0) { calFrom_ = NowS() + 1.0; calTo_ = calFrom_ + 1.5; }
        }
        log_("[bouche] camera ouverte : " + std::to_string((int)cap_.get(cv::CAP_PROP_FRAME_WIDTH)) + "x" + std::to_string((int)cap_.get(cv::CAP_PROP_FRAME_HEIGHT)));
      }
      if (!cap_.grab()) {
        if (++fail > 100) {
          log_("[bouche] camera perdue (debranchee ?) : je reessaie");
          cap_.release();
          camState_ = 0;
          fail = 0;
          std::this_thread::sleep_for(std::chrono::seconds(1));
        }
        continue;
      }
      fail = 0;
      const double t = NowS();
      if (t - lastProc < minInterval) continue;
      if (!cap_.retrieve(frame) || frame.empty()) continue;
      lastProc = t;
      // zone analysée, puis réduction : tout le reste se fait sur ~160x120
      cv::Rect roi((int)(tun_.roiX * frame.cols), (int)(tun_.roiY * frame.rows), (int)(tun_.roiW * frame.cols), (int)(tun_.roiH * frame.rows));
      roi &= cv::Rect(0, 0, frame.cols, frame.rows);
      if (roi.width < 8 || roi.height < 8) roi = cv::Rect(0, 0, frame.cols, frame.rows);
      const double sc = std::min(1.0, (double)kWork / roi.width);
      cv::resize(frame(roi), small, cv::Size(std::max(8, (int)(roi.width * sc)), std::max(8, (int)(roi.height * sc))), 0, 0, cv::INTER_AREA);
      if (small.channels() == 3) cv::cvtColor(small, gray, cv::COLOR_BGR2GRAY);
      else if (small.channels() == 4) cv::cvtColor(small, gray, cv::COLOR_BGRA2GRAY);
      else gray = small;
      Process(gray, t);
    }
    cap_.release();
  }

  void Process(const cv::Mat& gray, double t) {
    const Detection d = Detect(gray, tun_);
    frames_++;
    if (d.f.found) lastSeen_ = t;
    {
      std::lock_guard<std::mutex> l(calMu_);
      if (calPhase_ != 0 && t >= calFrom_ && t <= calTo_) calSamples_.push_back(d.f);
    }
    mouth::Out o = mouth::Map(GetCal(), d.f);
    if (o.valid) {
      o.jawOpen = (float)fx_.Filter(o.jawOpen, t);
      o.wide = (float)fw_.Filter(o.wide, t);
      o.side = (float)fs_.Filter(o.side, t);
      o.vert = (float)fv_.Filter(o.vert, t);
    }
    {
      std::lock_guard<std::mutex> l(m_);
      lastOut_ = o;
      lastFeat_ = d.f;
    }
    if (NowS() - previewReq_ < 1.5) {
      std::lock_guard<std::mutex> l(pm_);
      gray.copyTo(prevGray_);
      prevDet_ = d;
    }
    Publish();
  }

  void Publish() {
    mouth::Packet p;
    mouth::Out o;
    mouth::Feature f;
    { std::lock_guard<std::mutex> l(m_); o = lastOut_; f = lastFeat_; }
    if (o.valid) p.flags |= mouth::kValid;
    if (GetCal().Valid()) p.flags |= mouth::kCalibrated;
    if (calPhase_ != 0) p.flags |= mouth::kCalibrating;
    p.seq = (uint16_t)(seq_++);
    p.jawOpen = o.jawOpen; p.wide = o.wide; p.side = o.side; p.vert = o.vert;
    p.rawArea = (float)f.area; p.rawWidth = (float)f.width;
    using namespace std::chrono;
    p.micros = (uint32_t)duration_cast<microseconds>(steady_clock::now().time_since_epoch()).count();
    uint8_t buf[mouth::kPacketSize];
    mouth::Encode(p, buf);
    out_.Send(buf, mouth::kPacketSize);
  }

  // ------------------------------------------------ étalonnage sauvegardé
  std::string CalPath() const { return opt_.dataDir + "\\ar6dof_mouth_cal.txt"; }

  void LoadCalibration() {
    std::ifstream f(CalPath());
    std::string line;
    mouth::Cal c;
    while (std::getline(f, line)) {
      if (line.size() < 3 || line[1] != ' ') continue;
      mouth::Feature ft;
      if (!mouth::FeatureFromString(line.substr(2), &ft)) continue;
      if (line[0] == 'N') { c.neutral = ft; c.hasNeutral = true; }
      else if (line[0] == 'O') { c.open = ft; c.hasOpen = true; }
      else if (line[0] == 'W') { c.wide = ft; c.hasWide = true; }
      else if (line[0] == 'S') { c.narrow = ft; c.hasNarrow = true; }
    }
    { std::lock_guard<std::mutex> l(cm_); cal_ = c; }
    if (c.Valid()) log_("[bouche] etalonnage precedent recharge");
  }

  void SaveCalibration() {
    std::ofstream f(CalPath());
    if (!f) { log_("[bouche] impossible d'ecrire " + CalPath()); return; }
    const mouth::Cal c = GetCal();
    if (c.hasNeutral) f << "N " << mouth::FeatureToString(c.neutral) << "\n";
    if (c.hasOpen) f << "O " << mouth::FeatureToString(c.open) << "\n";
    if (c.hasWide) f << "W " << mouth::FeatureToString(c.wide) << "\n";
    if (c.hasNarrow) f << "S " << mouth::FeatureToString(c.narrow) << "\n";
  }

  // ------------------------------------------------ évènements et commandes
  void Event(const std::string& s) { ev_.Send(reinterpret_cast<const uint8_t*>(s.data()), (int)s.size()); }

  mouth::Cal GetCal() { std::lock_guard<std::mutex> l(cm_); return cal_; }

  void SendStatus() {
    const mouth::Cal c = GetCal();
    char b[256];
    std::snprintf(b, sizeof(b), "STATUS on=%d cam=%d fps=%.0f see=%d neutral=%d open=%d wide=%d narrow=%d cal=%d", enabled_ ? 1 : 0, camState_.load(), fps_,
                  enabled_ && NowS() - lastSeen_.load() < 0.5 ? 1 : 0, c.hasNeutral ? 1 : 0, c.hasOpen ? 1 : 0, c.hasWide ? 1 : 0,
                  c.hasNarrow ? 1 : 0, c.Valid() ? 1 : 0);
    Event(b);
  }

  void SetEnabled(bool on) {
    if (on == enabled_) return;
    enabled_ = on;
    { std::lock_guard<std::mutex> l(cvMu_); }
    cv_.notify_all();
    if (!on) { std::lock_guard<std::mutex> l(pm_); prevGray_.release(); }
    log_(on ? "[bouche] demarrage de la camera" : "[bouche] arret de la camera demande");
  }

  // Une expression : on mesure pendant 1,5 s (après 1 s pour se mettre en position), puis on garde la médiane.
  void BeginPhase(int phase) {
    SetEnabled(true);
    std::lock_guard<std::mutex> l(calMu_);
    calSamples_.clear();
    calPhase_ = phase;
    const double now = NowS();
    if (camState_ == 1) {
      calFrom_ = now + 1.0;
      calTo_ = calFrom_ + 1.5;
    } else {   // la camera s'ouvre encore : la mesure commence 1 s apres son ouverture (voir Worker) ; delai maxi 10 s
      calFrom_ = now + 1e9;
      calTo_ = now + 10.0;
    }
  }

  void FinishPhase() {
    std::vector<mouth::Feature> s;
    int phase;
    {
      std::lock_guard<std::mutex> l(calMu_);
      s.swap(calSamples_);
      phase = calPhase_;
      calPhase_ = 0;
    }
    static const char* names[] = {"", "neutre", "grande ouverture", "large", "serree"};
    if (s.size() < 5) {
      Event(std::string("MCAL_DONE ") + std::to_string(phase) + " fail pas assez d'images (camera ouverte ?)");
      log_(std::string("[bouche] etalonnage '") + names[phase] + "' echoue : pas assez d'images");
      return;
    }
    const mouth::Feature m = mouth::MedianFeature(s);
    if (phase == 2 && !m.found) {
      Event("MCAL_DONE 2 fail aucune cavite vue bouche ouverte (lumiere ? zone ?)");
      log_("[bouche] bouche grande ouverte : aucune cavite detectee");
      return;
    }
    bool complete;
    {
      std::lock_guard<std::mutex> l(cm_);
      if (phase == 1) { cal_.neutral = m; cal_.hasNeutral = true; }
      else if (phase == 2) { cal_.open = m; cal_.hasOpen = true; }
      else if (phase == 3) { cal_.wide = m; cal_.hasWide = true; }
      else if (phase == 4) { cal_.narrow = m; cal_.hasNarrow = true; }
      complete = cal_.Valid();
    }
    fx_.Reset(); fw_.Reset(); fs_.Reset(); fv_.Reset();
    SaveCalibration();
    Event(std::string("MCAL_DONE ") + std::to_string(phase) + " ok");
    log_(std::string("[bouche] etalonnage '") + names[phase] + "' enregistre" + (complete ? " (etalonnage complet : neutre + grande ouverture)" : ""));
    SendStatus();
  }

  void Handle(const std::string& line) {
    const std::vector<std::string> w = eye::SplitWords(line);
    if (w.empty()) return;
    const std::string& c = w[0];
    if (c == "PING") Event("PONG");
    else if (c == "MOUTH" && w.size() >= 2) { SetEnabled(w[1] != "0"); SendStatus(); }
    else if (c == "MCAL" && w.size() >= 2) {
      const std::string& p = w[1];
      const int ph = p == "neutral" ? 1 : p == "open" ? 2 : p == "wide" ? 3 : p == "narrow" ? 4 : 0;
      if (ph) { BeginPhase(ph); log_("[bouche] etalonnage : prends l'expression dans 1 s et garde-la 1,5 s"); }
    } else if (c == "MCAL_CLEAR") {
      { std::lock_guard<std::mutex> l(cm_); cal_ = mouth::Cal(); }
      std::remove(CalPath().c_str());
      log_("[bouche] etalonnage efface");
      SendStatus();
    }
  }

  void CmdLoop() {
    SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s == INVALID_SOCKET) { log_("[bouche] UDP 9887 indisponible"); return; }
    sockaddr_in a{};
    a.sin_family = AF_INET;
    a.sin_port = htons((u_short)kPortCmd);
    inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
    if (bind(s, reinterpret_cast<sockaddr*>(&a), sizeof(a)) != 0) {
      log_("[bouche] port UDP 9887 deja utilise : commandes du mouth tracking indisponibles");
      closesocket(s);
      return;
    }
    DWORD to = 50;
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char*>(&to), sizeof(to));
    double lastStatus = 0, lastFpsT = NowS();
    char buf[512];
    while (!stop_) {
      const int n = recvfrom(s, buf, sizeof(buf) - 1, 0, nullptr, nullptr);
      if (n > 0) {
        buf[n] = 0;
        std::string all(buf, n);
        size_t p = 0;
        while (p <= all.size()) {
          size_t q = all.find('\n', p);
          if (q == std::string::npos) q = all.size();
          const std::string line = Trim(all.substr(p, q - p));
          if (!line.empty()) Handle(line);
          p = q + 1;
        }
      }
      const double now = NowS();
      bool done = false;
      {
        std::lock_guard<std::mutex> l(calMu_);
        done = calPhase_ != 0 && now > calTo_;
      }
      if (done) FinishPhase();
      if (now - lastFpsT >= 1.0) {
        const long f = frames_.load();
        fps_ = (f - fpsPrev_) / (now - lastFpsT);
        fpsPrev_ = f;
        lastFpsT = now;
      }
      if (now - lastStatus >= 1.0) { lastStatus = now; SendStatus(); }
    }
    closesocket(s);
  }

  Options opt_;
  LogFn log_;
  Tunables tun_;
  cv::VideoCapture cap_;
  std::thread worker_, cmdTh_;
  std::atomic<bool> stop_{false}, enabled_{false};
  std::mutex cvMu_;
  std::condition_variable cv_;
  UdpSender out_, ev_;
  std::atomic<unsigned> seq_{0};
  std::atomic<int> camState_{0};
  std::atomic<long> frames_{0};
  std::atomic<double> lastSeen_{0}, previewReq_{0};
  double fps_ = 0;
  long fpsPrev_ = 0;
  std::mutex cm_;
  mouth::Cal cal_;   // protégé par cm_
  eye::OneEuro fx_, fw_, fs_, fv_;
  // dernière mesure (protégée par m_)
  std::mutex m_;
  mouth::Out lastOut_;
  mouth::Feature lastFeat_;
  // aperçu (protégé par pm_)
  std::mutex pm_;
  cv::Mat prevGray_;
  Detection prevDet_;
  // étalonnage en cours (protégé par calMu_)
  std::mutex calMu_;
  std::atomic<int> calPhase_{0};
  double calFrom_ = 0, calTo_ = 0;
  std::vector<mouth::Feature> calSamples_;
};

std::unique_ptr<Tracker> g_tracker;

}  // namespace

void Start(const Options& o, LogFn log) {
  if (g_tracker) return;
  g_tracker.reset(new Tracker(o, log));
  if (!g_tracker->Begin()) { g_tracker->End(); g_tracker.reset(); }
}

void Stop() {
  if (!g_tracker) return;
  g_tracker->End();
  g_tracker.reset();
}

bool Running() { return g_tracker != nullptr; }

bool GetPreview(cv::Mat* out) { return g_tracker && g_tracker->Preview(out); }

}  // namespace ar6mouth
