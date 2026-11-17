// Page « Aide » de ar6dof_app : diagrammes animés (style présentation d'entreprise) qui montrent, pour chaque choix, l'option conseillée.
// Aucun code Windows ici : tout est dessiné par un petit rasteriseur logiciel (bords lissés par distance signée), donc testable sous Linux
// (tests/help_draw_test.cpp écrit les images). L'application Windows n'a plus qu'à copier le tampon de pixels puis à écrire les textes
// (Raster::texts) avec ses polices, par-dessus.
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <string>
#include <vector>

namespace helpdraw {

typedef uint32_t Rgb;                       // 0xRRGGBB
const Rgb kNone = 0xFF000000u;              // « pas de couleur » : la forme n'est pas remplie / pas de contour

inline Rgb Mix(Rgb a, Rgb b, double t) {    // t = 0 -> a, t = 1 -> b
  t = std::min(1.0, std::max(0.0, t));
  auto ch = [&](int sh) { return (uint32_t)std::lround(((a >> sh) & 255) * (1 - t) + ((b >> sh) & 255) * t); };
  return (ch(16) << 16) | (ch(8) << 8) | ch(0);
}

struct Pt { double x, y; };

struct TextItem {
  double x, y, w, h;      // rectangle en unités logiques
  std::wstring s;
  int style;              // 0 texte normal, 1 titre
  Rgb col;
  int align;              // bits : 1 = centré, 2 = à droite, 4 = centré verticalement
};

// Rasteriseur : tampon BGRA (ordre des octets d'un DIB 32 bits), k = pixels par unité logique.
class Raster {
 public:
  Raster(int wpx, int hpx, double k) : w_(wpx), h_(hpx), k_(k), px_((size_t)wpx * hpx * 4, 255) {}
  int Width() const { return w_; }
  int Height() const { return h_; }
  double Scale() const { return k_; }
  const uint8_t* Data() const { return px_.data(); }
  std::vector<TextItem> texts;

  void Clear(Rgb c) {
    for (size_t i = 0; i < (size_t)w_ * h_; i++) { px_[i * 4] = c & 255; px_[i * 4 + 1] = (c >> 8) & 255; px_[i * 4 + 2] = (c >> 16) & 255; px_[i * 4 + 3] = 255; }
    texts.clear();
  }

  // Rectangle à coins arrondis
  void Box(double x, double y, double w, double h, double r, Rgb fill, Rgb line = kNone, double lw = 1.5) {
    r = std::min(r, 0.5 * std::min(w, h));
    const double cx = x + 0.5 * w, cy = y + 0.5 * h, hx = 0.5 * w - r, hy = 0.5 * h - r;
    Paint(x, y, x + w, y + h, lw, [=](double px, double py) {
      const double qx = std::fabs(px - cx) - hx, qy = std::fabs(py - cy) - hy;
      return std::hypot(std::max(qx, 0.0), std::max(qy, 0.0)) + std::min(std::max(qx, qy), 0.0) - r;
    }, fill, line, lw);
  }

  // Ellipse (cercle si rx = ry)
  void Disc(double cx, double cy, double rx, double ry, Rgb fill, Rgb line = kNone, double lw = 1.5) {
    Paint(cx - rx, cy - ry, cx + rx, cy + ry, lw, [=](double px, double py) {
      const double ux = (px - cx) / rx, uy = (py - cy) / ry, l = std::hypot(ux, uy);
      const double g = std::hypot(ux / rx, uy / ry);   // gradient de l : distance approchée au 1er ordre
      return g > 1e-9 ? (l - 1.0) * l / g : -std::min(rx, ry);
    }, fill, line, lw);
  }

  void Line(double x1, double y1, double x2, double y2, Rgb col, double lw = 2.0) {
    const double dx = x2 - x1, dy = y2 - y1, L2 = dx * dx + dy * dy;
    Paint(std::min(x1, x2) - lw, std::min(y1, y2) - lw, std::max(x1, x2) + lw, std::max(y1, y2) + lw, 0, [=](double px, double py) {
      double t = L2 > 0 ? ((px - x1) * dx + (py - y1) * dy) / L2 : 0;
      t = std::min(1.0, std::max(0.0, t));
      return std::hypot(px - (x1 + t * dx), py - (y1 + t * dy)) - 0.5 * lw;   // capsule : bouts arrondis
    }, col, kNone, 0);
  }

  void Poly(const std::vector<Pt>& p, Rgb fill, Rgb line = kNone, double lw = 1.5) {
    if (p.size() < 3) return;
    double x0 = p[0].x, x1 = p[0].x, y0 = p[0].y, y1 = p[0].y;
    for (const Pt& q : p) { x0 = std::min(x0, q.x); x1 = std::max(x1, q.x); y0 = std::min(y0, q.y); y1 = std::max(y1, q.y); }
    const size_t n = p.size();
    Paint(x0, y0, x1, y1, lw, [&p, n](double px, double py) {
      double best = 1e18;
      bool in = false;
      for (size_t i = 0, j = n - 1; i < n; j = i++) {
        const double ax = p[j].x, ay = p[j].y, bx = p[i].x, by = p[i].y;
        if (((ay > py) != (by > py)) && (px < (bx - ax) * (py - ay) / (by - ay) + ax)) in = !in;
        const double ex = bx - ax, ey = by - ay, L2 = ex * ex + ey * ey;
        double t = L2 > 0 ? ((px - ax) * ex + (py - ay) * ey) / L2 : 0;
        t = std::min(1.0, std::max(0.0, t));
        best = std::min(best, std::hypot(px - (ax + t * ex), py - (ay + t * ey)));
      }
      return in ? -best : best;
    }, fill, line, lw);
  }

  void Text(double x, double y, double w, double h, const std::wstring& s, int style, Rgb col, int align = 0) { texts.push_back({x, y, w, h, s, style, col, align}); }

 private:
  int w_, h_;
  double k_;
  std::vector<uint8_t> px_;

  void Blend(int ix, int iy, Rgb c, double a) {
    if (a <= 0) return;
    uint8_t* p = &px_[((size_t)iy * w_ + ix) * 4];
    const double b = (c & 255), g = (c >> 8) & 255, r = (c >> 16) & 255;
    p[0] = (uint8_t)std::lround(p[0] + (b - p[0]) * a);
    p[1] = (uint8_t)std::lround(p[1] + (g - p[1]) * a);
    p[2] = (uint8_t)std::lround(p[2] + (r - p[2]) * a);
  }

  // Parcourt les pixels de la boîte englobante ; sdf = distance signée en unités logiques (négative dedans).
  template <class F>
  void Paint(double x0, double y0, double x1, double y1, double lw, F sdf, Rgb fill, Rgb line, double lineW) {
    const double m = std::max(lw, 1.0) + 1.0;
    const int ix0 = std::max(0, (int)std::floor((x0 - m) * k_)), ix1 = std::min(w_ - 1, (int)std::ceil((x1 + m) * k_));
    const int iy0 = std::max(0, (int)std::floor((y0 - m) * k_)), iy1 = std::min(h_ - 1, (int)std::ceil((y1 + m) * k_));
    for (int iy = iy0; iy <= iy1; iy++)
      for (int ix = ix0; ix <= ix1; ix++) {
        const double d = sdf((ix + 0.5) / k_, (iy + 0.5) / k_) * k_;   // en pixels
        if (fill != kNone) Blend(ix, iy, fill, std::min(1.0, std::max(0.0, 0.5 - d)));
        if (line != kNone) Blend(ix, iy, line, std::min(1.0, std::max(0.0, 0.5 - (std::fabs(d) - 0.5 * lineW * k_))));
      }
  }
};

// ------------------------------------------------------------------ contenu
struct Theme { Rgb bg, card, border, text, dim, accent, good; };

const double kW = 692, kH = 612;           // taille logique de la page
const int kSlides = 5;

struct SlideText { const wchar_t* title[2]; const wchar_t* bullet[3][2]; const wchar_t* reco[2]; };

// [0] = français, [1] = anglais
const SlideText kText[kSlides] = {
  {{L"Démarrer en 3 étapes", L"Get started in 3 steps"},
   {{L"Choisis ta caméra (ou tes deux caméras) et le port COM de l'IMU, puis clique sur Enregistrer.", L"Pick your camera (or both cameras) and the IMU's COM port, then click Save."},
    {L"Démarrer le suivi lance le casque ; ensuite, appuie sur Play dans Unity.", L"Start tracking launches the headset; then press Play in Unity."},
    {L"Les pages Yeux et Bouche sont facultatives : passe-les si tu n'as pas ces caméras.", L"The Eyes and Mouth pages are optional: skip them if you don't have those cameras."}},
   {L"Conseillé : laisse « Lecteur direct Media Foundation » coché (page Avancé) : moins de latence et de CPU.", L"Recommended: keep \"Direct Media Foundation reader\" checked (Advanced page): lower latency and CPU."}},
  {{L"Stéréo ou une seule caméra ?", L"Stereo or a single camera?"},
   {{L"Stéréo : les deux caméras mesurent la profondeur, le suivi est précis et à la bonne échelle dès le départ.", L"Stereo: both cameras measure depth, tracking is precise and at the right scale from the start."},
    {L"Une caméra : moins de calcul, mais la profondeur n'est connue qu'en bougeant la tête.", L"One camera: less computation, but depth is only known by moving your head."},
    {L"En mode une caméra, indique la distance moyenne des objets (page Avancé) et corrige l'échelle dans Unity si besoin.", L"In one-camera mode, enter the average distance of objects (Advanced page) and fix the scale in Unity if needed."}},
   {L"Conseillé : Stéréo, sauf si ton PC est trop juste ou si une seule caméra fonctionne.", L"Recommended: Stereo, unless your PC is too slow or only one camera works."}},
  {{L"Calibrer la caméra", L"Calibrating the camera"},
   {{L"Imprime le damier et colle-le sur une surface plate et rigide (carton épais, plaque).", L"Print the checkerboard and glue it on a flat, rigid surface (thick card, board)."},
    {L"Tiens-le immobile, entier et visible par les deux caméras : l'application enregistre toute seule quand c'est net.", L"Hold it still, fully visible to both cameras: the app records by itself when it is sharp."},
    {L"Change de pose à chaque vue : près, loin, coins, bords et inclinaisons.", L"Change pose every view: near, far, corners, edges and tilts."}},
   {L"Conseillé : fais les 16 poses (le calcul est possible dès 10 vues, mais le résultat est meilleur avec toutes).", L"Recommended: do all 16 poses (calculation is possible from 10 views, but the result is better with all of them)."}},
  {{L"Yeux et bouche (facultatif)", L"Eyes and mouth (optional)"},
   {{L"Choisis la caméra de chaque œil dans la page Yeux ; la pupille et le reflet infrarouge sont suivis.", L"Pick each eye's camera in the Eyes page; the pupil and the infrared glint are tracked."},
    {L"Étalonne les yeux une fois (points affichés sur l'écran) ; si le regard dérive plus tard, clique sur Recentrer.", L"Calibrate the eyes once (points shown on screen); if gaze drifts later, click Recenter."},
    {L"Bouche : fais les 4 poses (neutre, ouverte, large, étroite) pour régler l'ouverture et la largeur.", L"Mouth: do the 4 poses (neutral, open, wide, narrow) to set opening and width."}},
   {L"Conseillé : étalonne d'abord les yeux, puis la bouche, et laisse l'éclairage stable pendant l'étalonnage.", L"Recommended: calibrate the eyes first, then the mouth, and keep lighting stable while calibrating."}},
  {{L"Dans le casque", L"Inside the headset"},
   {{L"Réglages sensibles (IPD, contrôle précis) : les boutons − et + changent d'un cran, sans viser un curseur.", L"Sensitive settings (IPD, fine control): the − and + buttons change one notch, no need to aim a slider."},
    {L"Un jeu et son menu sont liés : ils se déplacent ensemble. « Cacher le menu » ne laisse que le jeu.", L"A game and its menu are linked: they move together. \"Hide menu\" leaves only the game."},
    {L"Les sons de l'interface se règlent dans Paramètres > Interface > Sons.", L"Interface sounds are set in Settings > Interface > Sounds."}},
   {L"Conseillé : règle l'IPD cran par cran en regardant l'image entre chaque pas.", L"Recommended: set the IPD one notch at a time, looking at the image between steps."}},
};

inline Rgb White() { return 0xFFFFFF; }
inline double Smooth(double x) { x = std::min(1.0, std::max(0.0, x)); return x * x * (3 - 2 * x); }

namespace detail {

inline void Check(Raster& r, double cx, double cy, double rad, Rgb col) {
  r.Disc(cx, cy, rad, rad, col);
  r.Line(cx - 0.45 * rad, cy + 0.02 * rad, cx - 0.1 * rad, cy + 0.4 * rad, White(), std::max(1.6, 0.26 * rad));
  r.Line(cx - 0.1 * rad, cy + 0.4 * rad, cx + 0.5 * rad, cy - 0.35 * rad, White(), std::max(1.6, 0.26 * rad));
}

inline void Arrow(Raster& r, double x1, double y1, double x2, double y2, Rgb col, double lw = 2.5) {
  const double a = std::atan2(y2 - y1, x2 - x1), hl = 11, hw = 6;
  r.Line(x1, y1, x2 - hl * 0.8 * std::cos(a), y2 - hl * 0.8 * std::sin(a), col, lw);
  r.Poly({{x2, y2}, {x2 - hl * std::cos(a) + hw * std::sin(a), y2 - hl * std::sin(a) - hw * std::cos(a)}, {x2 - hl * std::cos(a) - hw * std::sin(a), y2 - hl * std::sin(a) + hw * std::cos(a)}}, col);
}

inline void Headset(Raster& r, const Theme& th, double cx, double cy, double s, double glow) {
  const Rgb body = Mix(th.card, th.text, 0.12);
  r.Box(cx - 78 * s, cy - 28 * s, 156 * s, 56 * s, 24 * s, body, th.text, 2);
  r.Disc(cx, cy + 25 * s, 12 * s, 9 * s, Mix(th.card, th.bg, 0.5), th.text, 1.5);   // creux du nez
  for (int side = -1; side <= 1; side += 2) {
    const double x = cx + side * 42 * s;
    r.Disc(x, cy - 2 * s, 14 * s, 14 * s, 0x1B1F27, Mix(th.accent, White(), 0.5 * glow), 2.5 + 2 * glow);
    r.Disc(x, cy - 2 * s, 7 * s, 7 * s, Mix(0x30445C, th.accent, 0.5 * glow));
    r.Disc(x - 3 * s, cy - 5 * s, 2.4 * s, 2.4 * s, White());
  }
}

inline void Stage(Raster& r, const Theme& th, double x, double y, double w, double h) { r.Box(x, y, w, h, 14, Mix(th.card, th.bg, 0.55), th.border, 1.5); }

inline void Badge(Raster& r, const Theme& th, double x, double y, double t, bool en) {
  const double pulse = 0.5 + 0.5 * std::sin(t * 4.0);
  r.Box(x, y, 118, 26, 13, th.good, Mix(th.good, White(), 0.55 * pulse), 2.5);
  Check(r, x + 15, y + 13, 8, Mix(th.good, 0x000000, 0.3));
  r.Text(x + 28, y, 88, 26, en ? L"Recommended" : L"Recommandé", 0, White(), 4);
}

}  // namespace detail

// Dessine la diapositive `slide` ; t = secondes depuis son affichage.
inline void Draw(Raster& r, const Theme& th, int slide, double t, bool en) {
  using namespace detail;
  const int lg = en ? 1 : 0;
  slide = std::min(std::max(slide, 0), kSlides - 1);
  const SlideText& st = kText[slide];
  r.Clear(th.bg);
  r.Box(0, 0, kW, kH, 8, th.card, th.border, 1.5);
  r.Text(24, 14, 644, 34, st.title[lg], 1, th.text, 4);
  Stage(r, th, 24, 56, 644, 304);
  const Rgb ink = th.text, soft = Mix(th.card, th.text, 0.1);
  const double enter = Smooth(t * 2.5);   // les éléments apparaissent en fondu court

  if (slide == 0) {   // ---- démarrage : 3 étapes
    const wchar_t* lab[3][2] = {{L"Choisir la caméra et le port de l'IMU", L"Pick the camera and the IMU port"}, {L"Enregistrer les réglages", L"Save the settings"}, {L"Démarrer le suivi", L"Start tracking"}};
    const int step = (int)std::floor(t / 1.7) % 3;
    const double ph = std::fmod(t, 1.7) / 1.7;
    for (int i = 0; i < 3; i++) {
      const double bx = 52 + i * 212, bw = 168, lift = (i == step ? -5.0 * Smooth(ph * 4) : 0.0), by = 128 + lift;
      r.Box(bx, by, bw, 160, 14, th.card, i == step ? th.accent : th.border, i == step ? 3.5 : 1.5);
      const double cx = bx + bw / 2, cy = by + 62;
      if (i == 0) {   // appareil photo
        r.Box(cx - 36, cy - 18, 72, 46, 9, soft, ink, 2);
        r.Box(cx - 14, cy - 26, 28, 12, 4, soft, ink, 2);
        r.Disc(cx, cy + 5, 17, 17, 0x1B1F27, ink, 2);
        r.Disc(cx, cy + 5, 9, 9, th.accent);
        r.Disc(cx - 3, cy + 1, 3, 3, White());
      } else if (i == 1) {   // puce (IMU)
        for (int k = -1; k <= 1; k++) {
          r.Line(cx - 38, cy + k * 14, cx - 26, cy + k * 14, ink, 3);
          r.Line(cx + 26, cy + k * 14, cx + 38, cy + k * 14, ink, 3);
          r.Line(cx + k * 14, cy - 38, cx + k * 14, cy - 26, ink, 3);
          r.Line(cx + k * 14, cy + 26, cx + k * 14, cy + 38, ink, 3);
        }
        r.Box(cx - 26, cy - 26, 52, 52, 7, soft, ink, 2.5);
        r.Box(cx - 12, cy - 12, 24, 24, 4, th.accent);
      } else {   // lecture
        r.Disc(cx, cy, 33, 33, th.accent);
        r.Poly({{cx - 10, cy - 18}, {cx - 10, cy + 18}, {cx + 20, cy}}, White());
      }
      r.Disc(bx + 22, by + 22, 14, 14, th.accent);
      r.Text(bx + 8, by + 8, 28, 28, std::to_wstring(i + 1), 1, White(), 5);
      r.Text(bx + 10, by + 108, bw - 20, 48, lab[i][lg], 0, th.text, 1);
      if (i < 2) {
        const double ay = 208;
        Arrow(r, bx + bw + 6, ay, bx + bw + 40, ay, th.dim, 2.5);
        if (i == step) r.Disc(bx + bw + 6 + 34 * ph, ay, 5, 5, th.accent);
      }
    }
  } else if (slide == 1) {   // ---- stéréo / une caméra
    for (int side = 0; side < 2; side++) {
      const double px = 36 + side * 320, py = 70, pw = 296, ph = 276;
      const bool rec = (side == 0);
      const double pulse = 0.5 + 0.5 * std::sin(t * 4.0);
      r.Box(px, py, pw, ph, 14, th.card, rec ? Mix(th.good, th.card, 0.45 - 0.35 * pulse) : th.border, rec ? 3.5 : 1.5);
      r.Text(px + 14, py + 8, 150, 28, side == 0 ? (en ? L"Stereo" : L"Stéréo") : (en ? L"One camera" : L"Une caméra"), 1, th.text, 4);
      const double mx = px + pw / 2, tx = mx, ty = py + 214;
      const double beat = std::fmod(t, 2.0) / 2.0;
      if (side == 0) {
        Badge(r, th, px + pw - 130, py + 8, t, en);
        Headset(r, th, mx, py + 90, 1.0, 0.5 + 0.5 * std::sin(t * 4));
        const double lx = mx - 42, rx = mx + 42, cy = py + 90;
        r.Poly({{lx, cy + 10}, {rx, cy + 10}, {tx, ty}}, Mix(th.card, th.accent, 0.14));
        r.Line(lx, cy + 10, tx, ty, th.accent, 2.5);
        r.Line(rx, cy + 10, tx, ty, th.accent, 2.5);
        r.Disc(lx + (tx - lx) * beat, cy + 10 + (ty - cy - 10) * beat, 4.5, 4.5, th.accent);
        r.Disc(rx + (tx - rx) * beat, cy + 10 + (ty - cy - 10) * beat, 4.5, 4.5, th.accent);
      } else {
        const double sh = 38 * std::sin(t * 1.3);   // la tête bouge : la caméra voit l'objet depuis deux endroits
        const double cy = py + 90;
        r.Box(mx - 120 - 26, cy - 18, 52, 36, 9, Mix(th.card, th.bg, 0.5), th.dim, 1.5);   // positions passées (fantômes)
        r.Box(mx + 120 - 26, cy - 18, 52, 36, 9, Mix(th.card, th.bg, 0.5), th.dim, 1.5);
        const double cxm = mx + sh;
        r.Line(cxm, cy + 12, tx, ty, th.accent, 2.5);
        r.Box(cxm - 26, cy - 18, 52, 36, 9, soft, ink, 2);
        r.Disc(cxm, cy + 1, 12, 12, 0x1B1F27, th.accent, 2.5);
        r.Disc(cxm - 3, cy - 2, 2.5, 2.5, White());
        r.Disc(cxm + (tx - cxm) * beat, cy + 12 + (ty - cy - 12) * beat, 4.5, 4.5, th.accent);
        r.Text(tx - 20, ty - 48, 40, 30, L"?", 1, th.dim, 5);
      }
      r.Disc(tx, ty, 15, 15, Mix(th.accent, 0x000000, 0.1));
      r.Disc(tx - 4, ty - 5, 4, 4, White());
      r.Text(px + 12, py + ph - 38, pw - 24, 32, side == 0 ? (en ? L"2 cameras: depth is measured" : L"2 caméras : profondeur mesurée") : (en ? L"1 camera: depth is estimated by moving" : L"1 caméra : profondeur estimée en bougeant"), 0, th.dim, 5);
    }
  } else if (slide == 2) {   // ---- calibration
    const double yaw = 0.62 * std::sin(t * 1.1), pitch = 0.35 * std::sin(t * 0.7 + 1.0);
    const double bcx = 190, bcy = 196, R = 124;
    auto proj = [&](double u, double v) -> Pt {
      const double x = u, y = v * 0.75;
      const double x1 = x * std::cos(yaw), z1 = x * std::sin(yaw);
      const double y2 = y * std::cos(pitch) - z1 * std::sin(pitch), z2 = y * std::sin(pitch) + z1 * std::cos(pitch);
      const double s = 1.0 / (1.0 - z2 * 0.38);
      return {bcx + x1 * s * R, bcy + y2 * s * R};
    };
    for (int j = 0; j < 6; j++)
      for (int i = 0; i < 8; i++) {
        const double u0 = -1 + i * 0.25, u1 = u0 + 0.25, v0 = -1 + j / 3.0, v1 = v0 + 1.0 / 3.0;
        r.Poly({proj(u0, v0), proj(u1, v0), proj(u1, v1), proj(u0, v1)}, ((i + j) & 1) ? 0x20242C : 0xF4F4F4);
      }
    r.Poly({proj(-1, -1), proj(1, -1), proj(1, 1), proj(-1, 1)}, kNone, th.dim, 2.5);
    r.Text(36, 322, 312, 28, en ? L"Flat · sharp · fully visible" : L"Plat · net · entier", 0, th.dim, 5);
    const int views = std::min(16, (int)(t * 1.6) % 22);   // le compteur monte puis repart à zéro
    r.Text(392, 100, 250, 26, en ? L"Recorded views" : L"Vues enregistrées", 1, th.text, 0);
    for (int i = 0; i < 16; i++) {
      const double x = 400 + i * 15.5;
      const Rgb c = i < views ? (i < 10 ? th.accent : th.good) : Mix(th.card, th.dim, 0.35);
      r.Disc(x, 160, 6, 6, c);
    }
    r.Line(400 + 9 * 15.5, 172, 400 + 9 * 15.5, 186, th.dim, 1.5);
    r.Text(404, 188, 250, 22, en ? L"10 = enough to calculate" : L"10 = assez pour calculer", 0, th.dim, 0);
    r.Line(400 + 15 * 15.5, 214, 400 + 15 * 15.5, 228, th.dim, 1.5);
    r.Text(404, 230, 250, 22, en ? L"16 = all poses (best)" : L"16 = toutes les poses (idéal)", 0, th.dim, 2);
    if (views >= 16) Check(r, 640, 128, 11, th.good);
  } else if (slide == 3) {   // ---- yeux et bouche
    const double ex = 190, ey = 190;
    const double dx = 30 * std::sin(t * 1.3), dy = 15 * std::sin(t * 0.9 + 1.0);
    r.Disc(ex, ey, 104, 58, Mix(0xFFFFFF, th.card, 0.15), ink, 2.5);
    r.Disc(ex + dx, ey + dy, 42, 42, Mix(th.accent, 0x000000, 0.45));
    r.Disc(ex + dx, ey + dy, 18, 18, 0x0B0D11);
    r.Disc(ex + dx + 8, ey + dy - 8, 4.5, 4.5, White());
    r.Disc(ex + dx, ey + dy, 27, 27, kNone, th.good, 2.5);
    r.Line(ex + dx - 36, ey + dy, ex + dx - 24, ey + dy, th.good, 2.5);
    r.Line(ex + dx + 24, ey + dy, ex + dx + 36, ey + dy, th.good, 2.5);
    r.Text(36, 322, 312, 28, en ? L"Eyes: pupil and glint tracked" : L"Yeux : pupille et reflet suivis", 0, th.dim, 5);
    const double mx = 500, my = 190, open = 0.5 + 0.5 * std::sin(t * 1.6), wide = 0.5 + 0.5 * std::sin(t * 0.8 + 2.0);
    const double mw = 52 + 26 * wide, mh = 5 + 26 * open;
    r.Disc(mx, my, mw + 10, mh + 12, Mix(0xD9667A, th.card, 0.15));
    r.Disc(mx, my, mw, mh, 0x3A1418);
    r.Poly({{mx - mw * 0.6, my - mh * 0.55}, {mx + mw * 0.6, my - mh * 0.55}, {mx + mw * 0.5, my - mh * 0.15}, {mx - mw * 0.5, my - mh * 0.15}}, 0xF4F4F4);
    r.Text(400, 322, 200, 28, en ? L"Mouth: opening and width" : L"Bouche : ouverture et largeur", 0, th.dim, 5);
    r.Box(430, 268, 140, 10, 5, Mix(th.card, th.dim, 0.3));
    r.Box(430, 268, 140 * open, 10, 5, th.accent);
  } else {   // ---- dans le casque
    const double cyc = std::fmod(t, 9.0);
    const double hide = Smooth((cyc - 4.8) / 0.6) * (1 - Smooth((cyc - 8.0) / 0.6));   // 0 = menu visible, 1 = menu caché
    const double wx = 40, wy = 78, ww = 380, wh = 262 - hide * 200;
    r.Box(wx, wy, ww, wh, 14, th.card, th.border, 2);
    r.Text(wx + 16, wy + 8, 150, 40, en ? L"Settings" : L"Réglages", 1, th.text, 4);
    r.Box(wx + ww - 168, wy + 9, 152, 36, 10, hide > 0.5 ? th.accent : soft, hide > 0.5 ? kNone : th.border, 1.5);
    r.Text(wx + ww - 168, wy + 9, 152, 36, hide > 0.5 ? (en ? L"Show menu" : L"Montrer le menu") : (en ? L"Hide menu" : L"Cacher le menu"), 0, hide > 0.5 ? White() : th.text, 5);
    if (hide < 0.05) {
      const int notch = (cyc > 1.2 ? 1 : 0) + (cyc > 2.6 ? 1 : 0);
      const double v = 63.0 + 0.5 * notch;
      auto press = [&](double t0) { return std::exp(-std::pow((cyc - t0) / 0.18, 2)); };
      r.Text(wx + 16, wy + 78, 70, 30, L"IPD", 0, th.text, 4);
      r.Text(wx + 100, wy + 50, 200, 24, std::to_wstring((int)v) + (notch % 2 ? (en ? L".5 mm" : L",5 mm") : (en ? L".0 mm" : L",0 mm")), 1, th.text, 5);
      const double mp = press(1.2) + press(2.6);
      r.Box(wx + 16 + 62, wy + 74, 36, 36, 9, Mix(th.card, th.dim, 0.18), th.border, 1.5);
      r.Text(wx + 78, wy + 74, 36, 36, L"−", 1, th.text, 5);
      r.Box(wx + ww - 16 - 36, wy + 74, 36, 36, 9, mp > 0.2 ? th.accent : Mix(th.card, th.dim, 0.18), th.border, 1.5);
      r.Text(wx + ww - 52, wy + 74, 36, 36, L"+", 1, mp > 0.2 ? White() : th.text, 5);
      const double tx0 = wx + 124, tx1 = wx + ww - 70;
      r.Box(tx0, wy + 89, tx1 - tx0, 6, 3, Mix(th.card, th.dim, 0.3));
      const double f = (v - 60.0) / 8.0;
      r.Box(tx0, wy + 89, (tx1 - tx0) * f, 6, 3, th.accent);
      r.Disc(tx0 + (tx1 - tx0) * f, wy + 92, 9, 9, White(), th.accent, 2.5);
      r.Text(wx + 16, wy + 130, ww - 32, 60, en ? L"One press = one notch (0.5 mm)" : L"Un appui = un cran (0,5 mm)", 0, th.dim, 0);
    }
    // jeu lié : damier (échecs) à droite, relié à la fenêtre
    const double gx = 466, gy = 96;
    r.Box(gx - 10, gy - 10, 196, 196, 12, Mix(th.card, th.bg, 0.4), th.border, 1.5);
    for (int j = 0; j < 8; j++)
      for (int i = 0; i < 8; i++) r.Box(gx + i * 22, gy + j * 22, 22, 22, 0, ((i + j) & 1) ? 0x6B4A2E : 0xE6D2AE);
    r.Disc(gx + 22 * 3 + 11, gy + 22 * 4 + 11, 8, 8, 0x1B1F27, White(), 1.5);
    r.Disc(gx + 22 * 5 + 11, gy + 22 * 2 + 11, 8, 8, 0xF4F4F4, 0x1B1F27, 1.5);
    const double ly = wy + 24;
    for (double x = wx + ww + 4; x < gx - 14; x += 9) r.Line(x, ly, x + 5, ly, th.accent, 2.5);   // lien en pointillés
    r.Text(gx - 10, gy + 190, 196, 26, en ? L"Game + menu: linked" : L"Jeu + menu : liés", 0, th.dim, 5);
  }

  // puces et texte
  for (int i = 0; i < 3; i++) {
    r.Disc(34, 392 + i * 42, 4, 4, th.accent);
    r.Text(46, 372 + i * 42, 622, 40, st.bullet[i][lg], 0, th.text, 4);
  }
  r.Box(24, 504, 644, 52, 12, Mix(th.card, th.good, 0.12), th.good, 1.5);
  Check(r, 48, 530, 11, th.good);
  r.Text(70, 504, 590, 52, st.reco[lg], 0, th.text, 4);
  (void)enter;
  // navigation : ‹ Précédent · points · Suivant ›
  r.Box(24, 568, 126, 34, 9, Mix(th.card, th.dim, 0.1), th.border, 1.5);
  r.Text(24, 568, 126, 34, en ? L"‹  Previous" : L"‹  Précédent", 0, th.text, 5);
  r.Box(542, 568, 126, 34, 9, th.accent);
  r.Text(542, 568, 126, 34, en ? L"Next  ›" : L"Suivant  ›", 0, White(), 5);
  for (int i = 0; i < kSlides; i++) r.Disc(346 + (i - (kSlides - 1) / 2.0) * 22, 585, i == slide ? 6 : 4.5, i == slide ? 6 : 4.5, i == slide ? th.accent : Mix(th.card, th.dim, 0.45));
}

// Clic en (x, y) (unités logiques) : -1 = précédent, 1 = suivant, 100 + i = point i, 0 = rien.
inline int HitTest(double x, double y) {
  if (y >= 568 && y <= 602) {
    if (x >= 24 && x <= 150) return -1;
    if (x >= 542 && x <= 668) return 1;
  }
  if (y >= 570 && y <= 600)
    for (int i = 0; i < kSlides; i++)
      if (std::fabs(x - (346 + (i - (kSlides - 1) / 2.0) * 22)) < 11) return 100 + i;
  return 0;
}

}  // namespace helpdraw
