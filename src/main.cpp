// ar6dof_probe : étape 1 du casque AR open source.
// Vérifie le matériel ET la calibration avant de brancher le tracking :
//  - choix de la caméra stéréo et de l'IMU dans une liste
//  - chargement d'une calibration faite ailleurs (dossier "calibration")
//  - aperçu gauche/droite, brut ou rectifié
//  - lecture IMU en direct : valeurs, cadence réelle, checksums
//  Touches : s = sauver une paire brute | r = brut/rectifié | c = mesurer la
//  qualité de la calibration | x = inverser gauche/droite | q = quitter
#include <windows.h>

#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <utility>

#include <opencv2/opencv.hpp>

#include "calib.h"
#include "devices.h"
#include "imu_parser.h"
#include "serial_win.h"

struct ImuState {
  std::mutex m;
  float acc[3] = {0, 0, 0}, gyr[3] = {0, 0, 0};
  double rate_acc = 0, rate_gyr = 0;
  uint64_t bytes = 0, ok = 0, bad = 0;
  bool port_error = false;
};

static std::atomic<bool> g_run{true};

static void ImuThread(std::string port, ImuState* st) {
  SerialPort sp;
  if (!sp.open(port)) {
    std::lock_guard<std::mutex> l(st->m);
    st->port_error = true;
    return;
  }
  ImuParser parser;
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
        if (s.type == 1) { for (int i = 0; i < 3; i++) st->acc[i] = s.v[i]; ++n_acc; }
        else             { for (int i = 0; i < 3; i++) st->gyr[i] = s.v[i]; ++n_gyr; }
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
  std::string dir = AskStr("\nDossier de calibration RTAB-Map ('-' = aucune)", "calibration");
  if (dir != "-") {
    std::string err;
    if (LoadRtabmapStereo(dir, calib, err)) {
      haveCalib = true;
      std::cout << "Calibration chargee (" << calib.files << "), faite en "
                << calib.size.width << "x" << calib.size.height << " par oeil.\n";
    } else {
      std::cout << "Calibration NON chargee : " << err << "\n";
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
  std::thread th;
  if (pi >= 0) th = std::thread(ImuThread, ports[pi], &imu);

  std::filesystem::create_directories("capture");
  int saved = 0, frames = 0, fails = 0;
  double fps = 0;
  auto tf = std::chrono::steady_clock::now();
  cv::Mat frame;
  Rectifier rect;
  bool rectReady = false, showRect = haveCalib, swapEyes = false;
  std::string checkMsg = "touche 'c' : mesurer la qualite de la calibration (scene riche en details)";
  std::cout << "\nFenetre ouverte. s=sauver paire, r=brut/rectifie, c=controle calib, x=inverser G/D, q=quitter\n";

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
    // Lignes horizontales : en rectifie, un meme point doit etre sur la meme ligne
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
      put("pas de calibration chargee", {0, 165, 255});
    }
    if (pi < 0) {
      put("IMU: non selectionnee");
    } else {
      std::lock_guard<std::mutex> l(imu.m);
      if (imu.port_error) {
        put("IMU: erreur d'ouverture/lecture du port", {0, 0, 255});
      } else if (imu.bytes > 0 && imu.ok == 0) {
        put("IMU: octets recus mais 0 paquet valide (firmware v2 flashe ?)", {0, 0, 255});
      } else {
        float na = std::sqrt(imu.acc[0] * imu.acc[0] + imu.acc[1] * imu.acc[1] + imu.acc[2] * imu.acc[2]);
        std::snprintf(line, sizeof(line), "IMU accel %.1f Hz  |a|=%.2f m/s2 (attendu ~9.81 a l'arret)", imu.rate_acc, na);
        put(line);
        std::snprintf(line, sizeof(line), "   a = %+.3f %+.3f %+.3f", imu.acc[0], imu.acc[1], imu.acc[2]);
        put(line);
        std::snprintf(line, sizeof(line), "IMU gyro  %.1f Hz   w = %+.4f %+.4f %+.4f rad/s", imu.rate_gyr,
                      imu.gyr[0], imu.gyr[1], imu.gyr[2]);
        put(line);
        std::snprintf(line, sizeof(line), "paquets ok=%llu  checksum ko=%llu",
                      (unsigned long long)imu.ok, (unsigned long long)imu.bad);
        put(line);
      }
    }
    cv::imshow("ar6dof_probe", disp);

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
  }

  g_run = false;
  if (th.joinable()) th.join();
  return 0;
}
