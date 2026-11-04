#pragma once
// Graphique de l'IMU pour l'application (aperçu 800x300 dans la mémoire partagée, source « IMU ») : un casque en 3D qui suit l'orientation
// mesurée (montage et filtre appliqués), ses trois axes, les angles en clair et les consignes de la calibration du montage.
// Si le modèle penche dans le mauvais sens quand tu penches le casque, le montage est inversé (bouton « Inverser avant / arrière »).
#include <cstdio>
#include <string>
#include <vector>

#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>

#include "orientation.h"

namespace ar6imu {

// Police OpenCV : ASCII seulement. Les lettres accentuées (UTF-8) deviennent leur lettre de base, le reste est ignoré.
inline std::string Ascii(const std::string& s) {
  std::string o;
  for (size_t i = 0; i < s.size(); i++) {
    const unsigned char c = (unsigned char)s[i];
    if (c < 0x80) { o += (char)c; continue; }
    if (c == 0xC3 && i + 1 < s.size()) {
      const unsigned char d = (unsigned char)s[++i];
      char r = '?';
      if (d >= 0x80 && d <= 0x85) r = 'A';
      else if (d == 0x87) r = 'C';
      else if (d >= 0x88 && d <= 0x8B) r = 'E';
      else if (d >= 0x8C && d <= 0x8F) r = 'I';
      else if (d >= 0x92 && d <= 0x96) r = 'O';
      else if (d >= 0x99 && d <= 0x9C) r = 'U';
      else if (d >= 0xA0 && d <= 0xA5) r = 'a';
      else if (d == 0xA7) r = 'c';
      else if (d >= 0xA8 && d <= 0xAB) r = 'e';
      else if (d >= 0xAC && d <= 0xAF) r = 'i';
      else if (d >= 0xB2 && d <= 0xB6) r = 'o';
      else if (d >= 0xB9 && d <= 0xBC) r = 'u';
      o += r;
    } else if (c >= 0xC0) {
      // autre caractère UTF-8 : on saute ses octets de suite
      while (i + 1 < s.size() && ((unsigned char)s[i + 1] & 0xC0) == 0x80) i++;
    }
  }
  return o;
}

// Coupe un texte en lignes de 'width' caractères au plus (aux espaces).
inline std::vector<std::string> Wrap(const std::string& s, size_t width, size_t maxLines) {
  std::vector<std::string> lines;
  std::string cur, word;
  auto flushWord = [&]() {
    if (word.empty()) return;
    if (!cur.empty() && cur.size() + 1 + word.size() > width) { lines.push_back(cur); cur.clear(); }
    if (!cur.empty()) cur += ' ';
    cur += word;
    word.clear();
  };
  for (char c : s) {
    if (c == ' ') flushWord(); else word += c;
  }
  flushWord();
  if (!cur.empty()) lines.push_back(cur);
  if (lines.size() > maxLines) lines.resize(maxLines);
  return lines;
}

// q : casque -> monde (repère X avant, Y gauche, Z haut). qok faux : pas encore de mesure.
inline cv::Mat Draw(const Quat& q, bool qok, const std::string& msg, bool calibrating) {
  cv::Mat img(300, 800, CV_8UC3, cv::Scalar(26, 26, 26));
  const View v = MakeView(62.0, 16.0, 1.15);
  const double foc = 560.0, cx = 205.0, cy = 160.0;
  auto proj = [&](const Vec3& p, cv::Point& o) {
    double sx = 0, sy = 0;
    if (!Project(v, p, foc, cx, cy, sx, sy)) return false;
    o = cv::Point((int)(sx + 0.5), (int)(sy + 0.5));
    return true;
  };
  auto seg = [&](const Vec3& a, const Vec3& b, const cv::Scalar& col, int th) {
    cv::Point pa, pb;
    if (proj(a, pa) && proj(b, pb)) cv::line(img, pa, pb, col, th, cv::LINE_AA);
  };

  // sol : une grille au niveau des pieds du modèle
  for (int i = -4; i <= 4; i++) {
    const double t = i * 0.08;
    seg({t, -0.32, -0.14}, {t, 0.32, -0.14}, cv::Scalar(52, 52, 52), 1);
    seg({-0.32, t, -0.14}, {0.32, t, -0.14}, cv::Scalar(52, 52, 52), 1);
  }

  const Mat3 R = qok ? ToMat3(q) : Mat3();
  // casque : un boîtier (avant = +X) ; la face avant est en couleur
  const double hx = 0.10, hy = 0.075, hz = 0.04;
  Vec3 c[8];
  for (int i = 0; i < 8; i++) c[i] = R * Vec3{(i & 1) ? hx : -hx, (i & 2) ? hy : -hy, (i & 4) ? hz : -hz};
  const int edges[12][2] = {{0, 1}, {2, 3}, {4, 5}, {6, 7}, {0, 2}, {1, 3}, {4, 6}, {5, 7}, {0, 4}, {1, 5}, {2, 6}, {3, 7}};
  for (const auto& e : edges) {
    const bool front = (e[0] & 1) && (e[1] & 1);   // les deux bouts sur la face avant
    seg(c[e[0]], c[e[1]], front ? cv::Scalar(40, 150, 245) : cv::Scalar(190, 190, 190), front ? 3 : 2);
  }
  // axes : avant (rouge), gauche (bleu), haut (vert)
  struct Ax { Vec3 d; cv::Scalar col; const char* name; } axes[3] = {
      {{0.24, 0, 0}, cv::Scalar(70, 70, 235), "AVANT"}, {{0, 0.20, 0}, cv::Scalar(235, 150, 60), "GAUCHE"}, {{0, 0, 0.19}, cv::Scalar(90, 210, 90), "HAUT"}};
  for (const auto& a : axes) {
    const Vec3 e = R * a.d;
    seg({0, 0, 0}, e, a.col, 2);
    cv::Point pe;
    if (proj(e, pe)) cv::putText(img, a.name, pe + cv::Point(5, -4), cv::FONT_HERSHEY_SIMPLEX, 0.5, a.col, 1, cv::LINE_AA);
  }

  // angles en clair
  char b[160];
  int y = 36;
  const cv::Scalar white(235, 235, 235), grey(160, 160, 160), accent(40, 150, 245);
  if (!qok) {
    cv::putText(img, "IMU : pas de mesure", cv::Point(420, y), cv::FONT_HERSHEY_SIMPLEX, 0.7, grey, 1, cv::LINE_AA);
  } else {
    double roll = 0, pitch = 0, yaw = 0;
    ToEuler(q, roll, pitch, yaw);
    const double r2d = 180.0 / 3.14159265358979323846;
    roll *= r2d; pitch *= r2d; yaw *= r2d;
    std::snprintf(b, sizeof(b), "Tangage %+.0f deg : %s", pitch, pitch > 4 ? "nez vers le BAS" : pitch < -4 ? "nez vers le HAUT" : "a plat");
    cv::putText(img, b, cv::Point(420, y), cv::FONT_HERSHEY_SIMPLEX, 0.62, white, 1, cv::LINE_AA);
    y += 30;
    std::snprintf(b, sizeof(b), "Roulis  %+.0f deg : %s", roll, roll > 4 ? "penche a DROITE" : roll < -4 ? "penche a GAUCHE" : "droit");
    cv::putText(img, b, cv::Point(420, y), cv::FONT_HERSHEY_SIMPLEX, 0.62, white, 1, cv::LINE_AA);
    y += 30;
    std::snprintf(b, sizeof(b), "Cap     %+.0f deg", yaw);
    cv::putText(img, b, cv::Point(420, y), cv::FONT_HERSHEY_SIMPLEX, 0.62, white, 1, cv::LINE_AA);
    y += 26;
  }

  // consignes de la calibration du montage (ou dernier message)
  y += 14;
  cv::putText(img, calibrating ? "CALIBRATION DU MONTAGE" : "Montage", cv::Point(420, y), cv::FONT_HERSHEY_SIMPLEX, 0.55, calibrating ? accent : grey, 1, cv::LINE_AA);
  y += 24;
  const auto lines = Wrap(Ascii(msg), 41, 6);
  for (const auto& l : lines) {
    cv::putText(img, l, cv::Point(420, y), cv::FONT_HERSHEY_SIMPLEX, 0.5, calibrating ? white : grey, 1, cv::LINE_AA);
    y += 21;
  }
  cv::putText(img, "Le modele doit bouger comme ton casque", cv::Point(420, 288), cv::FONT_HERSHEY_SIMPLEX, 0.45, grey, 1, cv::LINE_AA);
  return img;
}

}  // namespace ar6imu
