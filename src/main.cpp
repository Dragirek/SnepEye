// ar6dof_probe : casque AR open source, étape 2 (tracking 6DoF).
//
//  Fenêtre "ar6dof_probe" (caméras) :
//    s sauver une paire brute | r brut/rectifié | c qualité de la calibration
//    x inverser gauche/droite | q quitter
//  Fenêtre "orientation" (IMU en 3D) :
//    g puis f = calibration guidée du montage | 1-6 tourner de 90 deg | 0 reset | z yaw=0
//  Fenêtre "trajectoire" (tracking) :
//    t démarrer/arrêter+recentrer | u prior IMU on/off | [ ] délai caméra | - = prédiction
//    , . zoom de la grille | m poser un repère (distance mesurée en cm) | n l'effacer
//    v verrouiller/déverrouiller le sol détecté
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include <atomic>
#include <condition_variable>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <deque>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <utility>

#include <opencv2/opencv.hpp>

#include "calib.h"
#include "camera_model_json.h"
#include "ble_remote.h"
#include "delay_estimator.h"
#include "devices.h"
#include "floor.h"
#include "frame_clock.h"
#include "frame_share_win.h"
#include "fusion.h"
#include "imu_parser.h"
#include "orientation.h"
#include "pose_snap.h"
#include "pose_packet.h"
#include "serial_win.h"
#include "tracker.h"
#include "udp_control.h"
#include "udp_sender.h"

struct ImuState {
  std::mutex m;
  Vec3 acc_s, gyr_s;          // valeurs brutes, repère du capteur
  Mat3 mount;                 // capteur -> casque
  Quat q;                     // orientation casque -> monde
  bool q_ok = false;
  bool reinit = false, zero_yaw = false;
  std::deque<GyroSample> gyroBuf;  // gyro (repère casque) horodaté sur l'horloge du PC
  ClockSync clk;
  double rate_acc = 0, rate_gyr = 0;
  uint64_t bytes = 0, ok = 0, bad = 0;
  bool port_error = false;
};

static std::atomic<bool> g_run{true};

// Secondes depuis le lancement (horloge du PC, commune à l'IMU et aux images).
static double NowS() {
  static const auto t0 = std::chrono::steady_clock::now();
  return std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
}

static std::string ExeDir() {
  char p[MAX_PATH];
  DWORD n = GetModuleFileNameA(nullptr, p, MAX_PATH);
  std::string s(p, n);
  size_t k = s.find_last_of("\\/");
  return k == std::string::npos ? std::string(".") : s.substr(0, k);
}

static void SaveMount(const Mat3& m) {
  std::ofstream f(ExeDir() + "\\mount.txt");
  for (int i = 0; i < 3; i++)
    for (int j = 0; j < 3; j++) f << m.m[i][j] << " ";
}

static Mat3 LoadMount() {
  std::ifstream f(ExeDir() + "\\mount.txt");
  double v[9];
  for (int i = 0; i < 9; i++)
    if (!(f >> v[i])) return Mat3();
  Mat3 t;
  for (int i = 0; i < 3; i++)
    for (int j = 0; j < 3; j++) t.m[i][j] = v[i * 3 + j];
  return std::abs(t.Det() - 1.0) < 1e-3 ? t : Mat3();
}

// Horloge système haute résolution, commune à ar6dof_probe et à Unity (sert à horodater les poses).
static double QpcSeconds() {
  static const LARGE_INTEGER freq = [] { LARGE_INTEGER f; QueryPerformanceFrequency(&f); return f; }();
  LARGE_INTEGER c;
  QueryPerformanceCounter(&c);
  return (double)c.QuadPart / (double)freq.QuadPart;
}

// Appelée par le thread IMU (verrou de l'IMU déjà pris) après chaque lot de mesures gyro.
static void PublishPose(ImuState* st, VisionSnap* vs, UdpSender* udp, uint32_t* seq, const Mat3& Rcb) {
  SnapData sd;
  { std::lock_guard<std::mutex> l(vs->m); sd = vs->d; }
  if (!sd.valid) return;
  PoseOut po = ComputePose(sd, st->gyroBuf, NowS() + sd.predS, Rcb);
  auto pkt = PackPose(sd.flags, (*seq)++, po.p, QuatFromMat3(po.R), po.vG, po.wG, sd.floorZ, QpcSeconds() + sd.predS, (float)sd.camDelay);
  udp->Send(pkt.data(), (int)pkt.size());
}

static void ImuThread(std::string port, ImuState* st, VisionSnap* vs, UdpSender* udp) {
  SerialPort sp;
  if (!sp.open(port)) {
    std::lock_guard<std::mutex> l(st->m);
    st->port_error = true;
    return;
  }
  ImuParser parser;
  Mahony filt;
  Vec3 accBody;
  bool haveAcc = false, haveT = false;
  int64_t lastT = 0;
  uint64_t n_acc = 0, n_gyr = 0, a0 = 0, g0 = 0, pubGyro = 0;
  uint32_t pubSeq = 0;
  const Mat3 Rcb = CamFromBody();
  auto t0 = std::chrono::steady_clock::now();
  uint8_t buf[4096];
  while (g_run) {
    int n = sp.read(buf, sizeof(buf));
    if (n < 0) {
      std::lock_guard<std::mutex> l(st->m);
      st->port_error = true;
      return;
    }
    if (n > 0) {
      std::lock_guard<std::mutex> l(st->m);
      parser.feed(buf, n, [&](const ImuSample& s) {
        if (st->reinit) { filt.Reset(); haveAcc = false; haveT = false; st->gyroBuf.clear(); st->reinit = false; }
        if (st->zero_yaw) { filt.ZeroYaw(); st->zero_yaw = false; }
        Vec3 v{s.v[0], s.v[1], s.v[2]};
        if (s.type == 1) {
          st->acc_s = v;
          ++n_acc;
          accBody = st->mount * v;
          haveAcc = true;
        } else {
          st->gyr_s = v;
          ++n_gyr;
          {
            double tm = st->clk.ToHost((double)s.t_us * 1e-6, NowS());
            st->gyroBuf.push_back({tm, st->mount * v});
            while (st->gyroBuf.size() > 1 && st->gyroBuf.back().t - st->gyroBuf.front().t > 3.0)
              st->gyroBuf.pop_front();
          }
          if (haveAcc) {
            Vec3 g = st->mount * v;
            if (haveT) {
              double dt = (double)(s.t_us - lastT) * 1e-6;
              if (dt > 0 && dt < 0.1) filt.Update(g, accBody, dt);
            } else {
              filt.Update(g, accBody, 0.0);
            }
            haveT = true;
            lastT = s.t_us;
            st->q = filt.q();
            st->q_ok = filt.Initialized();
          }
        }
      });
      st->bytes = parser.bytes_total;
      st->ok = parser.packets_ok;
      st->bad = parser.bad_checksum;
      if (vs && udp && udp->IsOpen() && n_gyr != pubGyro) {  // nouvelles mesures gyro : on publie la pose tout de suite
        pubGyro = n_gyr;
        PublishPose(st, vs, udp, &pubSeq, Rcb);
      }
    }
    auto now = std::chrono::steady_clock::now();
    double dt = std::chrono::duration<double>(now - t0).count();
    if (dt >= 1.0) {
      std::lock_guard<std::mutex> l(st->m);
      st->rate_acc = (n_acc - a0) / dt;
      st->rate_gyr = (n_gyr - g0) / dt;
      a0 = n_acc; g0 = n_gyr; t0 = now;
    }
  }
}

// ---- Configuration : les réponses sont sauvegardées ; avec --auto elles sont rejouées sans aucune question ----
static bool g_auto = false;
static std::map<std::string, std::string> g_cfg;

static std::string ConfigPath() { return ExeDir() + "\\ar6dof_config.txt"; }
static void LoadConfig() {
  std::ifstream f(ConfigPath());
  std::string line;
  while (std::getline(f, line)) {
    size_t e = line.find('=');
    if (e != std::string::npos) g_cfg[line.substr(0, e)] = line.substr(e + 1);
  }
}
static void SaveConfig() {
  std::ofstream f(ConfigPath());
  for (const auto& kv : g_cfg) f << kv.first << "=" << kv.second << "\n";
}

static int SavedInt(const char* key, int def, int lo, int hi) {
  auto it = g_cfg.find(key);
  if (it != g_cfg.end()) {
    try { int v = std::stoi(it->second); if (v >= lo && v <= hi) return v; } catch (...) {}
  }
  return def;
}

// La dernière réponse devient la valeur proposée par défaut (touche Entrée) ; avec --auto elle est utilisée sans question.
static int AskInt(const char* key, const std::string& q, int def, int lo, int hi) {
  const int shown = SavedInt(key, def, lo, hi);
  if (g_auto) {
    std::cout << "[auto] " << key << " = " << shown << "\n";
    return shown;
  }
  while (true) {
    std::cout << q << " [" << shown << "] : ";
    std::string line;
    std::getline(std::cin, line);
    int v = shown;
    bool ok = true;
    if (!line.empty()) {
      try { v = std::stoi(line); } catch (...) { ok = false; }
      if (v < lo || v > hi) ok = false;
    }
    if (ok) { g_cfg[key] = std::to_string(v); return v; }
    std::cout << "  valeur invalide\n";
  }
}

static std::string AskStr(const char* key, const std::string& q, const std::string& def) {
  auto it = g_cfg.find(key);
  const std::string shown = it != g_cfg.end() ? it->second : def;
  if (g_auto) {
    std::cout << "[auto] " << key << " = " << shown << "\n";
    return shown;
  }
  std::cout << q << " [" << shown << "] : ";
  std::string line;
  std::getline(std::cin, line);
  std::string v = line.empty() ? shown : line;
  g_cfg[key] = v;
  return v;
}

// Tout ce qui s'affiche dans la console est aussi écrit dans ar6dof_probe.log (utile quand Unity lance le programme sans fenêtre).
class TeeBuf : public std::streambuf {
 public:
  TeeBuf(std::streambuf* a, std::streambuf* b) : a_(a), b_(b) {}
 protected:
  int overflow(int c) override {
    if (c == EOF) return !EOF;
    int r1 = a_->sputc((char)c), r2 = b_->sputc((char)c);
    return (r1 == EOF || r2 == EOF) ? EOF : c;
  }
  int sync() override {
    int r1 = a_->pubsync(), r2 = b_->pubsync();
    return (r1 == 0 && r2 == 0) ? 0 : -1;
  }
 private:
  std::streambuf *a_, *b_;
};

// Charge CPU de ce programme depuis le dernier appel, en % de la machine entière (tous coeurs confondus)
static double ProcCpuPercent() {
  static ULONGLONG lastWall = 0, lastCpu = 0;
  FILETIME c, e, k, u;
  if (!GetProcessTimes(GetCurrentProcess(), &c, &e, &k, &u)) return 0;
  auto ft = [](const FILETIME& f) { return ((ULONGLONG)f.dwHighDateTime << 32) | f.dwLowDateTime; };
  ULONGLONG cpu = ft(k) + ft(u);
  FILETIME now;
  GetSystemTimeAsFileTime(&now);
  ULONGLONG wall = ft(now);
  double pct = 0;
  if (lastWall != 0 && wall > lastWall) {
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    pct = 100.0 * (double)(cpu - lastCpu) / (double)(wall - lastWall) / (double)(si.dwNumberOfProcessors ? si.dwNumberOfProcessors : 1);
  }
  lastWall = wall;
  lastCpu = cpu;
  return pct;
}

static double g_qpcOffset = 0;  // QueryPerformanceCounter - NowS : permet de donner des instants "système" à Unity

// ---- Capture de la caméra dans son propre thread : le décodage et le partage vers Unity ne dépendent plus du suivi ----
struct Mailbox {
  std::mutex m;
  std::condition_variable cond;
  cv::Mat frame;   // dernière image décodée (BGR)
  uint64_t id = 0;
  double tRead = 0;     // instant d'arrivée brut
  double tSmooth = 0;   // instant d'arrivée lissé (sans gigue de décodage)
  bool failed = false;
};
static std::atomic<double> g_camFps{0.0}, g_shareMs{0.0};

static void CaptureThread(cv::VideoCapture* cap, Mailbox* mb, FrameShare* share, bool shareOn) {
  bool shareFailed = false;
  int fails = 0;
  uint64_t n = 0, frameNo = 0;
  FrameClock clk;
  auto t0 = std::chrono::steady_clock::now();
  while (g_run) {
    cv::Mat f;
    if (!cap->read(f) || f.empty()) {
      if (++fails > 100) {
        std::lock_guard<std::mutex> l(mb->m);
        mb->failed = true;
        mb->cond.notify_all();
        return;
      }
      continue;
    }
    fails = 0;
    const double tRead = NowS();  // instant de réception ; l'image a été prise un peu avant (voir camDelay)
    const double tSm = clk.Update(frameNo++, tRead);
    if (shareOn && !shareFailed) {
      if (!share->IsOpen()) {
        if (share->Open("Local\\ar6dof_frame", f))
          std::cout << "Passthrough : image partagee " << f.cols << "x" << f.rows << " (BGRA, Local\\ar6dof_frame)\n";
        else {
          shareFailed = true;
          std::cout << "Passthrough : partage impossible (image BGR 8 bits requise)\n";
        }
      }
      if (share->IsOpen() && share->Matches(f)) {
        auto s0 = std::chrono::steady_clock::now();
        share->Write(f, tSm + g_qpcOffset);
        double ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - s0).count();
        g_shareMs = 0.9 * g_shareMs.load() + 0.1 * ms;
      }
    }
    {
      std::lock_guard<std::mutex> l(mb->m);
      mb->frame = f;  // le tampon est partagé, pas copié
      mb->id++;
      mb->tRead = tRead;
      mb->tSmooth = tSm;
    }
    mb->cond.notify_one();
    ++n;
    auto now = std::chrono::steady_clock::now();
    double el = std::chrono::duration<double>(now - t0).count();
    if (el >= 1.0) { g_camFps = n / el; n = 0; t0 = now; }
  }
}

// Modèle 3D "casque" + axes + sol, dessiné avec OpenCV (pas de dépendance 3D).
static void DrawOrientation(cv::Mat& img, const Quat& q, bool ok) {
  img.setTo(cv::Scalar(32, 32, 32));
  const double focal = 560, cx = img.cols * 0.5, cy = img.rows * 0.5 + 10;
  View v = MakeView(-35, 22, 1.1);
  auto line3 = [&](Vec3 a, Vec3 b, cv::Scalar col, int th) {
    double x1, y1, x2, y2;
    if (Project(v, a, focal, cx, cy, x1, y1) && Project(v, b, focal, cx, cy, x2, y2))
      cv::line(img, cv::Point((int)x1, (int)y1), cv::Point((int)x2, (int)y2), col, th, cv::LINE_AA);
  };
  // Sol fixe (aligné sur la gravité) : repère de ce qui est "à plat"
  for (int i = -3; i <= 3; i++) {
    double t = i * 0.1;
    line3({-0.3, t, -0.14}, {0.3, t, -0.14}, cv::Scalar(70, 70, 70), 1);
    line3({t, -0.3, -0.14}, {t, 0.3, -0.14}, cv::Scalar(70, 70, 70), 1);
  }
  line3({0, 0, 0}, {0, 0, 0.3}, cv::Scalar(200, 200, 200), 1);  // verticale du monde
  if (!ok) {
    cv::putText(img, "en attente de l'IMU...", {20, 40}, cv::FONT_HERSHEY_SIMPLEX, 0.7, {0, 165, 255}, 2);
    return;
  }
  Mat3 R = ToMat3(q);
  const double hx = 0.09, hy = 0.16, hz = 0.06;
  Vec3 P[8];
  for (int i = 0; i < 8; i++) {
    Vec3 b{(i & 1) ? hx : -hx, (i & 2) ? hy : -hy, (i & 4) ? hz : -hz};
    P[i] = R * b;
  }
  const int bits[3] = {1, 2, 4};
  for (int i = 0; i < 8; i++)
    for (int bit : bits) {
      if (i & bit) continue;
      int j = i | bit;
      bool front = (bit != 1) && (i & 1);  // arête de la face avant (x = +hx)
      line3(P[i], P[j], front ? cv::Scalar(0, 220, 255) : cv::Scalar(200, 200, 200), front ? 3 : 1);
    }
  // Deux "yeux" sur la face avant
  for (int s = -1; s <= 1; s += 2) {
    double x, y;
    if (Project(v, R * Vec3{hx, 0.07 * s, 0}, focal, cx, cy, x, y))
      cv::circle(img, cv::Point((int)x, (int)y), 7, cv::Scalar(0, 220, 255), 2, cv::LINE_AA);
  }
  // Axes du casque
  struct Ax { Vec3 d; cv::Scalar c; const char* name; };
  Ax axes[3] = {{{0.25, 0, 0}, cv::Scalar(0, 0, 255), "X avant"},
                {{0, 0.22, 0}, cv::Scalar(0, 200, 0), "Y gauche"},
                {{0, 0, 0.22}, cv::Scalar(255, 120, 0), "Z haut"}};
  for (auto& a : axes) {
    Vec3 tip = R * a.d;
    line3({0, 0, 0}, tip, a.c, 3);
    double x, y;
    if (Project(v, tip * 1.1, focal, cx, cy, x, y))
      cv::putText(img, a.name, cv::Point((int)x + 4, (int)y), cv::FONT_HERSHEY_SIMPLEX, 0.55, a.c, 1, cv::LINE_AA);
  }
}

static Mat3 FromCv(const cv::Matx33d& a) {
  Mat3 r;
  for (int i = 0; i < 3; i++)
    for (int j = 0; j < 3; j++) r.m[i][j] = a(i, j);
  return r;
}
static cv::Matx33d ToCv(const Mat3& a) {
  cv::Matx33d r;
  for (int i = 0; i < 3; i++)
    for (int j = 0; j < 3; j++) r(i, j) = a.m[i][j];
  return r;
}
static void ToGray(const cv::Mat& src, cv::Mat& dst) {
  if (src.channels() == 3) cv::cvtColor(src, dst, cv::COLOR_BGR2GRAY);
  else if (src.channels() == 4) cv::cvtColor(src, dst, cv::COLOR_BGRA2GRAY);
  else dst = src;
}

static double LoadBaseline() {
  std::ifstream f(ExeDir() + "\\baseline_mm.txt");
  double v = 0;
  if (!(f >> v) || v < 0) return 0;
  return v;
}
static void SaveBaseline(double v) {
  std::ofstream f(ExeDir() + "\\baseline_mm.txt");
  f << v;
}

static bool OpenCam(cv::VideoCapture& cap, int idx, int api, int W, int H, int fps) {
  cap.release();
  if (!cap.open(idx, api)) return false;
  cap.set(cv::CAP_PROP_FOURCC, cv::VideoWriter::fourcc('M', 'J', 'P', 'G'));
  cap.set(cv::CAP_PROP_FRAME_WIDTH, W);
  cap.set(cv::CAP_PROP_FRAME_HEIGHT, H);
  cap.set(cv::CAP_PROP_FPS, fps);  // sans cette ligne Windows choisit souvent 30 fps
  return cap.isOpened();
}

// Cadence réellement reçue pendant `seconds` (après quelques images de chauffe).
static double MeasureFps(cv::VideoCapture& cap, double seconds) {
  cv::Mat f;
  int fail = 0, n = 0;
  for (int i = 0; i < 5; i++) cap.read(f);
  auto t0 = std::chrono::steady_clock::now();
  double el = 0;
  while (el < seconds) {
    if (!cap.read(f) || f.empty()) {
      if (++fail > 50) break;
      continue;
    }
    ++n;
    el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
  }
  return el > 0 ? n / el : 0;
}

static std::string Lab(double v) {
  char b[32];
  if (std::abs(v) < 1e-9) return "0";
  if (std::abs(v) < 0.995) std::snprintf(b, sizeof(b), "%+d cm", (int)std::lround(v * 100));
  else std::snprintf(b, sizeof(b), "%+.1f m", v);
  return b;
}

// Trajectoire : vue de dessus (gauche) et de côté (droite), monde G, avec GRILLE GRADUÉE.
// zoom = pixels par mètre (0 = automatique). Le repère (m) est tracé en magenta.
static void DrawTrajectory(cv::Mat& img, const std::vector<Vec3>& trail, const Vec3& pos, const Mat3& R, bool valid,
                           double zoom, bool haveMark, const Vec3& mark, double* outPxPerM,
                           bool floorValid, double floorZ) {
  img.setTo(cv::Scalar(32, 32, 32));
  const int pw = img.cols / 2, top0 = 92, bot = img.rows - 76;
  double sc = zoom;
  if (sc <= 0) {
    double ext = 0.3;
    for (const auto& p : trail)
      ext = std::max(ext, std::max(std::abs(p.x), std::max(std::abs(p.y), std::abs(p.z))));
    sc = 120.0 / ext;
  }
  if (outPxPerM) *outPxPerM = sc;
  const cv::Rect ra(4, top0, pw - 8, bot - top0), rb(pw + 4, top0, pw - 8, bot - top0);
  cv::Mat A = img(ra), Bm = img(rb);  // dessiner dans ces vues = découpage automatique
  A.setTo(cv::Scalar(38, 38, 38));
  Bm.setTo(cv::Scalar(38, 38, 38));
  const cv::Point c1(ra.width / 2, ra.height / 2), c2(rb.width / 2, rb.height / 2);

  const double steps[9] = {0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0};
  double step = steps[8];
  for (int i = 0; i < 9; i++)
    if (steps[i] * sc >= 40.0) { step = steps[i]; break; }
  const double gp = step * sc;  // pixels entre deux lignes de grille
  auto grid = [&](cv::Mat& M, const cv::Point& c, bool topView) {
    int nx = (int)(M.cols / 2 / gp) + 1, ny = (int)(M.rows / 2 / gp) + 1;
    for (int i = -nx; i <= nx; i++) {
      int x = c.x + (int)std::lround(i * gp);
      cv::line(M, cv::Point(x, 0), cv::Point(x, M.rows), i == 0 ? cv::Scalar(100, 100, 100) : cv::Scalar(58, 58, 58), 1);
      double v = topView ? -i * step : i * step;  // dessus : y (gauche +) ; côté : x (avant +)
      cv::putText(M, Lab(v), cv::Point(x + 2, M.rows - 4), cv::FONT_HERSHEY_SIMPLEX, 0.32, cv::Scalar(150, 150, 150), 1, cv::LINE_AA);
    }
    for (int j = -ny; j <= ny; j++) {
      int y = c.y + (int)std::lround(j * gp);
      cv::line(M, cv::Point(0, y), cv::Point(M.cols, y), j == 0 ? cv::Scalar(100, 100, 100) : cv::Scalar(58, 58, 58), 1);
      double v = -j * step;  // dessus : x (avant +) ; côté : z (haut +)
      cv::putText(M, Lab(v), cv::Point(2, y - 2), cv::FONT_HERSHEY_SIMPLEX, 0.32, cv::Scalar(150, 150, 150), 1, cv::LINE_AA);
    }
  };
  grid(A, c1, true);
  grid(Bm, c2, false);
  cv::rectangle(img, ra, cv::Scalar(90, 90, 90), 1);
  cv::rectangle(img, rb, cv::Scalar(90, 90, 90), 1);
  cv::putText(img, "dessus : avant en haut, gauche a gauche", cv::Point(8, top0 - 6), cv::FONT_HERSHEY_SIMPLEX, 0.4, cv::Scalar(170, 170, 170), 1, cv::LINE_AA);
  cv::putText(img, "cote : avant a droite, haut en haut", cv::Point(pw + 8, top0 - 6), cv::FONT_HERSHEY_SIMPLEX, 0.4, cv::Scalar(170, 170, 170), 1, cv::LINE_AA);
  char gt[64];
  if (step < 0.995) std::snprintf(gt, sizeof(gt), "1 carreau = %d cm", (int)std::lround(step * 100));
  else std::snprintf(gt, sizeof(gt), "1 carreau = %.0f m", step);
  cv::putText(img, gt, cv::Point(8, bot + 16), cv::FONT_HERSHEY_SIMPLEX, 0.45, cv::Scalar(0, 255, 255), 1, cv::LINE_AA);

  auto top = [&](const Vec3& p) { return cv::Point((int)(c1.x - p.y * sc), (int)(c1.y - p.x * sc)); };
  auto side = [&](const Vec3& p) { return cv::Point((int)(c2.x + p.x * sc), (int)(c2.y - p.z * sc)); };
  for (size_t i = 1; i < trail.size(); i++) {
    cv::line(A, top(trail[i - 1]), top(trail[i]), cv::Scalar(0, 200, 255), 1, cv::LINE_AA);
    cv::line(Bm, side(trail[i - 1]), side(trail[i]), cv::Scalar(0, 200, 255), 1, cv::LINE_AA);
  }
  if (floorValid) {  // le sol, dans la vue de côté
    int yf = c2.y - (int)(floorZ * sc);
    cv::line(Bm, cv::Point(0, yf), cv::Point(Bm.cols, yf), cv::Scalar(90, 220, 90), 2, cv::LINE_AA);
    cv::putText(Bm, "sol", cv::Point(Bm.cols - 34, yf - 4), cv::FONT_HERSHEY_SIMPLEX, 0.45, cv::Scalar(90, 220, 90), 1, cv::LINE_AA);
  }
  if (haveMark) {
    cv::circle(A, top(mark), 7, cv::Scalar(255, 0, 255), 2, cv::LINE_AA);
    cv::circle(Bm, side(mark), 7, cv::Scalar(255, 0, 255), 2, cv::LINE_AA);
    if (valid) {
      cv::line(A, top(mark), top(pos), cv::Scalar(255, 0, 255), 1, cv::LINE_AA);
      cv::line(Bm, side(mark), side(pos), cv::Scalar(255, 0, 255), 1, cv::LINE_AA);
    }
  }
  if (!valid) return;
  Vec3 fwd = R * Vec3{1, 0, 0};  // avant du casque dans le monde
  cv::circle(A, top(pos), 5, cv::Scalar(0, 255, 0), -1, cv::LINE_AA);
  cv::line(A, top(pos), top(pos + fwd * (25.0 / sc)), cv::Scalar(0, 0, 255), 2, cv::LINE_AA);
  cv::circle(Bm, side(pos), 5, cv::Scalar(0, 255, 0), -1, cv::LINE_AA);
  cv::line(Bm, side(pos), side(pos + fwd * (25.0 / sc)), cv::Scalar(0, 0, 255), 2, cv::LINE_AA);
}

int main(int argc, char** argv) {
  bool noWindowsFlag = false, noBle = false;
  std::ofstream logFile(ExeDir() + "\\ar6dof_probe.log", std::ios::trunc);
  TeeBuf tee(std::cout.rdbuf(), logFile.rdbuf());
  std::streambuf* origCout = std::cout.rdbuf(&tee);
  std::cout << std::unitbuf;   // chaque ligne est écrite tout de suite (console ET fichier)
  struct CoutRestore { std::streambuf* o; ~CoutRestore() { std::cout.rdbuf(o); } } coutRestore{origCout};
  g_qpcOffset = QpcSeconds() - NowS();
  for (int i = 1; i < argc; i++) {
    std::string a = argv[i];
    if (a == "--auto") g_auto = true;                 // rejoue les réponses de la dernière fois, sans question
    else if (a == "--no-ble") noBle = true;            // ne pas ecouter la telecommande Bluetooth
    else if (a == "--no-windows") noWindowsFlag = true;  // aucune fenêtre OpenCV : bien moins de CPU
  }
  LoadConfig();   // les réponses précédentes servent de valeurs par défaut, même sans --auto
  if (g_auto && g_cfg.empty()) std::cout << "[auto] aucune configuration trouvee (" << ConfigPath() << ") : lance d'abord ar6dof_probe.exe SANS --auto.\n";
  std::cout << "ar6dof_probe " << (g_auto ? "(--auto) " : "") << "- config : " << ConfigPath() << "\n";
  // Télécommande Bluetooth -> UDP local (Unity). Se reconnecte seule ; --no-ble pour la désactiver.
  struct BleStop { ~BleStop() { ar6ble::Stop(); } } bleStop;
  // Sans ça, l'ouverture d'une caméra avec le backend MSMF peut prendre
  // plusieurs dizaines de secondes.
  _putenv_s("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");

  std::cout << "=== ar6dof_probe ===\n\nCameras :\n";
  auto cams = ListCameras();
  if (cams.empty()) { std::cout << "  aucune camera trouvee\n"; return 1; }
  for (size_t i = 0; i < cams.size(); i++) std::cout << "  [" << i << "] " << cams[i] << "\n";
  int ci = AskInt("cam", "Camera stereo", 0, 0, (int)cams.size() - 1);

  std::cout << "\nPorts serie (IMU) :\n";
  auto ports = ListComPorts();
  for (size_t i = 0; i < ports.size(); i++) std::cout << "  [" << i << "] " << ports[i] << "\n";
  std::cout << "  [-1] pas d'IMU\n";
  int pi = -1;
  if (g_auto) {   // les numéros de port COM peuvent changer : on retrouve le port par son nom
    auto it = g_cfg.find("portname");
    if (it != g_cfg.end())
      for (size_t i = 0; i < ports.size(); i++) if (ports[i] == it->second) pi = (int)i;
    std::cout << "[auto] IMU = " << (pi >= 0 ? ports[pi] : std::string("aucune")) << "\n";
  } else {
    int defPort = ports.empty() ? -1 : 0;   // le port est retrouvé par son NOM (les numéros COM peuvent changer)
    auto sp = g_cfg.find("portname");
    if (sp != g_cfg.end())
      for (size_t i = 0; i < ports.size(); i++) if (ports[i] == sp->second) defPort = (int)i;
    g_cfg.erase("portidx");
    pi = AskInt("portidx", "Port IMU", defPort, -1, (int)ports.size() - 1);
    g_cfg["portname"] = pi >= 0 ? ports[pi] : "";
  }

  std::cout << "\nModes MJPEG de ta ELP : 3200x1200@60 | 2560x720@60 (16:9, incompatible avec ta calibration)\n"
               "                       1600x600@120 | 1280x480@120 | 640x240@120\n"
               "Image cote a cote (gauche|droite) : largeur TOTALE des 2 yeux, hauteur, cadence.\n";
  int W = AskInt("width", "Largeur totale", 1600, 320, 8000);
  int H = AskInt("height", "Hauteur", 600, 240, 4000);
  int FPS = AskInt("fps", "Cadence demandee (fps)", 120, 5, 240);
  int udpPort = AskInt("udp", "Port UDP vers Unity sur ce PC (0 = desactive)", 9876, 0, 65535);
  int shareFrames = AskInt("share", "Partager l'image de la camera avec Unity pour le passthrough (1 = oui, 0 = non)", 1, 0, 1);
  int showWindows = AskInt("windows", "Afficher les fenetres de controle (1 = oui, 0 = non : plus leger)", 1, 0, 1);
  int autoTrack = AskInt("autotrack", "Demarrer le tracking automatiquement (1 = oui, 0 = non)", 0, 0, 1);
  const bool windowsOn = showWindows && !noWindowsFlag;

  // Calibration faite ailleurs (RTAB-Map) : on la charge, on ne la fabrique pas.
  StereoCalib calib;
  bool haveCalib = false;
  std::string calibErr = "aucun dossier de calibration";
  std::string dir = AskStr("calibdir", "\nDossier de calibration RTAB-Map ('-' = aucune)", ExeDir() + "\\calibration");
  if (dir != "-") {
    if (LoadRtabmapStereo(dir, calib, calibErr)) {
      haveCalib = true;
      std::cout << "Calibration chargee (" << calib.files << "), faite en "
                << calib.size.width << "x" << calib.size.height << " par oeil.\n";
      // L'echelle metrique d'une calibration depend de la taille des cases de la mire saisie
      // pendant la calibration. On peut la corriger avec la distance REELLE entre les 2 objectifs.
      double calibBase = cv::norm(calib.T) * 1000.0;
      double saved = LoadBaseline();
      char defbuf[32];
      std::snprintf(defbuf, sizeof(defbuf), "%.1f", saved);
      std::cout << "Base entre les cameras dans la calibration : " << calibBase << " mm\n";
      std::string a = AskStr("baseline", "Base REELLE entre les centres des 2 objectifs, en mm (0 = garder celle de la calibration)", defbuf);
      double real = 0;
      try { real = std::stod(a); } catch (...) { real = 0; }
      SaveBaseline(real);
      if (real > 1.0 && calibBase > 1.0) {
        calib.T = calib.T * (real / calibBase);
        std::cout << "Echelle corrigee : x" << real / calibBase << " (base utilisee : " << real << " mm)\n";
      }
      if (ExportCameraModel(calib, ExeDir() + "\\camera_model.json"))
        std::cout << "Modele de camera exporte pour Unity : " << ExeDir() << "\\camera_model.json\n";
    } else {
      std::cout << "Calibration NON chargee : " << calibErr << "\n";
    }
  }

  // Ouverture de la caméra avec la cadence demandée. On MESURE la cadence reçue ;
  // si elle est trop basse avec un backend, on essaie l'autre (MSMF / DirectShow).
  cv::VideoCapture cap;
  {
    const int apis[2] = {cv::CAP_MSMF, cv::CAP_DSHOW};
    const char* names[2] = {"MSMF", "DSHOW"};
    int first = AskInt("backend", "Backend camera (0 = MSMF, 1 = DSHOW ; l'autre est essaye si la cadence est trop basse)", 0, 0, 1);
    // La télécommande Bluetooth démarre APRÈS toutes les questions (ses messages ne se mélangent plus aux réponses)
    if (!noBle) ar6ble::Start(9878, [](const std::string& m) { std::cout << m << std::endl; });
    double best = -1;
    int bestIdx = -1, openIdx = -1;
    for (int attempt = 0; attempt < 2; attempt++) {
      int bi = (first + attempt) % 2;
      openIdx = -1;
      if (!OpenCam(cap, ci, apis[bi], W, H, FPS)) {
        std::cout << "  " << names[bi] << " : ouverture impossible\n";
        continue;
      }
      openIdx = bi;
      double f = MeasureFps(cap, 1.5);
      std::cout << "  " << names[bi] << " : " << cap.get(cv::CAP_PROP_FRAME_WIDTH) << "x"
                << cap.get(cv::CAP_PROP_FRAME_HEIGHT) << ", " << f << " fps mesures (sans traitement)\n";
      if (f > best) { best = f; bestIdx = bi; }
      if (f >= 0.85 * FPS) break;
    }
    if (bestIdx < 0) { std::cout << "Impossible d'ouvrir la camera.\n"; return 1; }
    if (openIdx != bestIdx && !OpenCam(cap, ci, apis[bestIdx], W, H, FPS)) {
      std::cout << "Impossible de rouvrir la camera.\n";
      return 1;
    }
    std::cout << "Backend retenu : " << names[bestIdx] << " (" << best << " fps)\n";
    if (best < 0.85 * FPS)
      std::cout << "ATTENTION : cadence inferieure a la demande. Verifie le mode (resolution/fps), le port USB 3,\n"
                   "et que la piece est assez eclairee (en basse lumiere la camera allonge l'exposition et baisse sa cadence).\n";
  }
  std::cout << "Resolution negociee : " << cap.get(cv::CAP_PROP_FRAME_WIDTH) << "x"
            << cap.get(cv::CAP_PROP_FRAME_HEIGHT)
            << "  (si differente de ce que tu as demande, ce mode n'existe pas)\n";

  ImuState imu;
  imu.mount = LoadMount();
  std::thread th;

  std::filesystem::create_directories("capture");
  int saved = 0, frames = 0, fails = 0;
  double fps = 0;
  auto tf = std::chrono::steady_clock::now();
  cv::Mat frame, orient(520, 640, CV_8UC3);
  Rectifier rect;
  bool rectReady = false, showRect = haveCalib, swapEyes = false;
  std::string checkMsg = "touche 'c' : mesurer la qualite de la calibration (scene riche en details)";
  std::string mountMsg = "montage : g (a plat, a l'endroit) puis f (penche vers l'avant)";
  bool havePendingUp = false;
  Vec3 pendingUp;

  // ---- Tracking 6DoF ----
  bool tracking = false, usePrior = true, trackReady = false, haveLast = false;
  double camDelay = 0.030, predS = 0.015, voMs = 0, lastFrameT = 0, snapT_s = 0;
  DelayEstimator delayEst;   // délai caméra mesuré en comparant la rotation vue par la caméra à celle du gyro
  bool autoDelay = true, havePrevOk = false;
  double prevArr = 0, lastDelayEst = 0, delayQuality = 0;
  Mat3 prevRvc;
  cv::Size wk;
  cv::Matx33d Kt;
  double Bt = 0;
  Rectifier rectT;
  std::unique_ptr<StereoVO> vo;
  const Mat3 Rcb = CamFromBody();
  Mat3 R_GV, snapRvc, poseR;
  Vec3 snapT, snapV, poseP;
  bool poseValid = false;
  std::vector<Vec3> trail;
  double lastDispS = 0, pxPerM = 0;
  cv::Size smallFor;
  const double zoomList[6] = {0, 100, 200, 400, 800, 1600};  // 0 = auto
  int zoomIdx = 3;
  bool haveMark = false;
  Vec3 mark;
  std::string trackMsg = "touche t : demarrer le tracking 6DoF (fais d'abord g puis f dans la fenetre orientation)";
  cv::Mat traj(520, 640, CV_8UC3);
  // ---- Sol + sortie vers Unity ----
  UdpSender udp;
  if (udpPort > 0) {
    if (udp.Open("127.0.0.1", udpPort)) std::cout << "Sortie pose -> Unity : UDP 127.0.0.1:" << udpPort << "\n";
    else std::cout << "Sortie UDP impossible (port " << udpPort << ")\n";
  }
  uint32_t udpSeq = 0;
  FrameShare share;
  bool shareFailed = false;
  VisionSnap vsnap;
  if (pi >= 0) th = std::thread(ImuThread, ports[pi], &imu, &vsnap, &udp);   // publie la pose à la cadence de l'IMU
  FloorFilter floorF;
  bool floorValid = false, floorLocked = false;
  double floorZ = 0, lastFloorT = 0;
  if (!g_auto) SaveConfig();   // les réponses servent de configuration pour --auto
  UdpControl ctl;
  if (!ctl.Open(9877)) std::cout << "Canal de commande (UDP 9877) indisponible.\n";
  int injectKey = (autoTrack && haveCalib) ? 't' : -1;   // démarre le tracking dès la première image
  Mailbox mb;
  uint64_t lastId = 0;
  std::thread capThread(CaptureThread, &cap, &mb, &share, shareFrames != 0);
  std::cout << (windowsOn ? "\nFenetres ouvertes. Aide des touches : voir l'en-tete du programme / README.\n"
                          : "\nMode sans fenetres : commandes par UDP 9877 (T recentrer, V sol, Z yaw, Q quitter).\n");

  while (true) {
    double tRead = 0, tSm = 0;
    {
      std::unique_lock<std::mutex> l(mb.m);
      mb.cond.wait_for(l, std::chrono::milliseconds(30), [&] { return mb.id != lastId || mb.failed; });
      if (mb.failed) { std::cout << "Plus d'images de la camera.\n"; break; }
      if (mb.id == lastId) {   // pas de nouvelle image : on reste réactif aux touches et aux commandes
        l.unlock();
        int kk = windowsOn ? cv::waitKey(1) : -1;
        int cc = ctl.Poll();
        if (kk == 'q' || kk == 27 || cc == 'Q') break;
        continue;
      }
      frame = mb.frame;   // partage du tampon, aucune copie de pixels
      tRead = mb.tRead;
      tSm = mb.tSmooth;
      lastId = mb.id;
    }
    ++frames;
    const bool displayDue = (tRead - lastDispS) >= 0.033;  // affichage ~30 Hz max, tracking à pleine cadence
    auto now = std::chrono::steady_clock::now();
    double dt = std::chrono::duration<double>(now - tf).count();
    if (dt >= 1.0) {
      fps = frames / dt; frames = 0; tf = now;
      if (!windowsOn) {  // sans fenêtre : un résumé par seconde dans la console et le journal
        char sb[256];
        std::snprintf(sb, sizeof(sb), "camera %.1f fps | suivi %.1f fps | partage %.1f ms | tracking %s | %d points | delai camera %.1f ms (%s) | cpu %.0f%%\n",
                      g_camFps.load(), fps, g_shareMs.load(),
                      tracking ? (vo && vo->state == StereoVO::kOk ? "OK" : "perdu/init") : "arrete", vo ? vo->nPoints() : 0,
                      camDelay * 1000.0, autoDelay ? "auto" : "manuel", ProcCpuPercent());
        std::cout << sb;
      }
    }

    int w = frame.cols / 2;
    cv::Mat left = frame(cv::Rect(0, 0, w, frame.rows));
    cv::Mat right = frame(cv::Rect(w, 0, w, frame.rows));
    if (swapEyes) std::swap(left, right);

    // (Re)construire la rectification si besoin (taille réelle de l'image)
    if (haveCalib && (!rectReady || rect.eye != cv::Size(w, frame.rows))) {
      std::string err;
      rectReady = MakeRectifier(calib, cv::Size(w, frame.rows), rect, err);
      if (!rectReady) {
        calibErr = err;
        std::cout << "Rectification impossible : " << err << "\n";
        haveCalib = false;
        showRect = false;
      }
    }
    // Copie des données IMU (un seul verrouillage)
    Vec3 acc, gyr;
    Quat q;
    bool qok = false, portErr = false;
    double rA = 0, rG = 0;
    uint64_t bytes = 0, okN = 0, badN = 0;
    Mat3 mount;
    {
      std::lock_guard<std::mutex> l(imu.m);
      acc = imu.acc_s; gyr = imu.gyr_s; q = imu.q; qok = imu.q_ok; portErr = imu.port_error;
      rA = imu.rate_acc; rG = imu.rate_gyr; bytes = imu.bytes; okN = imu.ok; badN = imu.bad;
      mount = imu.mount;
    }

    // ---- Paire rectifiée de travail (<= 800 px par oeil) : tracking + affichage rectifié ----
    cv::Mat tl, tr;
    const bool needSmall = haveCalib && (tracking || (showRect && displayDue && windowsOn));
    if (needSmall) {
      const cv::Size want(w, frame.rows);
      if (!trackReady || smallFor != want) {
        double ws = std::min(1.0, 800.0 / w);
        wk = cv::Size(cvRound(w * ws), cvRound(frame.rows * ws));
        std::string e1;
        trackReady = MakeRectifier(calib, wk, rectT, e1) && RectifiedIntrinsics(calib, wk, Kt, Bt);
        smallFor = want;
        if (trackReady) {
          vo.reset(new StereoVO(Kt, Bt));
          haveLast = false;
        } else {
          trackMsg = "rectification de travail impossible : " + (e1.empty() ? std::string("calibration incompatible") : e1);
          tracking = false;
          showRect = false;
        }
      }
      if (trackReady) {
        cv::Mat gl, gr, sl, sr;
        ToGray(left, gl);
        ToGray(right, gr);
        if (wk != gl.size()) {
          cv::resize(gl, sl, wk, 0, 0, cv::INTER_AREA);
          cv::resize(gr, sr, wk, 0, 0, cv::INTER_AREA);
        } else {
          sl = gl;
          sr = gr;
        }
        cv::remap(sl, tl, rectT.mapL1, rectT.mapL2, cv::INTER_LINEAR);
        cv::remap(sr, tr, rectT.mapR1, rectT.mapR2, cv::INTER_LINEAR);
      }
    }
    // ---- Tracking 6DoF ----
    if (tracking && haveCalib) {
      if (trackReady && vo && !tl.empty()) {
        const double tcap = tSm - camDelay;   // instant de prise de vue = arrivée lissée - délai
        if (!haveLast) {
          // Première image : le monde est aligné sur la gravité (haut = Z) grace a l'IMU
          Vec3 upc = {0, -1, 0};
          if (qok && pi >= 0) upc = Rcb * ToMat3(q).Row(2);
          R_GV = GravityAlign(upc);
          vo->Reset();
          vo->Process(tl, tr, nullptr, 0.033);
          haveLast = true;
          lastFrameT = tcap;
          trail.clear();
        } else {
          double dt = std::max(0.001, std::min(0.2, tcap - lastFrameT));
          cv::Matx33d dRc;
          const cv::Matx33d* pr = nullptr;
          if (usePrior && pi >= 0) {
            Mat3 dRb;
            {
              std::lock_guard<std::mutex> l(imu.m);
              dRb = IntegrateGyro(imu.gyroBuf, lastFrameT, tcap);
            }
            dRc = ToCv(Rcb * dRb * Rcb.T());
            pr = &dRc;
          }
          auto tv0 = std::chrono::steady_clock::now();
          vo->Process(tl, tr, pr, dt);
          voMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - tv0).count();
          lastFrameT = tcap;
        }
        snapRvc = FromCv(vo->R);
        // Mesure du délai : rotation vue par la caméra entre deux images, comparée à l'intégrale du gyro
        if (vo->state == StereoVO::kOk) {
          if (havePrevOk && tSm - prevArr < 0.1) delayEst.AddStep(prevArr, tSm, prevRvc.T() * snapRvc);
          prevArr = tSm;
          prevRvc = snapRvc;
          havePrevOk = true;
        } else {
          havePrevOk = false;
        }
        if (autoDelay && pi >= 0 && tRead - lastDelayEst > 0.5) {
          lastDelayEst = tRead;
          std::deque<GyroSample> gcopy;
          { std::lock_guard<std::mutex> l(imu.m); gcopy = imu.gyroBuf; }   // copie rapide, le calcul se fait hors verrou
          double dEst = 0, qEst = 0;
          if (delayEst.Estimate(gcopy, Rcb, dEst, qEst)) { camDelay = 0.8 * camDelay + 0.2 * dEst; delayQuality = qEst; }
        }
        snapT = Vec3{vo->t[0], vo->t[1], vo->t[2]};
        snapV = Vec3{vo->v[0], vo->v[1], vo->v[2]};
        snapT_s = tcap;
      }
    }
    // Pose "maintenant + prédiction". L'envoi à Unity est fait par le thread IMU (cadence de l'IMU, ~200 Hz) :
    // ici on met seulement à jour l'état partagé (une fois par image) et on calcule la pose à afficher.
    if (haveLast) {
      uint16_t fl = 0;
      if (tracking && vo) {
        if (vo->state == StereoVO::kOk) fl |= kFlagTracking;
        if (vo->state == StereoVO::kLost) fl |= kFlagLost;
      }
      if (floorValid) fl |= kFlagFloor;
      if (floorLocked) fl |= kFlagFloorLocked;
      UpdateSnap(vsnap, snapRvc, snapT, snapV, snapT_s, NowS(), R_GV, fl, floorValid, floorZ, predS, camDelay);
      PoseOut po;
      {
        SnapData sd;
        { std::lock_guard<std::mutex> l(vsnap.m); sd = vsnap.d; }
        std::lock_guard<std::mutex> l(imu.m);
        po = ComputePose(sd, imu.gyroBuf, NowS() + predS, Rcb);
      }
      poseP = po.p;
      poseR = po.R;
      poseValid = true;
      if (tracking && vo && vo->state == StereoVO::kOk) {
        trail.push_back(poseP);
        if (trail.size() > 1500) trail.erase(trail.begin());
      }
      // Sol : plus bas plan horizontal dense parmi les points 3D suivis (estimé toutes les 0,5 s)
      if (tracking && vo && vo->state == StereoVO::kOk && !floorLocked && (tRead - lastFloorT) > 0.5) {
        lastFloorT = tRead;
        std::vector<double> zs;
        const auto& pts = vo->Points3D();
        zs.reserve(pts.size());
        for (const auto& pp : pts) zs.push_back(R_GV.m[2][0] * pp.x + R_GV.m[2][1] * pp.y + R_GV.m[2][2] * pp.z);
        floorF.Add(EstimateFloor(zs, poseP.z));
        floorValid = floorF.Valid();
        if (floorValid) floorZ = floorF.Z();
      }
      // Sans IMU il n'y a pas de thread de publication : on envoie à la cadence de la caméra
      if (pi < 0 && udp.IsOpen() && tracking && vo) {
        auto pkt = PackPose(fl, udpSeq++, po.p, QuatFromMat3(po.R), po.vG, po.wG, floorZ, QpcSeconds() + predS, (float)camDelay);
        udp.Send(pkt.data(), (int)pkt.size());
      }
    }

    if (displayDue && windowsOn) {
    lastDispS = tRead;
    cv::Mat shown;
    if (showRect && !tl.empty()) cv::hconcat(tl, tr, shown);
    else cv::hconcat(left, right, shown);

    double scale = std::min(1.0, 1600.0 / shown.cols);
    cv::Mat disp;
    cv::resize(shown, disp, cv::Size(), scale, scale);
    if (disp.channels() == 1) cv::cvtColor(disp, disp, cv::COLOR_GRAY2BGR);
    for (int y = 40; y < disp.rows; y += 60)
      cv::line(disp, {0, y}, {disp.cols, y}, {0, 255, 0}, 1);
    cv::line(disp, {disp.cols / 2, 0}, {disp.cols / 2, disp.rows}, {0, 0, 255}, 2);


    char line[256];
    int y = 24;
    auto put = [&](const std::string& s, cv::Scalar c = {255, 255, 255}) {
      cv::putText(disp, s, {10, y}, cv::FONT_HERSHEY_SIMPLEX, 0.6, {0, 0, 0}, 3);
      cv::putText(disp, s, {10, y}, cv::FONT_HERSHEY_SIMPLEX, 0.6, c, 1);
      y += 24;
    };
    std::snprintf(line, sizeof(line), "cam %dx%d | camera %.1f fps | suivi %.1f fps | partage %.1f ms | paires: %d  vue: %s%s",
                  frame.cols, frame.rows, g_camFps.load(), fps, g_shareMs.load(), saved,
                  (showRect && !tl.empty()) ? "RECTIFIEE" : "brute", swapEyes ? "  [G/D inverses]" : "");
    put(line);
    if (haveCalib && rectReady) {
      std::snprintf(line, sizeof(line), "calib: f=%.0f px  base=%.1f mm  champ ~%.0f deg", rect.f,
                    rect.baseline_mm, rect.hfov_deg);
      put(line);
      put(checkMsg, {0, 255, 255});
      for (size_t i = 0; i < rect.warnings.size() && i < 5; i++)
        put("! " + rect.warnings[i], {0, 165, 255});
    } else {
      put("pas de calibration : " + calibErr, {0, 165, 255});
    }
    if (pi < 0) {
      put("IMU: non selectionnee");
    } else if (portErr) {
      put("IMU: erreur d'ouverture/lecture du port", {0, 0, 255});
    } else if (bytes > 0 && okN == 0) {
      put("IMU: octets recus mais 0 paquet valide (firmware v2 flashe ?)", {0, 0, 255});
    } else {
      double na = Norm(acc);
      std::snprintf(line, sizeof(line), "IMU accel %.1f Hz  |a|=%.2f m/s2   gyro %.1f Hz   paquets ok=%llu ko=%llu",
                    rA, na, rG, (unsigned long long)okN, (unsigned long long)badN);
      put(line);
    }
    cv::imshow("ar6dof_probe", disp);

    // ---- Fenêtre orientation ----
    DrawOrientation(orient, q, qok && pi >= 0);
    {
      int yy = 500;
      auto put2 = [&](const std::string& s, cv::Scalar c = {255, 255, 255}, int ypos = -1) {
        int yp = ypos >= 0 ? ypos : yy;
        cv::putText(orient, s, {10, yp}, cv::FONT_HERSHEY_SIMPLEX, 0.45, c, 1, cv::LINE_AA);
      };
      if (qok && pi >= 0) {
        double r, p, ya;
        ToEuler(q, r, p, ya);
        const double k = 180.0 / 3.14159265358979;
        std::snprintf(line, sizeof(line), "roll %+6.1f  pitch %+6.1f  yaw %+6.1f deg", r * k, p * k, ya * k);
        put2(line, {255, 255, 255}, 24);
        put2("pitch>0: regarde en bas | roll>0: penche a droite | yaw>0: tourne a gauche (derive, pas de boussole)",
             {170, 170, 170}, 44);
      }
      put2("montage: avant=" + AxisName(mount.Row(0)) + "  gauche=" + AxisName(mount.Row(1)) +
               "  haut=" + AxisName(mount.Row(2)),
           {0, 255, 255}, 470);
      put2(mountMsg, {0, 255, 255}, 488);
      put2("g/f calibrer | 1-6 tourner de 90 deg | 0 reset montage | z yaw=0", {170, 170, 170}, 508);
    }
    cv::imshow("orientation", orient);

    // ---- Fenêtre trajectoire ----
    DrawTrajectory(traj, trail, poseP, poseR, poseValid && haveLast, zoomList[zoomIdx], haveMark, mark, &pxPerM,
                   floorValid, floorZ);
    {
      char tl[256];
      if (poseValid && haveLast) {
        double r, p, ya;
        MatToEuler(poseR, r, p, ya);
        const double kk = 180.0 / 3.14159265358979;
        std::snprintf(tl, sizeof(tl), "x %+.3f  y %+.3f  z %+.3f m   roll %+.1f pitch %+.1f yaw %+.1f deg",
                      poseP.x, poseP.y, poseP.z, r * kk, p * kk, ya * kk);
        cv::putText(traj, tl, cv::Point(10, 22), cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(255, 255, 255), 1, cv::LINE_AA);
        const char* stn = !vo ? "-" : vo->state == StereoVO::kOk ? "OK" : vo->state == StereoVO::kLost ? "PERDU" : "INIT";
        std::snprintf(tl, sizeof(tl), "tracking %s | points %d (inliers %d) | calcul %.1f ms | prior IMU: %s",
                      stn, vo ? vo->nPoints() : 0, vo ? vo->nInliers : 0, voMs, usePrior ? "oui" : "non");
        cv::putText(traj, tl, cv::Point(10, 44), cv::FONT_HERSHEY_SIMPLEX, 0.45,
                    (vo && vo->state == StereoVO::kLost) ? cv::Scalar(0, 0, 255) : cv::Scalar(0, 255, 255), 1, cv::LINE_AA);
      } else {
        cv::putText(traj, trackMsg, cv::Point(10, 22), cv::FONT_HERSHEY_SIMPLEX, 0.45, cv::Scalar(0, 255, 255), 1, cv::LINE_AA);
      }
      if (poseValid && haveLast) {
        double d0 = Norm(poseP) * 100.0;
        if (haveMark) {
          Vec3 dd = poseP - mark;
          std::snprintf(tl, sizeof(tl), "origine %.1f cm | repere %.1f cm (dx %+.1f dy %+.1f dz %+.1f)", d0, Norm(dd) * 100,
                        dd.x * 100, dd.y * 100, dd.z * 100);
        } else {
          std::snprintf(tl, sizeof(tl), "distance a l'origine : %.1f cm   (m = poser un repere ici)", d0);
        }
        cv::putText(traj, tl, cv::Point(10, 66), cv::FONT_HERSHEY_SIMPLEX, 0.5, cv::Scalar(255, 200, 255), 1, cv::LINE_AA);
      }
      if (floorValid) {
        std::snprintf(tl, sizeof(tl), "sol a %+.2f m %s | hauteur de la tete : %.0f cm", floorZ, floorLocked ? "(verrouille)" : "(estime)",
                      (poseP.z - floorZ) * 100.0);
        cv::putText(traj, tl, cv::Point(8, traj.rows - 46), cv::FONT_HERSHEY_SIMPLEX, 0.45, cv::Scalar(120, 255, 120), 1, cv::LINE_AA);
      } else if (tracking) {
        cv::putText(traj, "sol : pas encore detecte (regarde le sol, 2 a 4 m devant toi)", cv::Point(8, traj.rows - 46),
                    cv::FONT_HERSHEY_SIMPLEX, 0.45, cv::Scalar(0, 165, 255), 1, cv::LINE_AA);
      }
      std::snprintf(tl, sizeof(tl), ", . zoom (%.0f px/m)%s | m/n repere | v verrouiller le sol | t demarrer/recentrer | u prior IMU",
                    pxPerM, zoomList[zoomIdx] <= 0 ? " auto" : "");
      cv::putText(traj, tl, cv::Point(10, traj.rows - 30), cv::FONT_HERSHEY_SIMPLEX, 0.4, cv::Scalar(170, 170, 170), 1, cv::LINE_AA);
      std::snprintf(tl, sizeof(tl), "[ ] delai camera %.0f ms (%s, a = auto) | - = prediction %.0f ms", camDelay * 1000, autoDelay ? "auto" : "manuel", predS * 1000);
      cv::putText(traj, tl, cv::Point(10, traj.rows - 12), cv::FONT_HERSHEY_SIMPLEX, 0.4, cv::Scalar(170, 170, 170), 1, cv::LINE_AA);
    }
    cv::imshow("trajectoire", traj);
    }  // fin de l'affichage (displayDue)

    int k = windowsOn ? cv::waitKey(1) : -1;
    int cmd = ctl.Poll();   // commandes envoyées par Unity (1 caractère)
    if (k < 0 && cmd >= 0) k = cmd;
    if (k < 0 && injectKey >= 0) { k = injectKey; injectKey = -1; }
    if (k == 'q' || k == 27 || k == 'Q') break;
    if (k == 'V') k = 'v';   // commandes d'Unity : V sol, Z yaw
    if (k == 'Z') k = 'z';
    if (k == 'T') { tracking = false; haveLast = false; poseValid = false; k = 't'; }   // T = recentrer : arrêt puis redémarrage
    if (k == 'r' && haveCalib && rectReady) showRect = !showRect;
    if (k == 'x') swapEyes = !swapEyes;
    if (k == 'c') {
      if (haveCalib && rectReady) {
        cv::Mat ca, cb;  // rectification pleine résolution, seulement pour ce contrôle
        cv::remap(left, ca, rect.mapL1, rect.mapL2, cv::INTER_LINEAR);
        cv::remap(right, cb, rect.mapR1, rect.mapR2, cv::INTER_LINEAR);
        checkMsg = CheckRectification(ca, cb);
      } else {
        checkMsg = "pas de calibration chargee";
      }
    }
    if (k == 'm') { haveMark = poseValid && haveLast; mark = poseP; }
    if (k == 'n') haveMark = false;
    if (k == '.') zoomIdx = std::min(5, zoomIdx + 1);
    if (k == ',') zoomIdx = std::max(0, zoomIdx - 1);
    if (k == 's') {
      char n1[64], n2[64];
      std::snprintf(n1, sizeof(n1), "capture/left_%04d.png", saved);
      std::snprintf(n2, sizeof(n2), "capture/right_%04d.png", saved);
      cv::imwrite(n1, left);
      cv::imwrite(n2, right);
      ++saved;
    }
    // ---- Calibration du montage de l'IMU ----
    if (k == 'g') {
      havePendingUp = MountStep1(acc, gyr, pendingUp, mountMsg);
    }
    if (k == 'f') {
      if (!havePendingUp) {
        mountMsg = "appuie d'abord sur g (casque a plat, a l'endroit, immobile)";
      } else {
        Mat3 R;
        if (MountStep2(pendingUp, acc, gyr, R, mountMsg)) {
          std::lock_guard<std::mutex> l(imu.m);
          imu.mount = R;
          imu.reinit = true;
          SaveMount(R);
          havePendingUp = false;
        }
      }
    }
    if (k >= '1' && k <= '6') {
      int axis = (k - '1') / 2;
      int sign = ((k - '1') % 2 == 0) ? -1 : +1;
      std::lock_guard<std::mutex> l(imu.m);
      imu.mount = Rot90(axis, sign) * imu.mount;
      imu.reinit = true;
      SaveMount(imu.mount);
      mountMsg = "montage tourne de 90 deg (sauvegarde)";
    }
    if (k == '0') {
      std::lock_guard<std::mutex> l(imu.m);
      imu.mount = Mat3();
      imu.reinit = true;
      SaveMount(imu.mount);
      mountMsg = "montage reinitialise";
    }
    if (k == 'z') {
      std::lock_guard<std::mutex> l(imu.m);
      imu.zero_yaw = true;
    }
    // ---- Tracking ----
    if (k == 't') {
      if (!tracking) {
        if (!haveCalib) {
          trackMsg = "le tracking a besoin d'une calibration chargee";
        } else {
          tracking = true;
          haveLast = false;
          poseValid = false;
          trail.clear();
          InvalidateSnap(vsnap);
          delayEst.Reset();
          havePrevOk = false;
          if (vo) vo->Reset();
          trackMsg = "tracking demarre : l'origine est ici, immobile 1 s";
          floorF.Reset();
          floorValid = false;
          floorLocked = false;
        }
      } else {
        tracking = false;
        haveLast = false;
        poseValid = false;
        InvalidateSnap(vsnap);
        trackMsg = "tracking arrete";
      }
    }
    if (k == 'u') usePrior = !usePrior;
    if (k == 'v' && floorValid) floorLocked = !floorLocked;
    if (k == '[') { camDelay = std::max(0.0, camDelay - 0.005); autoDelay = false; }   // réglage manuel : coupe l'automatique
    if (k == ']') { camDelay = std::min(0.2, camDelay + 0.005); autoDelay = false; }
    if (k == 'a') autoDelay = !autoDelay;
    if (k == '-') predS = std::max(0.0, predS - 0.005);
    if (k == '=') predS = std::min(0.1, predS + 0.005);
  }

  g_run = false;
  if (capThread.joinable()) capThread.join();
  if (th.joinable()) th.join();
  return 0;
}
