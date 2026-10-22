// Eye tracking : threads caméras (un par oeil), détection pupille/reflet, étalonnage, UDP vers Unity.
// Les calculs sont dans eye_math.h (testé) et eye_pupil.h (algorithme validé par tests/eye_pupil_ref.py).
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
#include <map>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

#include <opencv2/imgproc.hpp>
#include <opencv2/videoio.hpp>

#include "eye_math.h"
#include "eye_pupil.h"
#include "eye_tracker.h"
#include "udp_sender.h"

namespace ar6eye {
namespace {

constexpr int kPortGaze = 9883, kPortCmd = 9884, kPortEvent = 9885;

double NowS() {
  using namespace std::chrono;
  return duration<double>(steady_clock::now().time_since_epoch()).count();
}

// ---------------------------------------------------------------- réglages (ar6dof_eye.txt, clé=valeur)
struct Tunables {
  eye::PupilConfig pupil;
  double smooth = 2.0;      // OneEuro minCutoff (Hz) : plus bas = plus stable à l'arrêt
  double reactivity = 0.05; // OneEuro beta
  std::string fourcc = "auto";  // auto | mjpg | yuy2 | none
};

const char* kDefaultTunables =
    "# Reglages de l'eye tracking (ar6dof_probe relit ce fichier au demarrage ; supprime-le pour revenir aux valeurs par defaut)\n"
    "# --- detection de la pupille ---\n"
    "margin=28          # gris au-dessus du point le plus sombre pour le seuil : monte si la pupille est mal remplie, baisse si l'iris est pris\n"
    "dark_pct=0.002     # part de l'image consideree comme 'la plus sombre'\n"
    "min_area=0.0025    # aire mini de la pupille (part de l'image)\n"
    "max_area=0.12      # aire maxi\n"
    "min_solidity=0.80  # 'pleine' : baisse si les cils/paupieres coupent la pupille\n"
    "min_aspect=0.50    # rapport petit axe / grand axe mini\n"
    "glint_min=200      # luminosite mini du reflet infrarouge (0-255)\n"
    "# --- lissage du regard ---\n"
    "smooth=2.0         # Hz : plus bas = plus stable a l'arret, plus haut = plus nerveux\n"
    "reactivity=0.05    # plus haut = moins de retard sur les saccades\n"
    "# --- camera ---\n"
    "fourcc=auto        # auto | mjpg | yuy2 | none (format demande a la camera)\n";

std::string Trim(const std::string& s) {
  size_t a = s.find_first_not_of(" \t\r\n"), b = s.find_last_not_of(" \t\r\n");
  return a == std::string::npos ? std::string() : s.substr(a, b - a + 1);
}

Tunables LoadTunables(const std::string& path, const LogFn& log) {
  Tunables t;
  std::ifstream f(path);
  if (!f) {
    std::ofstream w(path);
    if (w) { w << kDefaultTunables; log("[yeux] reglages ecrits : " + path); }
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
      if (k == "margin") t.pupil.margin = std::stoi(v);
      else if (k == "dark_pct") t.pupil.darkPct = std::stod(v);
      else if (k == "min_area") t.pupil.minArea = std::stod(v);
      else if (k == "max_area") t.pupil.maxArea = std::stod(v);
      else if (k == "min_solidity") t.pupil.minSolidity = std::stod(v);
      else if (k == "min_aspect") t.pupil.minAspect = std::stod(v);
      else if (k == "glint_min") t.pupil.glintMin = std::stoi(v);
      else if (k == "smooth") t.smooth = std::stod(v);
      else if (k == "reactivity") t.reactivity = std::stod(v);
      else if (k == "fourcc") t.fourcc = v;
    } catch (...) { log("[yeux] valeur invalide ignoree : " + k + "=" + v); }
  }
  return t;
}

// ---------------------------------------------------------------- un oeil
struct EyeOut {
  bool found = false, valid = false, blink = false;
  double yaw = 0, pitch = 0, open = 1;
};

struct Eye {
  int idx = 0, cam = -1;
  std::thread th;
  cv::VideoCapture cap;
  eye::PupilDetector det;
  // protégé par m
  std::mutex m;
  eye::Map map;
  eye::OneEuro fyaw, fpitch;
  eye::BlinkState blink;
  eye::Feature lastFeat;
  bool lastFound = false;
  double yaw = 0, pitch = 0;
  EyeOut out;
  // aperçu (protégé par pm)
  std::mutex pm;
  cv::Mat prevGray;
  eye::PupilResult prevRes;
  // état
  std::atomic<int> camState{0};     // 0 fermée, 1 ouverte
  std::atomic<long> frames{0};
  std::atomic<double> lastSeen{0};  // dernier instant où la pupille a été trouvée
  double fps = 0;
  long fpsPrev = 0;
};

class Tracker {
 public:
  Tracker(const Options& o, LogFn log) : opt_(o), log_(std::move(log)) {}

  bool Begin() {
    tun_ = LoadTunables(opt_.dataDir + "\\ar6dof_eye.txt", log_);
    if (opt_.camL < 0 && opt_.camR < 0) { log_("[yeux] aucune camera d'oeil configuree"); return false; }
    int cams[2] = {opt_.camL, opt_.camR};
    for (int i = 0; i < 2; i++) {
      if (cams[i] < 0) continue;
      eyes_[i].reset(new Eye());
      eyes_[i]->idx = i;
      eyes_[i]->cam = cams[i];
      eyes_[i]->det.SetConfig(tun_.pupil);
      ApplyFilterConfig(*eyes_[i]);
    }
    pubInterval_ = 0.8 / std::max(5, opt_.fps);
    LoadCalibration();
    if (!gazeOut_.Open("127.0.0.1", kPortGaze)) log_("[yeux] UDP 9883 indisponible");
    if (!evOut_.Open("127.0.0.1", kPortEvent)) log_("[yeux] UDP 9885 indisponible");
    enabled_ = opt_.startEnabled;
    for (int i = 0; i < 2; i++)
      if (eyes_[i]) eyes_[i]->th = std::thread([this, i] { Worker(*eyes_[i]); });
    cmdTh_ = std::thread([this] { CmdLoop(); });
    log_(std::string("[yeux] eye tracking pret (") + (enabled_ ? "cameras ouvertes" : "en veille : commande EYE 1 pour demarrer") + "), cadence visee " +
         std::to_string(opt_.fps) + " fps par oeil");
    return true;
  }

  void End() {
    stop_ = true;
    { std::lock_guard<std::mutex> l(cvMu_); }
    cv_.notify_all();
    for (auto& e : eyes_) if (e && e->th.joinable()) e->th.join();
    if (cmdTh_.joinable()) cmdTh_.join();
    for (auto& e : eyes_) if (e) e->cap.release();
  }

  bool Preview(cv::Mat* out) {
    previewReq_ = NowS();
    cv::Mat tiles[2];
    bool any = false;
    for (int i = 0; i < 2; i++) {
      cv::Mat t(240, 320, CV_8UC3, cv::Scalar(30, 30, 30));
      Eye* e = eyes_[i].get();
      const char* name = i == 0 ? "gauche" : "droit";
      if (!e) {
        cv::putText(t, std::string(name) + " : pas de camera", cv::Point(10, 120), cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(160, 160, 160), 1, cv::LINE_AA);
      } else {
        std::lock_guard<std::mutex> l(e->pm);
        if (!e->prevGray.empty() && e->camState == 1) {
          cv::cvtColor(e->prevGray, t, cv::COLOR_GRAY2BGR);
          if (e->prevRes.found) {
            cv::circle(t, cv::Point((int)std::lround(e->prevRes.px), (int)std::lround(e->prevRes.py)), (int)std::lround(e->prevRes.radius), cv::Scalar(0, 255, 0), 1, cv::LINE_AA);
            if (e->prevRes.glint)
              cv::drawMarker(t, cv::Point((int)std::lround(e->prevRes.gx), (int)std::lround(e->prevRes.gy)), cv::Scalar(0, 0, 255), cv::MARKER_CROSS, 9, 1);
          }
          any = true;
        } else {
          cv::putText(t, std::string(name) + (enabled_ ? " : attente de la camera..." : " : en veille"), cv::Point(10, 120), cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(160, 160, 160), 1, cv::LINE_AA);
        }
      }
      EyeOut o;
      if (e) { std::lock_guard<std::mutex> l(e->m); o = e->out; }
      char buf[96];
      if (e && o.valid) std::snprintf(buf, sizeof(buf), "%s  yaw %+.1f  pitch %+.1f", name, o.yaw, o.pitch);
      else if (e && o.blink) std::snprintf(buf, sizeof(buf), "%s  clignement", name);
      else std::snprintf(buf, sizeof(buf), "%s", name);
      cv::putText(t, buf, cv::Point(6, 16), cv::FONT_HERSHEY_SIMPLEX, 0.45, cv::Scalar(255, 255, 0), 1, cv::LINE_AA);
      tiles[i] = t;
    }
    if (!any && !enabled_) { /* on montre quand même l'état */ }
    cv::hconcat(tiles[0], tiles[1], *out);
    return true;
  }

 private:
  void ApplyFilterConfig(Eye& e) {
    e.fyaw.minCutoff = e.fpitch.minCutoff = tun_.smooth;
    e.fyaw.beta = e.fpitch.beta = tun_.reactivity;
  }

  // ------------------------------------------------ caméras
  bool TryOpen(Eye& e, int api, const std::string& fourcc) {
    e.cap.release();
    if (!e.cap.open(e.cam, api)) return false;
    if (fourcc == "mjpg") e.cap.set(cv::CAP_PROP_FOURCC, cv::VideoWriter::fourcc('M', 'J', 'P', 'G'));
    else if (fourcc == "yuy2") e.cap.set(cv::CAP_PROP_FOURCC, cv::VideoWriter::fourcc('Y', 'U', 'Y', '2'));
    e.cap.set(cv::CAP_PROP_FRAME_WIDTH, 320);
    e.cap.set(cv::CAP_PROP_FRAME_HEIGHT, 240);
    e.cap.set(cv::CAP_PROP_FPS, 120);
    cv::Mat f;
    for (int i = 0; i < 8; i++) if (e.cap.read(f) && !f.empty()) return true;   // la camera doit vraiment donner des images
    e.cap.release();
    return false;
  }

  bool OpenEye(Eye& e) {
    const int api = opt_.backend == 0 ? cv::CAP_MSMF : cv::CAP_DSHOW;
    bool ok = false;
    if (tun_.fourcc == "auto") ok = TryOpen(e, api, "mjpg") || TryOpen(e, api, "none");
    else ok = TryOpen(e, api, tun_.fourcc);
    if (!ok) return false;
    e.det.Reset();
    std::lock_guard<std::mutex> l(e.m);
    e.blink = eye::BlinkState();
    e.fyaw.Reset(); e.fpitch.Reset();
    return true;
  }

  void Worker(Eye& e) {
    const char* name = e.idx == 0 ? "gauche" : "droit";
    cv::Mat frame, gray;
    int fail = 0;
    bool warned = false;
    double lastProc = 0;
    const double minInterval = 0.9 / std::max(5, opt_.fps);
    while (!stop_) {
      if (!enabled_) {
        if (e.cap.isOpened()) {   // arrêt demandé : la caméra est libérée, plus aucune charge
          e.cap.release();
          e.camState = 0;
          log_(std::string("[yeux] camera ") + name + " arretee");
        }
        std::unique_lock<std::mutex> l(cvMu_);
        cv_.wait_for(l, std::chrono::milliseconds(500), [this] { return stop_ || enabled_; });
        continue;
      }
      if (!e.cap.isOpened()) {
        if (!OpenEye(e)) {
          if (!warned) {
            log_(std::string("[yeux] camera ") + name + " (numero " + std::to_string(e.cam) + ") introuvable ou muette : je reessaie toutes les 3 s");
            warned = true;
          }
          std::unique_lock<std::mutex> l(cvMu_);
          cv_.wait_for(l, std::chrono::seconds(3), [this] { return stop_ || !enabled_; });
          continue;
        }
        warned = false;
        fail = 0;
        e.camState = 1;
        log_(std::string("[yeux] camera ") + name + " ouverte : " + std::to_string((int)e.cap.get(cv::CAP_PROP_FRAME_WIDTH)) + "x" +
             std::to_string((int)e.cap.get(cv::CAP_PROP_FRAME_HEIGHT)));
      }
      if (!e.cap.grab()) {   // grab() seul : on ne décode que les images qu'on traite
        if (++fail > 100) {
          log_(std::string("[yeux] camera ") + name + " perdue (debranchee ?) : je reessaie");
          e.cap.release();
          e.camState = 0;
          fail = 0;
          std::this_thread::sleep_for(std::chrono::seconds(1));
        }
        continue;
      }
      fail = 0;
      const double t = NowS();
      if (t - lastProc < minInterval) continue;
      if (!e.cap.retrieve(frame) || frame.empty()) continue;
      lastProc = t;
      if (frame.channels() == 1) gray = frame;
      else cv::extractChannel(frame, gray, 1);
      if (gray.type() != CV_8UC1) continue;
      Process(e, gray, t);
    }
    e.cap.release();
  }

  // ------------------------------------------------ une image
  void Process(Eye& e, const cv::Mat& gray, double t) {
    const eye::PupilResult r = e.det.Detect(gray);
    eye::Feature f;
    f.px = r.px; f.py = r.py; f.gx = r.gx; f.gy = r.gy; f.glint = r.glint;
    e.frames++;
    if (r.found) e.lastSeen = t;

    if (r.found && calActive_) {
      std::lock_guard<std::mutex> l(calMu_);
      if (t >= calFrom_ && t <= calTo_ && !cal_[e.idx].empty()) cal_[e.idx].back().f.push_back(f);
    }
    {
      std::lock_guard<std::mutex> l(e.m);
      e.blink.Update(r.found, r.area);
      EyeOut o;
      o.found = r.found;
      o.blink = e.blink.blink;
      o.open = e.blink.openness;
      if (r.found) {
        e.lastFeat = f;
        e.lastFound = true;
        double y = 0, p = 0;
        if (!o.blink && e.map.Apply(f, &y, &p)) {
          e.yaw = e.fyaw.Filter(y, t);
          e.pitch = e.fpitch.Filter(p, t);
          o.valid = true;
        }
      } else {
        e.lastFound = false;
      }
      o.yaw = e.yaw;
      o.pitch = e.pitch;
      e.out = o;
    }
    if (NowS() - previewReq_ < 1.5) {   // copie pour la fenêtre de contrôle seulement si elle est ouverte
      std::lock_guard<std::mutex> l(e.pm);
      gray.copyTo(e.prevGray);
      e.prevRes = r;
    }
    if (t - lastPub_ >= pubInterval_) { lastPub_ = t; Publish(); }   // un paquet par cycle, que ce soit l'oeil gauche ou droit qui le déclenche
  }

  void Publish() {
    std::lock_guard<std::mutex> pl(pubMu_);
    EyeOut o[2];
    for (int i = 0; i < 2; i++)
      if (eyes_[i]) { std::lock_guard<std::mutex> l(eyes_[i]->m); o[i] = eyes_[i]->out; }
    eye::GazePacket g;
    g.seq = (uint16_t)(seq_++);
    g.flags = 0;
    if (o[0].valid) g.flags |= eye::kValidL;
    if (o[1].valid) g.flags |= eye::kValidR;
    if (o[0].blink) g.flags |= eye::kBlinkL;
    if (o[1].blink) g.flags |= eye::kBlinkR;
    if (calibrated_) g.flags |= eye::kCalibrated;
    if (calActive_) g.flags |= eye::kCalibrating;
    g.yawL = (float)o[0].yaw; g.pitchL = (float)o[0].pitch;
    g.yawR = (float)o[1].yaw; g.pitchR = (float)o[1].pitch;
    g.openL = (float)o[0].open; g.openR = (float)o[1].open;
    if (o[0].valid && o[1].valid) {
      g.yawC = (float)(0.5 * (o[0].yaw + o[1].yaw));
      g.pitchC = (float)(0.5 * (o[0].pitch + o[1].pitch));
    } else if (o[0].valid) { g.yawC = g.yawL; g.pitchC = g.pitchL; }
    else if (o[1].valid) { g.yawC = g.yawR; g.pitchC = g.pitchR; }
    else { g.yawC = lastYawC_; g.pitchC = lastPitchC_; }
    lastYawC_ = g.yawC; lastPitchC_ = g.pitchC;
    using namespace std::chrono;
    g.micros = (uint32_t)duration_cast<microseconds>(steady_clock::now().time_since_epoch()).count();
    uint8_t buf[eye::kGazeSize];
    eye::EncodeGaze(g, buf);
    gazeOut_.Send(buf, eye::kGazeSize);
  }

  // ------------------------------------------------ étalonnage sauvegardé
  std::string CalPath() const { return opt_.dataDir + "\\ar6dof_eye_cal.txt"; }

  void LoadCalibration() {
    std::ifstream f(CalPath());
    std::string line;
    int n = 0;
    while (std::getline(f, line)) {
      if (line.size() < 3 || (line[0] != 'L' && line[0] != 'R') || line[1] != ' ') continue;
      const int i = line[0] == 'L' ? 0 : 1;
      eye::Map m;
      if (eyes_[i] && eye::MapFromString(line.substr(2), &m)) { eyes_[i]->map = m; n++; }
    }
    calibrated_ = n > 0;
    if (n > 0) log_("[yeux] etalonnage precedent recharge (" + std::to_string(n) + " oeil)");
  }

  void SaveCalibration() {
    std::ofstream f(CalPath());
    if (!f) { log_("[yeux] impossible d'ecrire " + CalPath()); return; }
    for (int i = 0; i < 2; i++) {
      if (!eyes_[i]) continue;
      std::lock_guard<std::mutex> l(eyes_[i]->m);
      if (eyes_[i]->map.valid) f << (i == 0 ? "L " : "R ") << eye::MapToString(eyes_[i]->map) << "\n";
    }
  }

  void UpdateCalibratedFlag() {
    bool any = false;
    for (int i = 0; i < 2; i++)
      if (eyes_[i]) { std::lock_guard<std::mutex> l(eyes_[i]->m); any = any || eyes_[i]->map.valid; }
    calibrated_ = any;
  }

  // ------------------------------------------------ évènements et commandes
  void Event(const std::string& s) { evOut_.Send(reinterpret_cast<const uint8_t*>(s.data()), (int)s.size()); }

  void SendStatus() {
    char b[256];
    auto cs = [&](int i) { return eyes_[i] ? eyes_[i]->camState.load() : -1; };
    auto see = [&](int i) { return eyes_[i] && enabled_ && NowS() - eyes_[i]->lastSeen.load() < 0.5 ? 1 : 0; };
    auto cal = [&](int i) { if (!eyes_[i]) return 0; std::lock_guard<std::mutex> l(eyes_[i]->m); return eyes_[i]->map.valid ? 1 : 0; };
    std::snprintf(b, sizeof(b), "STATUS on=%d camL=%d camR=%d fpsL=%.0f fpsR=%.0f calL=%d calR=%d seeL=%d seeR=%d",
                  enabled_ ? 1 : 0, cs(0), cs(1), eyes_[0] ? eyes_[0]->fps : 0.0, eyes_[1] ? eyes_[1]->fps : 0.0, cal(0), cal(1), see(0), see(1));
    Event(b);
  }

  void SetEnabled(bool on) {
    if (on == enabled_) return;
    enabled_ = on;
    { std::lock_guard<std::mutex> l(cvMu_); }
    cv_.notify_all();
    if (!on) {
      for (auto& e : eyes_) if (e) { std::lock_guard<std::mutex> l(e->pm); e->prevGray.release(); }
    }
    log_(on ? "[yeux] demarrage des cameras" : "[yeux] arret des cameras demande");
  }

  void SolveAll() {
    std::vector<eye::PointSamples> pts[2];
    {
      std::lock_guard<std::mutex> l(calMu_);
      pts[0] = cal_[0];
      pts[1] = cal_[1];
    }
    bool ok[2] = {false, false};
    double rms[2] = {0, 0};
    int used[2] = {0, 0};
    for (int i = 0; i < 2; i++) {
      if (!eyes_[i]) continue;
      eye::Map mp;
      ok[i] = eye::SolveEye(pts[i], &mp, &rms[i], &used[i]);
      if (ok[i]) {
        std::lock_guard<std::mutex> l(eyes_[i]->m);
        eyes_[i]->map = mp;
        eyes_[i]->fyaw.Reset();
        eyes_[i]->fpitch.Reset();
      }
    }
    calActive_ = false;
    {
      std::lock_guard<std::mutex> l(calMu_);
      pointPending_ = false;
    }
    if (!ok[0] && !ok[1]) {
      Event("CAL_RESULT fail pas assez de mesures (pupille perdue ? verifie la fenetre yeux)");
      log_("[yeux] etalonnage echoue : pas assez de mesures exploitables");
      return;
    }
    SaveCalibration();
    UpdateCalibratedFlag();
    char b[160];
    std::snprintf(b, sizeof(b), "CAL_RESULT ok %.2f %.2f %d %d", ok[0] ? rms[0] : -1.0, ok[1] ? rms[1] : -1.0, used[0], used[1]);
    Event(b);
    log_(std::string("[yeux] etalonnage termine : ") + b);
  }

  void Recenter() {
    int n = 0;
    for (int i = 0; i < 2; i++) {
      Eye* e = eyes_[i].get();
      if (!e) continue;
      std::lock_guard<std::mutex> l(e->m);
      if (!e->map.valid || !e->lastFound) continue;
      eye::Map tmp = e->map;
      tmp.offYaw = 0; tmp.offPitch = 0;
      double y = 0, p = 0;
      if (!tmp.Apply(e->lastFeat, &y, &p)) continue;
      e->map.offYaw = -y;
      e->map.offPitch = -p;
      e->fyaw.Reset(); e->fpitch.Reset();
      n++;
    }
    if (n > 0) SaveCalibration();
    Event(n > 0 ? "RECENTER ok" : "RECENTER fail oeil non detecte ou pas d'etalonnage");
    log_(n > 0 ? "[yeux] regard recentre" : "[yeux] recentrage impossible (oeil non detecte ou pas d'etalonnage)");
  }

  void Handle(const std::string& line) {
    const std::vector<std::string> w = eye::SplitWords(line);
    if (w.empty()) return;
    const std::string& c = w[0];
    try {
      if (c == "PING") Event("PONG");
      else if (c == "EYE" && w.size() >= 2) { SetEnabled(w[1] != "0"); SendStatus(); }
      else if (c == "CAL_BEGIN") {
        SetEnabled(true);
        std::lock_guard<std::mutex> l(calMu_);
        cal_[0].clear(); cal_[1].clear();
        calFrom_ = calTo_ = 0;
        pointPending_ = false;
        calActive_ = true;
        log_("[yeux] etalonnage : debut");
      } else if (c == "CAL_POINT" && w.size() >= 5) {
        std::lock_guard<std::mutex> l(calMu_);
        if (!calActive_) return;
        const double settle = std::stod(w[3]) / 1000.0, collect = std::stod(w[4]) / 1000.0;
        for (int i = 0; i < 2; i++) {
          eye::PointSamples p;
          p.yaw = std::stod(w[1]);
          p.pitch = std::stod(w[2]);
          cal_[i].push_back(p);
        }
        const double now = NowS();
        calFrom_ = now + settle;
        calTo_ = calFrom_ + collect;
        pointPending_ = true;
      } else if (c == "CAL_SOLVE") SolveAll();
      else if (c == "CAL_CANCEL") {
        std::lock_guard<std::mutex> l(calMu_);
        calActive_ = false;
        pointPending_ = false;
        cal_[0].clear(); cal_[1].clear();
        log_("[yeux] etalonnage annule");
      } else if (c == "CAL_CLEAR") {
        for (auto& e : eyes_) if (e) { std::lock_guard<std::mutex> l(e->m); e->map = eye::Map(); e->fyaw.Reset(); e->fpitch.Reset(); }
        calibrated_ = false;
        std::remove(CalPath().c_str());
        log_("[yeux] etalonnage efface");
        SendStatus();
      } else if (c == "RECENTER") Recenter();
    } catch (...) { log_("[yeux] commande invalide : " + line); }
  }

  void CmdLoop() {
    SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s == INVALID_SOCKET) { log_("[yeux] UDP 9884 indisponible"); return; }
    sockaddr_in a{};
    a.sin_family = AF_INET;
    a.sin_port = htons((u_short)kPortCmd);
    inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
    if (bind(s, reinterpret_cast<sockaddr*>(&a), sizeof(a)) != 0) {
      log_("[yeux] port UDP 9884 deja utilise : commandes d'eye tracking indisponibles");
      closesocket(s);
      return;
    }
    DWORD to = 50;   // ms : on se reveille 20 fois par seconde au plus
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char*>(&to), sizeof(to));
    double lastStatus = 0, lastFpsT = NowS();
    char buf[512];
    while (!stop_) {
      const int n = recvfrom(s, buf, sizeof(buf) - 1, 0, nullptr, nullptr);
      if (n > 0) {
        buf[n] = 0;
        std::string all(buf, n), line;
        size_t p = 0;
        while (p <= all.size()) {
          size_t q = all.find('\n', p);
          if (q == std::string::npos) q = all.size();
          line = Trim(all.substr(p, q - p));
          if (!line.empty()) Handle(line);
          p = q + 1;
        }
      }
      const double now = NowS();
      bool done = false;
      int nL = 0, nR = 0;
      {
        std::lock_guard<std::mutex> l(calMu_);
        if (calActive_ && pointPending_ && now > calTo_) {
          pointPending_ = false;
          done = true;
          nL = cal_[0].empty() ? 0 : (int)cal_[0].back().f.size();
          nR = cal_[1].empty() ? 0 : (int)cal_[1].back().f.size();
        }
      }
      if (done) Event("POINT_DONE " + std::to_string(nL) + " " + std::to_string(nR));
      if (now - lastFpsT >= 1.0) {
        for (auto& e : eyes_) if (e) { const long f = e->frames.load(); e->fps = (f - e->fpsPrev) / (now - lastFpsT); e->fpsPrev = f; }
        lastFpsT = now;
      }
      if (now - lastStatus >= 1.0) { lastStatus = now; SendStatus(); }
    }
    closesocket(s);
  }

  Options opt_;
  LogFn log_;
  Tunables tun_;
  std::unique_ptr<Eye> eyes_[2];
  double pubInterval_ = 0.01;
  std::atomic<double> lastPub_{0};
  std::mutex pubMu_;
  std::thread cmdTh_;
  std::atomic<bool> stop_{false}, enabled_{false};
  std::mutex cvMu_;
  std::condition_variable cv_;
  UdpSender gazeOut_, evOut_;
  std::atomic<unsigned> seq_{0};
  std::atomic<double> previewReq_{0};
  float lastYawC_ = 0, lastPitchC_ = 0;   // protégés par pubMu_
  // étalonnage en cours
  std::mutex calMu_;
  std::atomic<bool> calActive_{false}, calibrated_{false};
  bool pointPending_ = false;
  double calFrom_ = 0, calTo_ = 0;
  std::vector<eye::PointSamples> cal_[2];
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

}  // namespace ar6eye
