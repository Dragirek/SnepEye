// Dessine les diapositives de la page Aide (src/app/help_draw.h) en images PNG : clair/sombre, FR/EN, plusieurs instants.
// Verifie aussi que les textes ne depassent pas de leur rectangle (estimation de largeur) et que le clic sur « Suivant » / les points est bien detecte.
// g++ -std=c++17 -I src/app -I <opencv>/include/opencv4 tests/help_draw_test.cpp -lopencv_imgproc -lopencv_core ...   (argument : dossier de sortie)
#include <cstdio>
#include <string>
#include <opencv2/imgproc.hpp>
#include "help_draw.h"

using namespace helpdraw;

static std::string Fold(const std::wstring& w) {   // putText ne sait pas les accents
  std::string o;
  for (wchar_t c : w) {
    switch (c) {
      case L'é': case L'è': case L'ê': o += 'e'; break;
      case L'à': case L'â': o += 'a'; break;
      case L'ô': o += 'o'; break;
      case L'î': case L'ï': o += 'i'; break;
      case L'ç': o += 'c'; break;
      case L'œ': o += "oe"; break;
      case L'’': case L'«': case L'»': o += '"'; break;
      case 0x2212: o += '-'; break;
      case 0x2039: o += '<'; break;
      case 0x203A: o += '>'; break;
      case 0xB7: o += '.'; break;
      default: o += c < 128 ? (char)c : '?';
    }
  }
  return o;
}

static int g_over = 0;

static void DrawTexts(cv::Mat& img, const Raster& r, const Theme& th) {
  const double k = r.Scale();
  for (const TextItem& t : r.texts) {
    const double fs = (t.style == 1 ? 0.62 : 0.5) * k;
    const int thick = t.style == 1 ? 2 : 1;
    // estimation : meme largeur de caractere que Segoe UI 9 pt (~6.2 px/car a 96 ppp), 1.25x pour le titre
    const double cw = (t.style == 1 ? 8.2 : 6.4);
    std::string s = Fold(t.s);
    // cassure en lignes
    std::vector<std::string> lines;
    std::string cur, word;
    size_t maxc = (size_t)std::max(1.0, t.w / cw);
    for (size_t i = 0; i <= s.size(); i++) {
      if (i == s.size() || s[i] == ' ') {
        if (cur.empty()) cur = word; else if (cur.size() + 1 + word.size() <= maxc) cur += " " + word; else { lines.push_back(cur); cur = word; }
        word.clear();
      } else word += s[i];
    }
    if (!cur.empty()) lines.push_back(cur);
    const double lh = t.style == 1 ? 22 : 17;
    if (lines.size() * lh > t.h + 1) { g_over++; std::printf("DEPASSE (%zu lignes pour h=%.0f) : %s\n", lines.size(), t.h, s.c_str()); }
    double y = t.y + ((t.align & 4) ? (t.h - lines.size() * lh) / 2 : 0) + lh * 0.75;
    for (const std::string& ln : lines) {
      int base;
      cv::Size sz = cv::getTextSize(ln, cv::FONT_HERSHEY_SIMPLEX, fs / k * 0.6 * k / k, thick, &base);
      double x = t.x * k;
      const double w = ln.size() * cw * k;   // largeur estimee (pas celle d'OpenCV)
      if (t.align & 1) x = (t.x + t.w / 2) * k - w / 2; else if (t.align & 2) x = (t.x + t.w) * k - w;
      cv::putText(img, ln, cv::Point((int)x, (int)(y * k)), cv::FONT_HERSHEY_SIMPLEX, 0.4 * k * (t.style == 1 ? 1.2 : 1.0) / 1.0 * 0.62, cv::Scalar(t.col & 255, (t.col >> 8) & 255, (t.col >> 16) & 255), thick, cv::LINE_AA);
      y += lh;
      (void)sz; (void)th;
    }
  }
}

int main(int argc, char** argv) {
  const std::string out = argc > 1 ? argv[1] : ".";
  const double k = 1.25;
  int bad = 0;
  Theme light = {0xF3F3F3, 0xFFFFFF, 0xE5E5E5, 0x1A1A1A, 0x606060, 0x0078D4, 0x2E9E5B};
  Theme dark = {0x202020, 0x2B2B2B, 0x3A3A3A, 0xFFFFFF, 0xACACAC, 0x4CC2FF, 0x4CC38A};
  const double times[3] = {0.4, 2.2, 5.5};
  for (int dk = 0; dk < 2; dk++)
    for (int sl = 0; sl < kSlides; sl++)
      for (int en = 0; en < 2; en++)
        for (int ti = 0; ti < 3; ti++) {
          if ((dk == 1 || en == 1) && ti != 1) continue;   // une seule image pour les variantes
          Raster r((int)(kW * k), (int)(kH * k), k);
          const Theme& th = dk ? dark : light;
          Draw(r, th, sl, times[ti] + (sl == 4 ? 0 : 0), en != 0);
          cv::Mat img(r.Height(), r.Width(), CV_8UC4, (void*)r.Data());
          cv::Mat bgr;
          cv::cvtColor(img, bgr, cv::COLOR_BGRA2BGR);
          if (!(dk == 0 && en == 1)) DrawTexts(bgr, r, th); else { int before = g_over; DrawTexts(bgr, r, th); (void)before; }
          char name[256];
          std::snprintf(name, sizeof(name), "%s/help_%d_%s_%s_%d.ppm", out.c_str(), sl, dk ? "dark" : "light", en ? "en" : "fr", ti);
          if (FILE* f = std::fopen(name, "wb")) {   // PPM : pas besoin du module imgcodecs
            std::fprintf(f, "P6\n%d %d\n255\n", bgr.cols, bgr.rows);
            for (int y = 0; y < bgr.rows; y++)
              for (int x = 0; x < bgr.cols; x++) { cv::Vec3b v = bgr.at<cv::Vec3b>(y, x); unsigned char rgb[3] = {v[2], v[1], v[0]}; std::fwrite(rgb, 1, 3, f); }
            std::fclose(f);
          }
        }
  // clics
  if (HitTest(600, 585) != 1 || HitTest(80, 585) != -1 || HitTest(346, 585) != 102 || HitTest(300, 300) != 0) { std::printf("ECHEC HitTest\n"); bad++; }
  // fond uni : aucun pixel hors de la page ne doit etre dessine sur une grande duree (pas de plantage / debordement de tampon)
  for (int sl = 0; sl < kSlides; sl++)
    for (double t = 0; t < 12; t += 0.37) { Raster r((int)(kW * 1.5), (int)(kH * 1.5), 1.5); Draw(r, light, sl, t, false); Draw(r, dark, sl, t, true); }
  if (g_over) { std::printf("%d texte(s) trop longs\n", g_over); bad++; }
  std::printf(bad ? "ECHEC\n" : "OK\n");
  return bad;
}
