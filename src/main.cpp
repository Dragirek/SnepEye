// ar6dof_probe : étape 1 du casque AR open source.
// Vérifie le matériel, la calibration caméra et l'orientation de l'IMU.
//
//  Fenêtre "ar6dof_probe" (caméras) :
//    s = sauver une paire brute | r = brut/rectifié | c = qualité de la calibration
//    x = inverser gauche/droite | q = quitter
//  Fenêtre "orientation" (IMU en 3D) :
//    g puis f = calibration guidée du montage (voir l'aide à l'écran)
//    1/2 = -/+90 deg autour de X (avant)  3/4 = autour de Y (gauche)  5/6 = autour de Z (haut)
//    0 = réinitialiser le montage | z = remettre le yaw à zéro
#include <windows.h>

#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <utility>

#include <opencv2/opencv.hpp>

#include "calib.h"
#include "devices.h"
#include "imu_parser.h"
#include "orientation.h"
#include "serial_win.h"

struct ImuState {
  std::mutex m;
  Vec3 acc_s, gyr_s;          // valeurs brutes, repère du capteur
  Mat3 mount;                 // capteur -> casque
  Quat q;                     // orientation casque -> monde
  bool q_ok = false;
  bool reinit = false, zero_yaw = false;
  double rate_acc = 0, rate_gyr = 0;
  uint64_t bytes = 0, ok = 0, bad = 0;
  bool port_error = false;
};

static std::atomic<bool> g_run{true};

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

static void ImuThread(std::string port, ImuState* st) {
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
  uint64_t n_acc = 0, n_gyr = 0, a0 = 0, g0 = 0;
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
        if (st->reinit) { filt.Reset(); haveAcc = false; haveT = false; st->reinit = false; }
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

static int AskInt(const std::string& q, int def, int lo, int hi) {
  while (true) {
    std::cout << q << " [" << def << "] : ";
    std::string line;
    std::getline(std::cin, line);
    if (line.empty()) return def;
    try {
      int v = std::stoi(line);
      if (v >= lo && v <= hi) return v;
    } catch (...) {}
    std::cout << "  valeur invalide\n";
  }
}

static std::string AskStr(const std::string& q, const std::string& def) {
  std::cout << q << " [" << def << "] : ";
  std::string line;
  std::getline(std::cin, line);
  return line.empty() ? def : line;
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

int main() {
  // Sans ça, l'ouverture d'une caméra avec le backend MSMF peut prendre
  // plusieurs dizaines de secondes.
  _putenv_s("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");

  std::cout << "=== ar6dof_probe ===\n\nCameras :\n";
  auto cams = ListCameras();
  if (cams.empty()) { std::cout << "  aucune camera trouvee\n"; return 1; }
  for (size_t i = 0; i < cams.size(); i++) std::cout << "  [" << i << "] " << cams[i] << "\n";
  int ci = AskInt("Camera stereo", 0, 0, (int)cams.size() - 1);

  std::cout << "\nPorts serie (IMU) :\n";
  auto ports = ListComPorts();
  for (size_t i = 0; i < ports.size(); i++) std::cout << "  [" << i << "] " << ports[i] << "\n";
  std::cout << "  [-1] pas d'IMU\n";
  int pi = AskInt("Port IMU", ports.empty() ? -1 : 0, -1, (int)ports.size() - 1);

  std::cout << "\nImage cote a cote (gauche|droite) : largeur TOTALE des 2 yeux et hauteur.\n";
  int W = AskInt("Largeur totale", 3200, 320, 8000);
  int H = AskInt("Hauteur", 1200, 240, 4000);

  // Calibration faite ailleurs (RTAB-Map) : on la charge, on ne la fabrique pas.
  StereoCalib calib;
  bool haveCalib = false;
  std::string calibErr = "aucun dossier de calibration";
  std::string dir = AskStr("\nDossier de calibration RTAB-Map ('-' = aucune)", ExeDir() + "\\calibration");
  if (dir != "-") {
    if (LoadRtabmapStereo(dir, calib, calibErr)) {
      haveCalib = true;
      std::cout << "Calibration chargee (" << calib.files << "), faite en "
                << calib.size.width << "x" << calib.size.height << " par oeil.\n";
    } else {
      std::cout << "Calibration NON chargee : " << calibErr << "\n";
    }
  }

  cv::VideoCapture cap(ci, cv::CAP_MSMF);
  if (!cap.isOpened()) { std::cout << "Impossible d'ouvrir la camera.\n"; return 1; }
  cap.set(cv::CAP_PROP_FOURCC, cv::VideoWriter::fourcc('M', 'J', 'P', 'G'));
  cap.set(cv::CAP_PROP_FRAME_WIDTH, W);
  cap.set(cv::CAP_PROP_FRAME_HEIGHT, H);
  std::cout << "Resolution negociee : " << cap.get(cv::CAP_PROP_FRAME_WIDTH) << "x"
            << cap.get(cv::CAP_PROP_FRAME_HEIGHT)
            << "  (si differente de ce que tu as demande, ce mode n'existe pas)\n";

  ImuState imu;
  imu.mount = LoadMount();
  std::thread th;
  if (pi >= 0) th = std::thread(ImuThread, ports[pi], &imu);

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
  std::cout << "\nFenetres ouvertes. Aide des touches : voir l'en-tete du programme / README.\n";

  while (true) {
    if (!cap.read(frame) || frame.empty()) {
      if (++fails > 100) { std::cout << "Plus d'images de la camera.\n"; break; }
      continue;
    }
    fails = 0;
    ++frames;
    auto now = std::chrono::steady_clock::now();
    double dt = std::chrono::duration<double>(now - tf).count();
    if (dt >= 1.0) { fps = frames / dt; frames = 0; tf = now; }

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
    cv::Mat rl, rr, shown;
    if (showRect && rectReady) {
      cv::remap(left, rl, rect.mapL1, rect.mapL2, cv::INTER_LINEAR);
      cv::remap(right, rr, rect.mapR1, rect.mapR2, cv::INTER_LINEAR);
      cv::hconcat(rl, rr, shown);
    } else {
      cv::hconcat(left, right, shown);
    }

    double scale = std::min(1.0, 1600.0 / shown.cols);
    cv::Mat disp;
    cv::resize(shown, disp, cv::Size(), scale, scale);
    if (disp.channels() == 1) cv::cvtColor(disp, disp, cv::COLOR_GRAY2BGR);
    for (int y = 40; y < disp.rows; y += 60)
      cv::line(disp, {0, y}, {disp.cols, y}, {0, 255, 0}, 1);
    cv::line(disp, {disp.cols / 2, 0}, {disp.cols / 2, disp.rows}, {0, 0, 255}, 2);

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

    char line[256];
    int y = 24;
    auto put = [&](const std::string& s, cv::Scalar c = {255, 255, 255}) {
      cv::putText(disp, s, {10, y}, cv::FONT_HERSHEY_SIMPLEX, 0.6, {0, 0, 0}, 3);
      cv::putText(disp, s, {10, y}, cv::FONT_HERSHEY_SIMPLEX, 0.6, c, 1);
      y += 24;
    };
    std::snprintf(line, sizeof(line), "cam %dx%d (oeil %dx%d) %.1f fps  paires: %d  vue: %s%s",
                  frame.cols, frame.rows, w, frame.rows, fps, saved,
                  (showRect && rectReady) ? "RECTIFIEE" : "brute", swapEyes ? "  [G/D inverses]" : "");
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

    int k = cv::waitKey(1);
    if (k == 'q' || k == 27) break;
    if (k == 'r' && haveCalib && rectReady) showRect = !showRect;
    if (k == 'x') swapEyes = !swapEyes;
    if (k == 'c') {
      if (!rl.empty()) checkMsg = CheckRectification(rl, rr);
      else checkMsg = "active d'abord la vue rectifiee (touche r)";
    }
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
  }

  g_run = false;
  if (th.joinable()) th.join();
  return 0;
}
