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
#include <cstdlib>
#include <cstring>
#include <deque>
#include <functional>
#include <future>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <utility>

#include <opencv2/opencv.hpp>
#include <opencv2/core/ocl.hpp>

#include "calib.h"
#include "calib_session.h"
#include "camera_model_json.h"
#include "ble_hub.h"
#include "ble_remote.h"
#include "delay_estimator.h"
#include "devices.h"
#include "eye_tracker.h"
#include "floor.h"
#include "frame_clock.h"
#include "mf_camera.h"
#include "mount_auto.h"
#include "mouth_tracker.h"
#include "imu_view.h"
#include "track_view.h"
#include "preview_share.h"
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
  uint32_t reopens = 0;       // fois ou le port est reste muet 3 s et a ete rouvert
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
  // Le port peut etre encore tenu une seconde par le programme qui vient de se fermer (mode services relance, suivi arrete...) :
  // on reessaie pendant ~5 s avant de declarer une erreur.
  bool opened = false;
  for (int tries = 0; tries < 10 && g_run; tries++) {
    if (sp.open(port)) { opened = true; break; }
    std::this_thread::sleep_for(std::chrono::milliseconds(500));
  }
  if (!opened) {
    std::lock_guard<std::mutex> l(st->m);
    st->port_error = true;
    return;
  }
  sp.pulseDtr();
  auto lastRx = std::chrono::steady_clock::now();
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
    if (n == 0) {
      // Rien recu depuis 3 s : port « ouvert mais muet » (carte pas prete, DTR sans effet...). On le rouvre, DTR relance.
      if (std::chrono::duration<double>(std::chrono::steady_clock::now() - lastRx).count() > 3.0) {
        sp.close();
        std::this_thread::sleep_for(std::chrono::milliseconds(300));
        for (int tries = 0; tries < 6 && g_run; tries++) {
          if (sp.open(port)) { sp.pulseDtr(); break; }
          std::this_thread::sleep_for(std::chrono::milliseconds(500));
        }
        lastRx = std::chrono::steady_clock::now();
        std::lock_guard<std::mutex> l(st->m);
        ++st->reopens;
        st->rate_acc = st->rate_gyr = 0;
      }
    }
    if (n > 0) {
      lastRx = std::chrono::steady_clock::now();
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
static std::atomic<double> g_camFps{0.0}, g_shareMs{0.0}, g_decodeMs{0.0};

// 'grab' lit la prochaine image (OpenCV, ou le lecteur Media Foundation direct) ; elle bloque jusqu'à l'arrivée d'une image.
static void CaptureThread(std::function<bool(cv::Mat&)> grab, Mailbox* mb, FrameShare* share, bool shareOn) {
  SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);   // la lecture de la caméra ne doit jamais attendre le reste
  bool shareFailed = false;
  int fails = 0;
  uint64_t n = 0, frameNo = 0;
  FrameClock clk;
  auto t0 = std::chrono::steady_clock::now();
  while (g_run) {
    cv::Mat f;
    if (!grab(f) || f.empty()) {
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

// Ecrit un fichier texte en entier puis le met a sa place d'un seul geste : un lecteur (Unity) ne voit jamais un fichier a moitie ecrit.
static void WriteTextAtomic(const std::string& path, const std::string& text) {
  const std::string tmp = path + ".tmp";
  {
    std::ofstream f(tmp, std::ios::binary | std::ios::trunc);
    if (!f) return;
    f << text;
  }
  MoveFileExA(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING);
}

// calib_info.json : ce que le suivi utilise (calibration, mode camera, zoom du mode). Les applications l'affichent, avec une alerte si le mode a change.
struct ZoomInfo {
  double zoom = 1.0;            // zoom du mode applique en ce moment
  std::string source = "none";  // none (reduit : hypothese), auto (appris avec le gyro), manual (saisi), inherited (autre cadence du meme format)
  bool autoOn = true;           // apprentissage actif
  bool confirmed = false;       // le zoom a ete verifie (rotation vue = rotation du gyro)
  int n = 0;                    // pas utilises par la derniere estimation
  double motion = 0, spread = 0, residual = 0;   // rotation cumulee (rad), dispersion, dernier zoom residuel mesure
  int run = 0;                  // cadence du mode utilise
};
static void WriteCalibInfo(const StereoCalib& raw, const StereoCalib& cur, const Rectifier* r, cv::Size run, const ZoomInfo& zi, const std::string& err) {
  const bool mismatch = raw.size.width > 0 && (run.width != raw.size.width || run.height != raw.size.height || (raw.fps > 0 && zi.run > 0 && raw.fps != zi.run));
  std::ostringstream o;
  o.setf(std::ios::fixed);
  o.precision(4);
  o << "{\"source\":\"" << ar6cal::JEsc(raw.source) << "\",\"model\":\"" << ar6cal::JEsc(raw.model) << "\",\"calibW\":" << raw.size.width
    << ",\"calibH\":" << raw.size.height << ",\"runW\":" << run.width << ",\"runH\":" << run.height << ",\"scale\":"
    << (raw.size.width > 0 ? (double)run.width / raw.size.width : 1.0) << ",\"mismatch\":" << (mismatch ? "true" : "false") << ",\"baselineMm\":"
    << (cur.T.total() == 3 ? cv::norm(cur.T) * 1000.0 : 0.0) << ",\"rms\":" << raw.rmsStereo << ",\"epi\":" << raw.epiPx << ",\"views\":" << raw.views
    << ",\"fps\":" << raw.fps << ",\"runFps\":" << zi.run << ",\"squareMm\":" << raw.squareMm << ",\"scaleCorr\":" << raw.scaleCorr << ",\"hfov\":"
    << (r ? r->hfov_deg : 0.0) << ",\"created\":\"" << ar6cal::JEsc(raw.created) << "\",\"error\":\"" << ar6cal::JEsc(err) << "\",\"zoom\":" << zi.zoom
    << ",\"zoomSource\":\"" << ar6cal::JEsc(zi.source) << "\",\"zoomAuto\":" << (zi.autoOn ? "true" : "false") << ",\"zoomConfirmed\":"
    << (zi.confirmed ? "true" : "false") << ",\"zoomN\":" << zi.n << ",\"zoomMotion\":" << zi.motion << ",\"zoomSpread\":" << zi.spread
    << ",\"zoomResidual\":" << zi.residual << "}";
  WriteTextAtomic(ExeDir() + "\\calib_info.json", o.str());
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

// Latence : Windows 11 peut ralentir un programme sans fenêtre visible (économie d'énergie, minuteries à 15 ms).
// On demande une priorité un peu plus haute, des minuteries à 1 ms et la fin de la limitation d'énergie.
static void BoostProcess() {
  SetPriorityClass(GetCurrentProcess(), ABOVE_NORMAL_PRIORITY_CLASS);
#if defined(PROCESS_POWER_THROTTLING_CURRENT_VERSION) && defined(PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION)
  PROCESS_POWER_THROTTLING_STATE st = {};
  st.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
  st.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION;
  st.StateMask = 0;   // contrôle pris, limitation désactivée
  SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &st, sizeof(st));
#endif
  typedef UINT(WINAPI * TimeBeginPeriodFn)(UINT);
  if (HMODULE m = LoadLibraryA("winmm.dll"))
    if (auto fn = reinterpret_cast<TimeBeginPeriodFn>(GetProcAddress(m, "timeBeginPeriod"))) fn(1);
}

// --parent <pid> : Unity lance ce programme et donne son propre numero de processus. Quand Unity se ferme (ou plante),
// le programme s'arrete tout seul : plus d'exe orphelin qui garde la camera et les ports.
static std::atomic<bool> g_parentGone{false};
static void StartParentWatch(DWORD pid) {
  std::thread([pid] {
    HANDLE h = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (!h) return;   // processus deja disparu ou inaccessible : on ne surveille rien
    WaitForSingleObject(h, INFINITE);
    CloseHandle(h);
    g_parentGone = true;
    std::this_thread::sleep_for(std::chrono::seconds(6));   // arret propre normalement deja fait ; sinon on force
    ExitProcess(0);
  }).detach();
}


// ---- Commandes communes au mode normal et au mode services -------------------------------------------------------------------------
// Les commandes arrivent en UDP 127.0.0.1:9877 : un caractere (T V Z x c g f 0-6 u [ ] a - = Q) ou une ligne "PV <source>".
// Il n'y a plus aucune fenetre ni aucune touche : tout est pilote par ar6dof_app (boutons) et par Unity.

// "PV n" : demande d'apercu (0 aucun, 1 camera stereo, 2 yeux, 3 bouche). A renvoyer au moins une fois par seconde : sans nouvelle demande
// pendant 3 s l'apercu s'arrete seul (aucun cout si l'application ou Unity est ferme).
static bool HandlePvCommand(const std::string& line) {
  if (line.size() >= 2 && line[0] == 'P' && line[1] == 'V') {
    ar6pv::Request(std::atoi(line.c_str() + 2));
    return true;
  }
  return false;
}

// Calibration du montage de l'IMU, sans tracking : g = etape 1 (a plat), f = etape 2 (penche vers l'avant), 1-6 = rotation de 90 deg, 0 = reinitialiser, z = yaw a zero.
struct MountCal {
  bool havePendingUp = false;
  Vec3 pendingUp;
  std::string msg = "montage : clique sur Calibrer le montage (un seul bouton, avec compte a rebours)";
  MountAuto au;   // calibration en un geste (commande MOUNT) : mesures moyennees, sans besoin de cliquer au bon moment
};

// Vue de l'IMU pour l'application (aperçu « IMU ») : pointeurs posés par le mode qui lit l'IMU, dernier message de calibration copié sous verrou.
static ImuState* g_imuView = nullptr;
static std::mutex g_viewMx;
static std::string g_viewMsg;
static std::atomic<bool> g_viewCal{false};
static void SetViewMsg(const std::string& m, bool calibrating) { std::lock_guard<std::mutex> l(g_viewMx); g_viewMsg = m; g_viewCal = calibrating; }

// Inverse l'avant et la gauche (rotation de 180 deg autour du haut) : pour corriger un montage pris dans le mauvais sens.
static void FlipForward(MountCal& mc) {
  if (!g_imuView) { mc.msg = "montage : IMU indisponible"; return; }
  std::lock_guard<std::mutex> l(g_imuView->m);
  Mat3 F;
  F.m[0][0] = -1; F.m[1][1] = -1;   // avant -> arriere, gauche -> droite, haut inchange
  g_imuView->mount = F * g_imuView->mount;
  g_imuView->reinit = true;
  SaveMount(g_imuView->mount);
  mc.msg = "montage : avant et gauche inverses (sauvegarde). Penche le casque : le modele doit pencher comme lui.";
}

// Commandes texte MOUNT / MOUNTU (demarrent la calibration en un geste ; 2e pose visiere vers le bas / vers le haut) et MOUNTX (l'annule). Vrai si la ligne a ete traitee.
static bool HandleMountLine(const std::string& line, MountCal& mc) {
  if (line == "MOUNT") { mc.au.Start(NowS(), mc.msg, false); mc.havePendingUp = false; return true; }    // pose 2 : visiere vers le bas
  if (line == "MOUNTU") { mc.au.Start(NowS(), mc.msg, true); mc.havePendingUp = false; return true; }    // pose 2 : visiere vers le haut
  if (line == "MOUNTX") { mc.au.Cancel(mc.msg); return true; }
  if (line == "MOUNTFLIP") { FlipForward(mc); return true; }
  return false;
}

// A appeler souvent : fait avancer la calibration en un geste avec les mesures de l'IMU (ne coute rien quand elle est inactive).
static void TickMountAuto(ImuState& imu, MountCal& mc) {
  if (!mc.au.Active()) return;
  Vec3 acc, gyr;
  { std::lock_guard<std::mutex> l(imu.m); acc = imu.acc_s; gyr = imu.gyr_s; }
  Mat3 R;
  if (mc.au.Update(NowS(), acc, gyr, R, mc.msg)) {
    std::lock_guard<std::mutex> l(imu.m);
    imu.mount = R;
    imu.reinit = true;
    SaveMount(R);
  }
  if (mc.au.TakeBeep()) std::thread([] { Beep(988, 140); }).detach();   // « mesure prise » : audible meme casque sur la tete
}

static void HandleMountCmd(int k, ImuState& imu, MountCal& mc, const Vec3& acc, const Vec3& gyr) {
  if (k == 'g') {
    mc.havePendingUp = MountStep1(acc, gyr, mc.pendingUp, mc.msg);
  } else if (k == 'f') {
    if (!mc.havePendingUp) {
      mc.msg = "fais d'abord l'etape 1 (casque a plat, a l'endroit, immobile)";
    } else {
      Mat3 R;
      if (MountStep2(mc.pendingUp, acc, gyr, R, mc.msg)) {
        std::lock_guard<std::mutex> l(imu.m);
        imu.mount = R;
        imu.reinit = true;
        SaveMount(R);
        mc.havePendingUp = false;
      }
    }
  } else if (k >= '1' && k <= '6') {
    int axis = (k - '1') / 2;
    int sign = ((k - '1') % 2 == 0) ? -1 : +1;
    std::lock_guard<std::mutex> l(imu.m);
    imu.mount = Rot90(axis, sign) * imu.mount;
    imu.reinit = true;
    SaveMount(imu.mount);
    mc.msg = "montage tourne de 90 deg (sauvegarde)";
  } else if (k == '0') {
    std::lock_guard<std::mutex> l(imu.m);
    imu.mount = Mat3();
    imu.reinit = true;
    SaveMount(imu.mount);
    mc.msg = "montage reinitialise";
  } else if (k == 'z') {
    std::lock_guard<std::mutex> l(imu.m);
    imu.zero_yaw = true;
  }
}

// Apercus des yeux et de la bouche vers la memoire partagee. Dort (200 ms) tant que personne ne demande rien ; ne copie rien non plus
// cote eye / mouth tracking. manage = mode services : les cameras d'oeil / de bouche sont ouvertes le temps de l'apercu puis refermees.
static void PreviewPump(bool manage) {
  UdpSender eyeCmd, mouthCmd;
  if (manage) { eyeCmd.Open("127.0.0.1", 9884); mouthCmd.Open("127.0.0.1", 9887); }
  auto send = [](UdpSender& u, const char* t) { u.Send(reinterpret_cast<const uint8_t*>(t), (int)std::strlen(t)); };
  bool eyeAuto = false, mouthAuto = false;
  cv::Mat m;
  while (g_run) {
    const int w = ar6pv::Wanted();
    if (manage) {
      if (w == ar6pv::kEyes && !eyeAuto && ar6eye::Running()) { send(eyeCmd, "EYE 1\n"); eyeAuto = true; }
      if (w != ar6pv::kEyes && eyeAuto) { send(eyeCmd, "EYE 0\n"); eyeAuto = false; }
      if (w == ar6pv::kMouth && !mouthAuto && ar6mouth::Running()) { send(mouthCmd, "MOUTH 1\n"); mouthAuto = true; }
      if (w != ar6pv::kMouth && mouthAuto) { send(mouthCmd, "MOUTH 0\n"); mouthAuto = false; }
    }
    if (w == ar6pv::kEyes) {
      if (ar6eye::Running() && ar6eye::GetPreview(&m)) ar6pv::Shared().Publish(m);
      else ar6pv::Shared().PublishText("eye tracking non configure : choisis les cameras d'oeil");
    } else if (w == ar6pv::kMouth) {
      if (ar6mouth::Running() && ar6mouth::GetPreview(&m)) ar6pv::Shared().Publish(m);
      else ar6pv::Shared().PublishText("mouth tracking non configure : choisis la camera de bouche");
    } else if (w == ar6pv::kTrack) {
      ar6pv::Shared().Publish(ar6trk::Draw());
    } else if (w == ar6pv::kImu) {
      if (g_imuView) {
        Quat q;
        bool qok = false;
        { std::lock_guard<std::mutex> l(g_imuView->m); q = g_imuView->q; qok = g_imuView->q_ok; }
        std::string msg;
        { std::lock_guard<std::mutex> l(g_viewMx); msg = g_viewMsg; }
        ar6pv::Shared().Publish(ar6imu::Draw(q, qok, msg, g_viewCal));
      } else {
        ar6pv::Shared().PublishText("IMU non selectionnee");
      }
    }
    std::this_thread::sleep_for(std::chrono::milliseconds((w == ar6pv::kEyes || w == ar6pv::kMouth || w == ar6pv::kImu || w == ar6pv::kTrack) ? 40 : 200));
  }
  if (eyeAuto) send(eyeCmd, "EYE 0\n");
  if (mouthAuto) send(mouthCmd, "MOUTH 0\n");
}

// Apercu de la camera stereo en mode services (le tracking ne tourne pas, donc personne d'autre n'utilise la camera) :
// elle n'est ouverte que pendant que l'apercu est demande, a 15 images/s, puis relachee.
static void StereoPreviewThread(int ci, int ciR, int camW, int H, int fps, int firstApi) {
  const int apis[2] = {firstApi == 1 ? cv::CAP_DSHOW : cv::CAP_MSMF, firstApi == 1 ? cv::CAP_MSMF : cv::CAP_DSHOW};
  cv::VideoCapture a, b;
  bool open = false;
  double lastPub = 0, lastFail = -10;
  int fails = 0;
  cv::Mat l, r, f;
  while (g_run) {
    if (ar6pv::Wanted() != ar6pv::kStereo) {
      if (open) { a.release(); b.release(); open = false; }
      std::this_thread::sleep_for(std::chrono::milliseconds(150));
      continue;
    }
    if (!open) {
      if (NowS() - lastFail < 2.0) { std::this_thread::sleep_for(std::chrono::milliseconds(100)); continue; }
      bool ok = false;
      for (int k = 0; k < 2 && !ok; k++) ok = OpenCam(a, ci, apis[k], camW, H, fps) && (ciR < 0 || OpenCam(b, ciR, apis[k], camW, H, fps));
      if (!ok) {
        a.release();
        b.release();
        lastFail = NowS();
        ar6pv::Shared().PublishText("camera introuvable ou deja utilisee");
        continue;
      }
      open = true;
      fails = 0;
      std::cout << "[apercu] camera stereo ouverte\n";
    }
    if (!a.grab() || (ciR >= 0 && !b.grab())) {
      if (++fails > 60) { a.release(); b.release(); open = false; lastFail = NowS(); }
      continue;
    }
    fails = 0;
    const double t = NowS();
    if (t - lastPub < 0.066) continue;   // grab() seul : on ne decode que les images affichees
    lastPub = t;
    if (!a.retrieve(l) || l.empty()) continue;
    if (ciR >= 0) {
      if (!b.retrieve(r) || r.empty() || l.rows != r.rows || l.type() != r.type()) continue;
      cv::hconcat(l, r, f);
    } else {
      f = l;
    }
    ar6pv::Shared().Publish(f);
  }
}

// ---- Mode services : ar6dof_probe --services ----------------------------------------------------------------------------------------
// Ni camera stereo ouverte en permanence, ni suivi 6DoF, ni passthrough, ni Bluetooth : seulement ce qu'il faut pour regler le materiel
// depuis ar6dof_app sans lancer Unity (calibration du montage de l'IMU, eye / mouth tracking, apercus des cameras).
// Les yeux et la bouche n'ouvrent leurs cameras que pendant un apercu ou une calibration.
static int RunServices() {
  g_auto = true;
  std::cout << "=== ar6dof_probe : mode services (sans suivi 6DoF) ===\n";
  _putenv_s("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");   // sinon l'ouverture d'une camera MSMF peut prendre plusieurs dizaines de secondes
  ImuState imu;
  imu.mount = LoadMount();
  g_imuView = &imu;
  std::thread th;
  int pi = -1;
  auto ports = ListComPorts();
  {
    auto it = g_cfg.find("portname");
    if (it != g_cfg.end())
      for (size_t i = 0; i < ports.size(); i++) if (ports[i] == it->second) pi = (int)i;
  }
  if (pi >= 0) {
    std::cout << "IMU : " << ports[pi] << "\n";
    th = std::thread(ImuThread, ports[pi], &imu, (VisionSnap*)nullptr, (UdpSender*)nullptr);
  } else {
    std::cout << "IMU : aucun port choisi (page Capteurs de l'application)\n";
  }
  struct Stop { ~Stop() { ar6eye::Stop(); ar6mouth::Stop(); } } stopAll;
  const int ncam = (int)ListCameras().size();
  const int ci = SavedInt("cam", 0, 0, 64), ciR = SavedInt("camR", -1, -1, 64);
  int eL = SavedInt("eyecamL", -1, -1, 64), eR = SavedInt("eyecamR", -1, -1, 64);
  int mc = SavedInt("mouthcam", -1, -1, 64);
  if (eL >= ncam) eL = -1;
  if (eR >= ncam) eR = -1;
  if (mc >= ncam) mc = -1;
  if ((eL >= 0 && (eL == ci || eL == ciR)) || (eR >= 0 && (eR == ci || eR == ciR)) || (eL >= 0 && eL == eR)) { std::cout << "[yeux] une meme camera est utilisee deux fois : eye tracking ignore\n"; eL = eR = -1; }
  if (mc >= 0 && (mc == ci || mc == ciR || mc == eL || mc == eR)) { std::cout << "[bouche] la camera de bouche est deja utilisee ailleurs : mouth tracking ignore\n"; mc = -1; }
  if (eL >= 0 || eR >= 0) {
    ar6eye::Options eo;
    eo.camL = eL; eo.camR = eR; eo.fps = SavedInt("eyefps", 90, 10, 240); eo.backend = SavedInt("eyebackend", 0, 0, 1);
    eo.startEnabled = false; eo.dataDir = ExeDir();
    ar6eye::Start(eo, [](const std::string& m) { std::cout << m << std::endl; });
  }
  if (mc >= 0) {
    ar6mouth::Options mo;
    mo.cam = mc; mo.fps = SavedInt("mouthfps", 60, 10, 120); mo.backend = SavedInt("mouthbackend", 0, 0, 1);
    mo.startEnabled = false; mo.dataDir = ExeDir();
    ar6mouth::Start(mo, [](const std::string& m) { std::cout << m << std::endl; });
  }
  // camera stereo (apercu seulement)
  const int W0 = SavedInt("width", 1600, 320, 8000), H0 = SavedInt("height", 600, 240, 4000), F0 = SavedInt("fps", 120, 5, 240);
  const int camW = ciR >= 0 ? (W0 / 2) : W0;
  std::thread stereo(StereoPreviewThread, ci, ciR, camW, H0, F0, SavedInt("backend", 0, 0, 1));
  std::thread pump(PreviewPump, true);
  UdpControl ctl;
  if (!ctl.Open(9877)) std::cout << "Canal de commande (UDP 9877) indisponible : un autre ar6dof_probe tourne-t-il ?\n";
  std::cout << "Pret : pilotage par l'application (UDP 9877).\n";
  MountCal mcal;
  std::string lastMsg = mcal.msg;
  double lastStat = 0;
  bool quit = false;
  while (!quit && !g_parentGone) {
    std::string line;
    while (ctl.PollText(line)) {
      if (line == "Q" || line == "q") { quit = true; break; }
      if (HandlePvCommand(line)) continue;
      if (HandleMountLine(line, mcal)) continue;
      if (line.size() != 1) continue;
      Vec3 acc, gyr;
      { std::lock_guard<std::mutex> l(imu.m); acc = imu.acc_s; gyr = imu.gyr_s; }
      HandleMountCmd((unsigned char)line[0], imu, mcal, acc, gyr);
    }
    TickMountAuto(imu, mcal);
    if (mcal.msg != lastMsg) { lastMsg = mcal.msg; std::cout << "[montage] " << mcal.msg << std::endl; }
    SetViewMsg(mcal.msg, mcal.au.Active());
    const double now = NowS();
    if (now - lastStat >= 1.0) {
      lastStat = now;
      bool err;
      double ra, rg;
      uint64_t okN, badN, bytes;
      uint32_t ro;
      double na;
      {
        std::lock_guard<std::mutex> l(imu.m);
        ro = imu.reopens;
        err = imu.port_error; ra = imu.rate_acc; rg = imu.rate_gyr; okN = imu.ok; badN = imu.bad; bytes = imu.bytes; na = Norm(imu.acc_s);
      }
      char sb[240];
      if (pi < 0) std::snprintf(sb, sizeof(sb), "[imu] non selectionnee");
      else if (err) std::snprintf(sb, sizeof(sb), "[imu] erreur d'ouverture ou de lecture du port");
      else if (bytes > 0 && okN == 0) std::snprintf(sb, sizeof(sb), "[imu] octets recus mais 0 paquet valide (firmware v2 flashe ?)");
      else if (bytes == 0 && ro > 0) std::snprintf(sb, sizeof(sb), "[imu] aucun octet recu (port rouvert %u fois) : carte branchee ? firmware flashe ? autre programme sur le port ?", (unsigned)ro);
      else std::snprintf(sb, sizeof(sb), "[imu] accel %.0f Hz | gyro %.0f Hz | |a| = %.2f m/s2 | paquets ok=%llu ko=%llu", ra, rg, na, (unsigned long long)okN, (unsigned long long)badN);
      std::cout << sb << std::endl;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  g_run = false;
  if (pump.joinable()) pump.join();
  if (stereo.joinable()) stereo.join();
  if (th.joinable()) th.join();
  return 0;
}

int main(int argc, char** argv) {
  bool noBle = false, services = false;
  int cvThreads = 2;
  std::ofstream logFile(ExeDir() + "\\ar6dof_probe.log", std::ios::trunc);
  TeeBuf tee(std::cout.rdbuf(), logFile.rdbuf());
  std::streambuf* origCout = std::cout.rdbuf(&tee);
  std::cout << std::unitbuf;   // chaque ligne est écrite tout de suite (console ET fichier)
  struct CoutRestore { std::streambuf* o; ~CoutRestore() { std::cout.rdbuf(o); } } coutRestore{origCout};
  g_qpcOffset = QpcSeconds() - NowS();
  BoostProcess();
  for (int i = 1; i < argc; i++) {
    std::string a = argv[i];
    if (a == "--auto") g_auto = true;                 // rejoue les réponses de la dernière fois, sans question
    else if (a == "--no-ble") noBle = true;            // ne pas ecouter la telecommande Bluetooth
    else if (a == "--no-windows") {}                    // ancienne option : il n'y a plus aucune fenêtre OpenCV (tout se pilote depuis ar6dof_app et Unity)
    else if (a == "--services") services = true;        // sans caméra stéréo ni suivi 6DoF : IMU, yeux, bouche et aperçus seulement
    else if (a == "--parent" && i + 1 < argc) { long long pid = atoll(argv[++i]); if (pid > 0) StartParentWatch((DWORD)pid); }
    else if (a == "--cv-threads" && i + 1 < argc) cvThreads = std::max(1, atoi(argv[++i]));   // threads d'OpenCV (defaut 2)
  }
  // OpenCV repartit chaque petit calcul (flot optique, remap...) sur TOUS les coeurs : sur un CPU a 16 threads la synchronisation
  // coute plus que le calcul (50 % de CPU partout pour un suivi leger). 2 threads suffisent largement et liberent le reste.
  cv::setNumThreads(cvThreads);
  cv::ocl::setUseOpenCL(false);   // pas de GPU OpenCL pour ces petits traitements : le transfert coute plus que le gain
  std::cout << "OpenCV : " << cvThreads << " thread(s) de calcul (option --cv-threads N)\n";
  LoadConfig();   // les réponses précédentes servent de valeurs par défaut, même sans --auto
  if (g_auto && g_cfg.empty()) std::cout << "[auto] aucune configuration trouvee (" << ConfigPath() << ") : lance d'abord ar6dof_probe.exe SANS --auto.\n";
  std::cout << "ar6dof_probe " << (g_auto ? "(--auto) " : "") << "- config : " << ConfigPath() << "\n";
  if (services) return RunServices();
  // Télécommande Bluetooth -> UDP local (Unity). Se reconnecte seule ; --no-ble pour la désactiver.
  struct BleStop { ~BleStop() { ar6ble::Stop(); ar6hub::Stop(); ar6eye::Stop(); ar6mouth::Stop(); } } bleStop;
  // Sans ça, l'ouverture d'une caméra avec le backend MSMF peut prendre
  // plusieurs dizaines de secondes.
  _putenv_s("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");

  std::cout << "=== ar6dof_probe ===\n\nCameras :\n";
  auto cams = ListCameras();
  if (cams.empty()) { std::cout << "  aucune camera trouvee\n"; return 1; }
  for (size_t i = 0; i < cams.size(); i++) std::cout << "  [" << i << "] " << cams[i] << "\n";
  int ci = AskInt("cam", "Camera stereo (ou camera GAUCHE si tes deux yeux sont deux cameras separees)", 0, 0, (int)cams.size() - 1);
  int ciR = AskInt("camR", "Camera DROITE (-1 = une seule camera : module a deux capteurs, image cote a cote)", -1, -1, (int)cams.size() - 1);
  if (ciR == ci) { std::cout << "  la camera droite est la meme que la gauche : ignoree (module a deux capteurs)\n"; ciR = -1; }

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
  if (ciR >= 0) W = (W / 2) * 2;
  const int camW = ciR >= 0 ? W / 2 : W;   // largeur demandee a CHAQUE camera (deux cameras separees : la moitie de l'image cote a cote)
  if (ciR >= 0)
    std::cout << "Deux cameras separees : " << camW << "x" << H << " chacune, assemblees en " << W << "x" << H << " (gauche|droite). Elles ne sont pas synchronisees par le materiel.\n";
  int udpPort = AskInt("udp", "Port UDP vers Unity sur ce PC (0 = desactive)", 9876, 0, 65535);
  int shareFrames = AskInt("share", "Partager l'image de la camera avec Unity pour le passthrough (1 = oui, 0 = non)", 1, 0, 1);
  int autoTrack = AskInt("autotrack", "Demarrer le tracking automatiquement (1 = oui, 0 = non)", 1, 0, 1);
  // Eye tracking (2 cameras infrarouges d'oeil). Reponse 0 = rien n'est cree : ni camera, ni thread, ni socket.
  int eyeOn = AskInt("eye", "Eye tracking avec 2 cameras d'oeil (1 = oui, 0 = non : aucune charge)", 0, 0, 1);
  int eyeCamL = -1, eyeCamR = -1, eyeFps = 90, eyeBackend = 0;
  if (eyeOn) {
    std::cout << "  (les numeros ci-dessus sont ceux des cameras ; -1 = pas de camera pour cet oeil)\n";
    eyeCamL = AskInt("eyecamL", "  Numero de la camera de l'oeil GAUCHE", 1, -1, (int)cams.size() - 1);
    eyeCamR = AskInt("eyecamR", "  Numero de la camera de l'oeil DROIT", 2, -1, (int)cams.size() - 1);
    eyeFps = AskInt("eyefps", "  Cadence de traitement par oeil (fps ; 60 = tres leger, 120 = maximum)", 90, 10, 240);
    eyeBackend = AskInt("eyebackend", "  Backend des cameras d'oeil (0 = MSMF : meme numerotation que la liste ci-dessus, 1 = DSHOW)", 0, 0, 1);
    if ((eyeCamL >= 0 && eyeCamL == ci) || (eyeCamR >= 0 && eyeCamR == ci) || (eyeCamL >= 0 && eyeCamL == eyeCamR)) {
      std::cout << "  ATTENTION : une meme camera est utilisee deux fois (stereo / oeil gauche / oeil droit) : eye tracking desactive.\n";
      eyeOn = 0;
    }
  }

  // Mouth tracking (une camera dirigee vers la bouche), independant de l'eye tracking. Reponse 0 = rien n'est cree.
  int mouthOn = AskInt("mouth", "Mouth tracking avec une camera de bouche (1 = oui, 0 = non : aucune charge)", 0, 0, 1);
  int mouthCam = -1, mouthFps = 60, mouthBackend = 0;
  if (mouthOn) {
    mouthCam = AskInt("mouthcam", "  Numero de la camera de la bouche (-1 = aucune)", 3, -1, (int)cams.size() - 1);
    if (mouthCam < 0) mouthOn = 0;
    mouthFps = AskInt("mouthfps", "  Cadence de traitement (fps ; 30 = tres leger)", 60, 10, 120);
    mouthBackend = AskInt("mouthbackend", "  Backend de la camera de bouche (0 = MSMF, 1 = DSHOW)", 0, 0, 1);
    if (mouthOn && (mouthCam == ci || mouthCam == ciR || mouthCam == eyeCamL || mouthCam == eyeCamR)) {
      std::cout << "  ATTENTION : la camera de bouche est deja utilisee ailleurs : mouth tracking desactive.\n";
      mouthOn = 0;
    }
  }

  // Calibration : format natif AR6DoF (fait par Unity > Calibration de la camera) ou fichiers RTAB-Map.
  StereoCalib calib, calibRaw;   // calibRaw : telle que calibree ; calib : exprimee dans le mode camera reellement utilise (ResolveCalib)
  bool haveCalib = false;
  std::string calibErr = "aucun dossier de calibration";
  std::string dir = AskStr("calibdir", "\nDossier de calibration (ar6dof_calib.yml, sinon fichiers RTAB-Map ; '-' = aucune)", ExeDir() + "\\calibration");
  if (dir != "-") {
    if (LoadCalibAuto(dir, calib, calibErr)) {
      haveCalib = true;
      std::cout << "Calibration chargee (" << calib.files << ", " << (calib.source == "ar6dof" ? "AR6DoF" : "RTAB-Map") << "), faite en "
                << calib.size.width << "x" << calib.size.height << " par oeil.\n";
      if (calib.source == "ar6dof")
        std::cout << "  modele " << calib.model << ", " << calib.views << " vues, erreur " << calib.rmsStereo << " px, ecart vertical " << calib.epiPx
                  << " px, mode camera " << calib.size.width << "x" << calib.size.height << " a " << calib.fps << " i/s, carreaux de "
                  << calib.squareMm << " mm.\n";
      // L'echelle metrique d'une calibration depend de la taille des carreaux de la mire saisie pendant la calibration.
      // Une calibration AR6DoF a sa propre verification a la regle ; pour un import RTAB-Map on peut corriger avec la distance REELLE des objectifs.
      double calibBase = cv::norm(calib.T) * 1000.0;
      std::cout << "Base entre les cameras dans la calibration : " << calibBase << " mm\n";
      if (calib.source == "ar6dof") {
        std::cout << "(la base saisie a la main est ignoree pour une calibration AR6DoF : l'echelle vient de la taille des carreaux, verifiee a la regle)\n";
      } else {
        double saved = LoadBaseline();
        char defbuf[32];
        std::snprintf(defbuf, sizeof(defbuf), "%.1f", saved);
        std::string a = AskStr("baseline", "Base REELLE entre les centres des 2 objectifs, en mm (0 = garder celle de la calibration)", defbuf);
        double real = 0;
        try { real = std::stod(a); } catch (...) { real = 0; }
        SaveBaseline(real);
        if (real > 1.0 && calibBase > 1.0) {
          calib.T = calib.T * (real / calibBase);
          std::cout << "Echelle corrigee : x" << real / calibBase << " (base utilisee : " << real << " mm)\n";
        }
      }
      if (ExportCameraModel(calib, ExeDir() + "\\camera_model.json"))
        std::cout << "Modele de camera exporte pour Unity : " << ExeDir() << "\\camera_model.json\n";
    } else {
      std::cout << "Calibration NON chargee : " << calibErr << "\n";
    }
  }

  if (haveCalib) calibRaw = calib;
  // Zoom du mode : une calibration faite dans un mode (taille, cadence) sert dans tous les autres. Les focales sont recalculees pour le mode utilise
  // (ResolveCalib) ; le « zoom » corrige le cas ou un mode recadre le capteur au lieu de le reduire (frequent en 120 i/s). Il est appris avec le gyro.
  ZoomStore zoomStore;
  const std::string zoomPath = ExeDir() + "\\mode_zoom.yml";
  ZoomInfo zi;
  zi.run = 0;
  double curZoom = 1.0;
  cv::Size resolvedFor(0, 0);
  bool zoomDirty = false;
  double lastZoomT = 0;
  if (haveCalib) zoomStore.Load(zoomPath, CalibId(calibRaw));

  // Ouverture de la caméra avec la cadence demandée. On MESURE la cadence reçue ;
  // si elle est trop basse avec un backend, on essaie l'autre (MSMF / DirectShow).
  cv::VideoCapture cap;
  int camApi = cv::CAP_MSMF, mfDirect = 0;
  {
    const int apis[2] = {cv::CAP_MSMF, cv::CAP_DSHOW};
    const char* names[2] = {"MSMF", "DSHOW"};
    int first = AskInt("backend", "Backend camera (0 = MSMF, 1 = DSHOW ; l'autre est essaye si la cadence est trop basse)", 0, 0, 1);
    mfDirect = AskInt("mfdirect", "Lecteur camera direct Media Foundation (1 = oui : moins de latence et de CPU, JPEG decode par libjpeg-turbo ; 0 = OpenCV)", 1, 0, 1);
    // La télécommande Bluetooth démarre APRÈS toutes les questions (ses messages ne se mélangent plus aux réponses)
    if (!noBle) ar6ble::Start(9878, [](const std::string& m) { std::cout << m << std::endl; });
    if (!noBle) ar6hub::Start([](const std::string& m) { std::cout << m << std::endl; });   // appareils Bluetooth des menus Unity
    if (eyeOn) {
      ar6eye::Options eo;
      eo.camL = eyeCamL; eo.camR = eyeCamR; eo.fps = eyeFps; eo.backend = eyeBackend; eo.dataDir = ExeDir();
      ar6eye::Start(eo, [](const std::string& m) { std::cout << m << std::endl; });
    }
    if (mouthOn) {
      ar6mouth::Options mo;
      mo.cam = mouthCam; mo.fps = mouthFps; mo.backend = mouthBackend; mo.startEnabled = true; mo.dataDir = ExeDir();
      ar6mouth::Start(mo, [](const std::string& m) { std::cout << m << std::endl; });
    }
    double best = -1;
    int bestIdx = -1, openIdx = -1;
    for (int attempt = 0; attempt < 2; attempt++) {
      int bi = (first + attempt) % 2;
      openIdx = -1;
      if (!OpenCam(cap, ci, apis[bi], camW, H, FPS)) {
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
    if (openIdx != bestIdx && !OpenCam(cap, ci, apis[bestIdx], camW, H, FPS)) {
      std::cout << "Impossible de rouvrir la camera.\n";
      return 1;
    }
    camApi = apis[bestIdx];
    std::cout << "Backend retenu : " << names[bestIdx] << " (" << best << " fps)\n";
    if (best < 0.85 * FPS)
      std::cout << "ATTENTION : cadence inferieure a la demande. Verifie le mode (resolution/fps), le port USB 3,\n"
                   "et que la piece est assez eclairee (en basse lumiere la camera allonge l'exposition et baisse sa cadence).\n";
  }
  std::cout << "Resolution negociee : " << cap.get(cv::CAP_PROP_FRAME_WIDTH) << "x"
            << cap.get(cv::CAP_PROP_FRAME_HEIGHT)
            << "  (si differente de ce que tu as demande, ce mode n'existe pas)\n";

  // Lecteur direct (option) : il faut libérer la caméra pour OpenCV avant de l'ouvrir ; en cas d'échec on rouvre avec OpenCV.
  ar6mf::Camera mfcam;
  bool useMf = false;
  if (mfDirect) {
    cap.release();
    std::string err;
    if (mfcam.Open(ci, camW, H, FPS, &err) && mfcam.SelfTest(40, 5, &err)) {
      useMf = true;
      std::cout << "Lecteur camera DIRECT (Media Foundation + libjpeg-turbo) actif : MJPEG " << camW << "x" << H << " a " << mfcam.ModeFps() << " fps.\n";
    } else {
      std::cout << "Lecteur direct indisponible (" << err << ") : retour a OpenCV.\n";
      mfcam.Close();
      if (!OpenCam(cap, ci, camApi, camW, H, FPS)) { std::cout << "Impossible de rouvrir la camera.\n"; return 1; }
    }
  }

  // Deuxieme camera (yeux = deux cameras separees) : meme mode que la premiere, lecteur direct si celui-ci a marche pour la gauche.
  cv::VideoCapture capR;
  ar6mf::Camera mfcamR;
  if (ciR >= 0) {
    bool okR = false;
    if (useMf) {
      std::string err;
      okR = mfcamR.Open(ciR, camW, H, FPS, &err) && mfcamR.SelfTest(40, 5, &err);
      if (!okR) { std::cout << "Camera droite : lecteur direct indisponible (" << err << ") : OpenCV.\n"; mfcamR.Close(); }
    }
    if (!okR) okR = OpenCam(capR, ciR, camApi, camW, H, FPS);
    if (!okR) { std::cout << "Impossible d'ouvrir la camera droite [" << ciR << "] (deja utilisee ? mode " << camW << "x" << H << " absent ?).\n"; return 1; }
    std::cout << "Camera droite [" << ciR << "] ouverte : " << camW << "x" << H << (mfcamR.IsOpen() ? " (lecteur direct)" : " (OpenCV)") << "\n";
  }

  ImuState imu;
  imu.mount = LoadMount();
  g_imuView = &imu;
  std::thread th;

  int frames = 0, fails = 0;
  double fps = 0;
  auto tf = std::chrono::steady_clock::now();
  cv::Mat frame;
  Rectifier rect;
  bool rectReady = false, swapEyes = false;
  std::string checkMsg = "bouton Verifier la calibration : mesure la qualite (scene riche en details)";
  MountCal mc;

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
  cv::Size smallFor;
  double lastPv = 0;   // dernier apercu de la camera stereo publie (seulement quand l'application ou Unity le demande)
  std::string trackMsg = "suivi arrete : bouton Redemarrer le suivi";
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
  FloorMap floorMap;          // hauteurs accumulees dans le temps (voir floor.h)
  double imuWaitT0 = -1;      // debut de l'attente de l'IMU avant d'aligner le monde sur la gravite
  bool floorValid = false, floorLocked = false;
  double floorZ = 0, lastFloorT = 0;
  if (!g_auto) SaveConfig();   // les réponses servent de configuration pour --auto
  UdpControl ctl;
  if (!ctl.Open(9877)) std::cout << "Canal de commande (UDP 9877) indisponible.\n";
  Mailbox mb;
  uint64_t lastId = 0;
  std::function<bool(cv::Mat&)> grab;
  if (useMf)
    grab = [&mfcam](cv::Mat& f) {
      double ms = 0;
      bool ok = mfcam.Read(f, &ms);
      if (ok && !f.empty()) g_decodeMs = 0.95 * g_decodeMs.load() + 0.05 * ms;
      return ok;
    };
  else
    grab = [&cap](cv::Mat& f) { return cap.read(f); };
  if (ciR >= 0) {
    // deux cameras : les deux lectures partent en meme temps (chacune attend sa prochaine image), puis les images sont collees cote a cote
    std::function<bool(cv::Mat&)> gl = grab, gr;
    if (mfcamR.IsOpen()) gr = [&mfcamR](cv::Mat& f) { return mfcamR.Read(f, nullptr); };
    else gr = [&capR](cv::Mat& f) { return capR.read(f); };
    grab = [gl, gr](cv::Mat& f) {
      cv::Mat l, r;
      auto fut = std::async(std::launch::async, [&gr, &r] { return gr(r); });
      bool okL = gl(l);
      bool okR = fut.get();
      f.release();
      if (!okL || !okR) return false;
      if (l.empty() || r.empty() || l.rows != r.rows || l.type() != r.type()) return true;   // image manquante : on saute ce tour
      cv::hconcat(l, r, f);
      return true;
    };
  }
  std::thread capThread(CaptureThread, grab, &mb, &share, shareFrames != 0);
  std::thread pump(PreviewPump, false);   // apercus des yeux / de la bouche, seulement quand ils sont demandes
  std::cout << "\nPilotage par l'application ou Unity (UDP 9877) : aucune fenetre, aucune touche.\n";
  // Les messages d'état (montage de l'IMU, suivi, vérification de la calibration) ne s'affichaient que dans les fenêtres :
  // ils sont aussi écrits dans la console / le journal, que lit l'application ar6dof_app (boutons à la place des touches).
  std::string lastMountMsg = mc.msg, lastTrackMsg = trackMsg, lastCheckMsg = checkMsg;
  std::deque<std::string> cmdQ;
  if (autoTrack && haveCalib) cmdQ.push_back("t");   // demarre le tracking des la premiere image
  bool quit = false;

  // ---- Calibration de la camera (Unity > Appareils > Calibration de la camera) : capture guidee, calcul, enregistrement ----
  // Commandes (lignes UDP 9877) : CALSTART colonnes lignes carreau_mm [base_mm], CALCANCEL, CALCOMPUTE, CALVERIFY mm, CALSCALE rapport,
  // CALAPPLY, CALIMPORT. L'etat est ecrit dans calib_status.json (lu par Unity) ; rien ne tourne tant que la page n'est pas utilisee.
  ar6cal::Session calSess;
  bool calWasTracking = false;
  unsigned calWrittenVer = ~0u;
  double calWriteT = -1e9;
  const std::string calStatusPath = ExeDir() + "\\calib_status.json";
  auto WriteCalStatus = [&](bool force) {
    const double t = NowS();
    const unsigned v = calSess.Version();
    if (!force && (v == calWrittenVer || t - calWriteT < 0.2)) return;
    calWrittenVer = v;
    calWriteT = t;
    WriteTextAtomic(calStatusPath, calSess.StatusJson());
  };
  WriteCalStatus(true);   // efface un etat laisse par un lancement precedent
  auto CalDir = [&]() { return dir != "-" ? dir : ExeDir() + "\\calibration"; };
  auto ApplyCalib = [&](const StereoCalib& nc) {
    calibRaw = nc;
    calib = nc;
    haveCalib = true;
    calibErr.clear();
    zoomStore.Load(zoomPath, CalibId(calibRaw));   // une nouvelle calibration oublie les zooms appris avec l'ancienne
    resolvedFor = cv::Size(0, 0);                  // la calibration est re-exprimee dans le mode utilise a la prochaine image
    rectReady = false;
    trackReady = false;
    haveLast = false;
    std::cout << "[calibration] calibration appliquee : " << calibRaw.model << ", base " << cv::norm(calibRaw.T) * 1000.0 << " mm" << std::endl;
  };
  auto HandleCal = [&](const std::string& cl) -> bool {
    if (cl.compare(0, 3, "CAL") != 0) return false;
    std::istringstream is(cl);
    std::string cmd;
    is >> cmd;
    if (cmd == "CALSTART") {
      int c = 9, r = 6;
      double mm = 25, hint = 0;
      is >> c >> r >> mm >> hint;
      ar6cal::Board b;
      b.cols = std::max(4, std::min(20, c));
      b.rows = std::max(3, std::min(20, r));
      b.squareMm = std::max(3.0, std::min(300.0, mm));
      if (tracking) calWasTracking = true;
      tracking = false; haveLast = false; poseValid = false;
      InvalidateSnap(vsnap);
      ar6trk::Stop();
      calSess.Start(b, FPS, 18, hint);
      trackMsg = "calibration de la camera en cours";
      std::cout << "[calibration] capture : mire " << b.cols << "x" << b.rows << " coins, carreaux de " << b.squareMm << " mm, mode "
                << frame.cols / 2 << "x" << frame.rows << " a " << FPS << " i/s" << std::endl;
    } else if (cmd == "CALCANCEL") {
      calSess.Cancel();
      if (calWasTracking && haveCalib) cmdQ.push_back("t");
      calWasTracking = false;
      std::cout << "[calibration] annulee" << std::endl;
    } else if (cmd == "CALCOMPUTE") {
      calSess.RequestCompute();
    } else if (cmd == "CALVERIFY") {
      double mm = 0;
      is >> mm;
      std::string e;
      if (mm < 50 || !calSess.Verify(mm, e)) std::cout << "[calibration] verification impossible : " << (mm < 50 ? "distance invalide" : e) << std::endl;
    } else if (cmd == "CALSCALE") {
      double ratio = 1;
      is >> ratio;
      calSess.ApplyScale(ratio);
      std::cout << "[calibration] echelle corrigee : x" << ratio << std::endl;
    } else if (cmd == "CALAPPLY") {
      if (!calSess.HasResult()) { std::cout << "[calibration] rien a enregistrer" << std::endl; }
      else {
        const std::string dst = CalDir();
        std::error_code ec;
        std::filesystem::create_directories(dst, ec);
        const std::filesystem::path nat = std::filesystem::path(dst) / NativeCalibName();
        if (std::filesystem::exists(nat, ec)) {
          std::filesystem::remove(std::filesystem::path(dst) / "ar6dof_calib.prev.yml", ec);
          std::filesystem::copy_file(nat, std::filesystem::path(dst) / "ar6dof_calib.prev.yml", ec);
        }
        StereoCalib nc = calSess.Result().calib;
        std::string err;
        if (SaveNativeCalib(nc, nat.string(), err)) {
          ApplyCalib(nc);
          calSess.MarkApplied();
          if (calWasTracking) cmdQ.push_back("t");
          calWasTracking = false;
          std::cout << "[calibration] enregistree dans " << nat.string() << std::endl;
        } else {
          std::cout << "[calibration] enregistrement impossible : " << err << std::endl;
        }
      }
    } else if (cmd == "CALZOOM") {   // zoom du mode saisi a la main (1.00 = focale reduite avec l'image ; 2.00 = mode recadre x2)
      double z = 0;
      is >> z;
      if (haveCalib && resolvedFor.width > 0 && z > 0.25 && z < 4.0) {
        ZoomEntry e;
        e.key = ModeKey(resolvedFor, FPS);
        e.zoom = z;
        e.source = "manual";
        zoomStore.Set(e);
        zoomStore.Save(zoomPath);
        zoomDirty = true;
        std::cout << "[calibration] zoom du mode " << e.key << " fixe a x" << z << std::endl;
      }
    } else if (cmd == "CALZOOMAUTO") {   // apprentissage automatique du zoom du mode : 1 = actif (defaut), 0 = fige
      int a = 1;
      is >> a;
      zi.autoOn = a != 0;
      std::cout << "[calibration] apprentissage du zoom : " << (zi.autoOn ? "actif" : "fige") << std::endl;
    } else if (cmd == "CALZOOMRESET") {   // oublie le zoom de ce mode : il sera appris de nouveau
      if (haveCalib && resolvedFor.width > 0) {
        zoomStore.Erase(ModeKey(resolvedFor, FPS));
        zoomStore.Save(zoomPath);
        zoomDirty = true;
        zi.autoOn = true;
        std::cout << "[calibration] zoom du mode oublie" << std::endl;
      }
    } else if (cmd == "CALIMPORT") {
      StereoCalib nc;
      std::string err;
      if (ConvertRtabmapToNative(CalDir(), nc, err)) {
        ApplyCalib(nc);
        std::cout << "[calibration] import RTAB-Map converti en " << NativeCalibName() << std::endl;
      } else {
        std::cout << "[calibration] import RTAB-Map impossible : " << err << std::endl;
      }
    }
    WriteCalStatus(true);
    return true;
  };

  auto drainCtl = [&] {
    std::string l;
    while (ctl.PollText(l)) {
      if (l == "Q" || l == "q") quit = true;
      else if (!HandlePvCommand(l) && !HandleMountLine(l, mc)) cmdQ.push_back(l);
    }
  };

  while (true) {
    double tRead = 0, tSm = 0;
    {
      std::unique_lock<std::mutex> l(mb.m);
      mb.cond.wait_for(l, std::chrono::milliseconds(30), [&] { return mb.id != lastId || mb.failed; });
      if (mb.failed) { std::cout << "Plus d'images de la camera.\n"; break; }
      if (mb.id == lastId) {   // pas de nouvelle image : on reste réactif aux commandes
        l.unlock();
        drainCtl();
        TickMountAuto(imu, mc);
        if (quit || g_parentGone) break;
        continue;
      }
      frame = mb.frame;   // partage du tampon, aucune copie de pixels
      tRead = mb.tRead;
      tSm = mb.tSmooth;
      lastId = mb.id;
    }
    ++frames;
    if (tRead - lastPv >= 0.066 && ar6pv::Wanted() == ar6pv::kStereo) {   // apercu pour l'application / Unity : 15 images/s, seulement s'il est demande
      lastPv = tRead;
      ar6pv::Shared().Publish(frame);
    }
    auto now = std::chrono::steady_clock::now();
    double dt = std::chrono::duration<double>(now - tf).count();
    if (dt >= 1.0) {
      fps = frames / dt; frames = 0; tf = now;
      {  // un résumé par seconde dans la console et le journal (lu par l'application et par Unity)
        char sb[360];
        std::snprintf(sb, sizeof(sb), "camera %.1f fps | suivi %.1f fps | partage %.1f ms | decodage %.1f ms | tracking %s | %d points | delai camera %.1f ms (%s) | cpu %.0f%%\n",
                      g_camFps.load(), fps, g_shareMs.load(), g_decodeMs.load(),
                      tracking ? (vo && vo->state == StereoVO::kOk ? "OK" : "perdu/init") : "arrete", vo ? vo->nPoints() : 0,
                      camDelay * 1000.0, autoDelay ? "auto" : "manuel", ProcCpuPercent());
        std::cout << sb;
      }
    }

    int w = frame.cols / 2;
    cv::Mat left = frame(cv::Rect(0, 0, w, frame.rows));
    cv::Mat right = frame(cv::Rect(w, 0, w, frame.rows));
    if (swapEyes) std::swap(left, right);
    calSess.SetSwapped(swapEyes);
    if (calSess.WantsFrames()) calSess.Feed(left, right, tRead);   // calibration en cours : detection de la mire (une copie toutes les 120 ms au plus)
    WriteCalStatus(false);

    // Calibration exprimee dans le mode camera reellement utilise (taille, cadence, zoom du mode) : le mouvement et l'echelle sont justes meme si la
    // calibration a ete faite avec une autre resolution ou une autre cadence.
    if (haveCalib && (resolvedFor != cv::Size(w, frame.rows) || zoomDirty)) {
      const cv::Size eyeNow(w, frame.rows);
      const std::string key = ModeKey(eyeNow, FPS);
      const bool sameMode = calibRaw.size == eyeNow && (calibRaw.fps <= 0 || calibRaw.fps == FPS);
      zi.run = FPS;
      if (const ZoomEntry* ze = zoomStore.Find(key)) {
        curZoom = ze->zoom;
        zi.source = ze->source;
        zi.confirmed = ze->source == "manual" || ze->samples > 0;
        if (ze->source == "manual") zi.autoOn = false;
      } else if (sameMode) {
        curZoom = 1.0;
        zi.source = "calib";
        zi.confirmed = true;
      } else {
        curZoom = 1.0;
        zi.source = "none";
        zi.confirmed = false;
      }
      zi.zoom = curZoom;
      ModeRel rel;
      calib = ResolveCalib(calibRaw, eyeNow, curZoom, &rel);
      resolvedFor = eyeNow;
      zoomDirty = false;
      rectReady = false;
      trackReady = false;
      haveLast = false;
      delayEst.Reset();
      ExportCameraModel(calib, ExeDir() + "\\camera_model.json");   // Unity relit ce fichier : il doit decrire le mode utilise, pas celui de la calibration
      std::cout << "[calibration] mode " << key << " (calibre en " << ModeKey(calibRaw.size, calibRaw.fps > 0 ? calibRaw.fps : FPS) << ") : echelle image x"
                << rel.scale << ", zoom du mode x" << curZoom << " (" << zi.source << ")" << std::endl;
    }
    // (Re)construire la rectification si besoin (taille réelle de l'image)
    if (haveCalib && (!rectReady || rect.eye != cv::Size(w, frame.rows))) {
      std::string err;
      rectReady = MakeRectifier(calib, cv::Size(w, frame.rows), rect, err);
      if (!rectReady) {
        calibErr = err;
        std::cout << "Rectification impossible : " << err << "\n";
        haveCalib = false;
        WriteCalibInfo(calibRaw, calib, nullptr, cv::Size(w, frame.rows), zi, err);
      } else {
        // Ces avertissements n'etaient jamais affiches : calibration faite dans un autre mode que celui utilise (echelle possiblement fausse).
        for (const auto& wn : rect.warnings) std::cout << "[calibration] ATTENTION : " << wn << std::endl;
        WriteCalibInfo(calibRaw, calib, &rect, cv::Size(w, frame.rows), zi, "");
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
    const bool needSmall = haveCalib && tracking;
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
        // Image sombre (typiquement 120 i/s : exposition courte, ou pièce peu éclairée) : peu de points accrochables et le suivi décroche.
        // Gain simple, le même pour les deux images (le suivi compare gauche et droite). Sans effet sur une image normalement exposée.
        {
          const double mean = cv::mean(sl)[0];
          if (mean < 70.0) {
            const double gain = std::min(3.0, 100.0 / std::max(mean, 20.0));
            cv::Mat ga, gb;
            sl.convertTo(ga, -1, gain);
            sr.convertTo(gb, -1, gain);
            sl = ga;
            sr = gb;
            static bool warned = false;
            if (!warned && gain > 1.5) {
              warned = true;
              std::cout << "[suivi] image sombre (luminosite moyenne " << (int)mean << "/255) : gain x" << gain
                        << " applique au suivi. A 120 i/s l'exposition est courte : plus de lumiere aide beaucoup.\n";
            }
          }
        }
        cv::remap(sl, tl, rectT.mapL1, rectT.mapL2, cv::INTER_LINEAR);
        cv::remap(sr, tr, rectT.mapR1, rectT.mapR2, cv::INTER_LINEAR);
      }
    }
    // ---- Tracking 6DoF ----
    // Premier alignement sur la gravite : il faut l'orientation de l'IMU, sinon on supposerait la camera de niveau et tout le monde serait penche
    // (c'etait la cause du monde « penche » au premier lancement). On attend l'IMU jusqu'a 3 s, puis on demarre quand meme.
    bool waitImu = false;
    if (tracking && haveCalib && !haveLast && pi >= 0 && !qok) {
      if (imuWaitT0 < 0) imuWaitT0 = tRead;
      if (tRead - imuWaitT0 < 3.0) { waitImu = true; trackMsg = "attente de l'IMU pour aligner le monde sur la gravite"; }
    }
    if (haveLast || qok) imuWaitT0 = -1;
    if (tracking && haveCalib && !waitImu) {
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
        // Inclinaison : le suivi visuel peut deriver en roulis / tangage. L'IMU connait le vrai « haut » : on ramene doucement le monde vers lui
        // (seulement quand la tete tourne lentement : le gyro et l'image ne sont pas pris au meme instant, a grande vitesse l'ecart serait faux).
        if (vo->state == StereoVO::kOk && haveLast && qok && pi >= 0 && Norm(gyr) < 0.35) {
          Vec3 upc = Rcb * ToMat3(q).Row(2);
          R_GV = GravityRefine(R_GV, snapRvc * upc, 0.02, 0.004);   // au plus ~0,2 deg par image, ~2 % de l'ecart
        }
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
        // Zoom du mode : rotation vue par la camera / rotation du gyro. Different de 1 = les focales supposees pour ce mode sont fausses (mode recadre).
        if (zi.autoOn && pi >= 0 && vo->state == StereoVO::kOk && tRead - lastZoomT > 1.0) {
          lastZoomT = tRead;
          std::deque<GyroSample> zcopy;
          { std::lock_guard<std::mutex> l(imu.m); zcopy = imu.gyroBuf; }
          double r = 1, sp = 1, mo = 0;
          int nz = 0;
          if (delayEst.EstimateZoom(zcopy, Rcb, camDelay, r, sp, nz, mo)) {
            zi.n = nz; zi.motion = mo; zi.spread = sp; zi.residual = r;
            const bool reliable = sp < 0.25 && r > 0.3 && r < 4.0;
            if (reliable && std::fabs(r - 1.0) <= 0.04) {
              if (!zi.confirmed && zi.source != "calib") {
                ZoomEntry e;
                e.key = ModeKey(resolvedFor, FPS);
                e.zoom = curZoom;
                e.source = "auto";
                e.samples = nz;
                zoomStore.Set(e);
                zoomStore.Save(zoomPath);
                std::cout << "[calibration] zoom du mode " << e.key << " confirme : x" << curZoom << std::endl;
              }
              zi.confirmed = true;
            } else if (reliable && zi.source != "calib" && zi.source != "manual") {
              ZoomEntry e;
              e.key = ModeKey(resolvedFor, FPS);
              e.zoom = std::max(0.25, std::min(4.0, curZoom * r));
              e.source = "auto";
              e.samples = 0;   // appris mais pas encore verifie : « verifie » = une mesure suivante (avec ce zoom) retombe a +-4 % de 1
              zoomStore.Set(e);
              zoomStore.Save(zoomPath);
              zoomDirty = true;   // les focales sont recalculees, le suivi redemarre une fois
              std::cout << "[calibration] zoom du mode " << e.key << " appris : x" << e.zoom << " (rotation vue/gyro = " << r << ", " << nz << " pas)" << std::endl;
            }
          } else {
            zi.n = nz; zi.motion = mo;
          }
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
      if (tracking && vo) ar6trk::Update(vo->state == StereoVO::kOk ? 1 : vo->state == StereoVO::kLost ? 2 : 0, poseP, poseR, vo->nPoints(), vo->nInliers, floorValid, floorZ, voMs);
      // Sol : plus bas plan horizontal dense parmi les points 3D suivis (estimé toutes les 0,5 s)
      if (tracking && vo && vo->state == StereoVO::kOk && !floorLocked && (tRead - lastFloorT) > 0.5) {
        lastFloorT = tRead;
        std::vector<double> zs;
        const auto& pts = vo->Points3D();
        zs.reserve(pts.size());
        for (const auto& pp : pts) zs.push_back(R_GV.m[2][0] * pp.x + R_GV.m[2][1] * pp.y + R_GV.m[2][2] * pp.z);
        floorF.Add(floorMap.Update(zs, poseP.z));   // plus bas groupe dense des hauteurs vues depuis le debut (voir floor.h)
        floorValid = floorF.Valid();
        if (floorValid) floorZ = floorF.Z();
      }
      // Sans IMU il n'y a pas de thread de publication : on envoie à la cadence de la caméra
      if (pi < 0 && udp.IsOpen() && tracking && vo) {
        auto pkt = PackPose(fl, udpSeq++, po.p, QuatFromMat3(po.R), po.vG, po.wG, floorZ, QpcSeconds() + predS, (float)camDelay);
        udp.Send(pkt.data(), (int)pkt.size());
      }
    }

    if (!tracking) ar6trk::Stop();   // la vue du suivi (application) affiche « arrete »


    drainCtl();   // commandes de l'application ou d'Unity (un caractere, ou une ligne de texte pour les apercus)
    if (quit || g_parentGone) break;
    while (!cmdQ.empty()) {
      const std::string cl = cmdQ.front();
      cmdQ.pop_front();
      if (HandleMountLine(cl, mc)) continue;
      if (HandleCal(cl)) continue;
      if (cl.size() != 1) continue;
      int k = (unsigned char)cl[0];
      if (k == 'V') k = 'v';   // V sol, Z yaw : commandes d'Unity
      if (k == 'Z') k = 'z';
      if (k == 'T') { tracking = false; haveLast = false; poseValid = false; k = 't'; }   // T = recentrer : arret puis redemarrage
      if (k == 'x') swapEyes = !swapEyes;
      if (k == 'c') {
        if (haveCalib && rectReady) {
          cv::Mat ca, cb;  // rectification pleine resolution, seulement pour ce controle
          cv::remap(left, ca, rect.mapL1, rect.mapL2, cv::INTER_LINEAR);
          cv::remap(right, cb, rect.mapR1, rect.mapR2, cv::INTER_LINEAR);
          checkMsg = CheckRectification(ca, cb);
        } else {
          checkMsg = "pas de calibration chargee";
        }
      }
      HandleMountCmd(k, imu, mc, acc, gyr);   // g, f (calibration du montage de l'IMU), 1-6, 0 et z
      if (k == 't') {
        if (!tracking) {
          if (!haveCalib) {
            trackMsg = "le tracking a besoin d'une calibration chargee";
          } else {
            tracking = true;
            haveLast = false;
            poseValid = false;
            InvalidateSnap(vsnap);
            delayEst.Reset();
            havePrevOk = false;
            if (vo) vo->Reset();
            trackMsg = "tracking demarre : l'origine est ici, immobile 1 s";
            ar6trk::Reset();
            floorF.Reset();
            floorMap.Reset();
            floorValid = false;
            floorLocked = false;
            imuWaitT0 = -1;
          }
        } else {
          tracking = false;
          haveLast = false;
          poseValid = false;
          InvalidateSnap(vsnap);
          trackMsg = "tracking arrete";
          ar6trk::Stop();
        }
      }
      if (k == 'u') usePrior = !usePrior;
      if (k == 'v' && floorValid) floorLocked = !floorLocked;
      if (k == '[') { camDelay = std::max(0.0, camDelay - 0.005); autoDelay = false; }   // reglage manuel : coupe l'automatique
      if (k == ']') { camDelay = std::min(0.2, camDelay + 0.005); autoDelay = false; }
      if (k == 'a') autoDelay = !autoDelay;
      if (k == '-') predS = std::max(0.0, predS - 0.005);
      if (k == '=') predS = std::min(0.1, predS + 0.005);
    }
    TickMountAuto(imu, mc);
    if (mc.msg != lastMountMsg) { lastMountMsg = mc.msg; std::cout << "[montage] " << mc.msg << std::endl; }
    SetViewMsg(mc.msg, mc.au.Active());
    if (trackMsg != lastTrackMsg) { lastTrackMsg = trackMsg; std::cout << "[suivi] " << trackMsg << std::endl; }
    if (checkMsg != lastCheckMsg) { lastCheckMsg = checkMsg; std::cout << "[calibration] " << checkMsg << std::endl; }
  }

  g_run = false;
  if (pump.joinable()) pump.join();
  if (capThread.joinable()) capThread.join();
  if (th.joinable()) th.join();
  return 0;
}
