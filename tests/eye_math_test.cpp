// Test des maths de l'eye tracking (sans dépendance) : g++ -std=c++17 -Isrc tests/eye_math_test.cpp -o eye_math_test
#include "eye_math.h"
#include <cassert>
#include <cstdio>
#include <cstdlib>
#include <random>

using namespace eye;

#define CHECK(c) do { if (!(c)) { std::printf("ECHEC ligne %d : %s\n", __LINE__, #c); std::exit(1); } } while (0)

// Un "oeil" synthétique : le regard (yaw, pitch) donne une position de pupille non linéaire en pixels, + un reflet qui suit un peu.
static void EyeAt(double yaw, double pitch, double slipX, double slipY, std::mt19937& rng, double noise, Feature* f) {
  std::normal_distribution<double> n(0.0, noise);
  double u = yaw / 30.0, v = pitch / 25.0;
  f->px = 160 + 55 * u + 6 * u * v + 8 * u * u + slipX + n(rng);
  f->py = 120 - 42 * v + 5 * u * v - 6 * v * v + slipY + n(rng);
  f->gx = 150 + 12 * u + slipX + n(rng) * 0.3;    // le reflet bouge peu avec l'oeil, mais suit le glissement du casque
  f->gy = 130 - 9 * v + slipY + n(rng) * 0.3;
  f->glint = true;
}

static std::vector<PointSamples> Grid(std::mt19937& rng, double noise, int perPoint, double slipX, double slipY, bool glint) {
  std::vector<PointSamples> pts;
  const double ys[3] = {-22, 0, 22}, ps[3] = {-15, 0, 15};
  for (double y : ys) for (double p : ps) {
    PointSamples s; s.yaw = y; s.pitch = p;
    for (int i = 0; i < perPoint; i++) { Feature f; EyeAt(y, p, slipX, slipY, rng, noise, &f); f.glint = glint; s.f.push_back(f); }
    pts.push_back(s);
  }
  return pts;
}

int main() {
  std::mt19937 rng(7);

  // 1) étalonnage 3x3 : l'erreur sur des points jamais vus est petite
  {
    auto pts = Grid(rng, 0.4, 60, 0, 0, true);
    Map m; double rms = 0; int used = 0;
    CHECK(SolveEye(pts, &m, &rms, &used));
    CHECK(used == 9);
    CHECK(m.valid && m.useGlint && m.order == 2);
    CHECK(rms < 1.0);
    double worst = 0;
    for (double y = -20; y <= 20; y += 5) for (double p = -14; p <= 14; p += 7) {
      Feature f; EyeAt(y, p, 0, 0, rng, 0.0, &f);
      double yw, pt; CHECK(m.Apply(f, &yw, &pt));
      worst = std::max(worst, std::hypot(yw - y, pt - p));
    }
    std::printf("etalonnage 3x3 : rms %.2f deg, pire erreur hors points %.2f deg\n", rms, worst);
    CHECK(worst < 2.0);

    // 2) enregistrement / lecture identiques
    Map m2;
    CHECK(MapFromString(MapToString(m), &m2));
    Feature f; EyeAt(10, 5, 0, 0, rng, 0.0, &f);
    double a1, b1, a2, b2;
    CHECK(m.Apply(f, &a1, &b1) && m2.Apply(f, &a2, &b2));
    CHECK(std::fabs(a1 - a2) < 1e-6 && std::fabs(b1 - b2) < 1e-6);
    CHECK(!MapFromString("n'importe quoi", &m2));

    // 3) le reflet compense le glissement du casque : on décale tout de 6 px, l'erreur reste modeste
    Feature g; EyeAt(10, 5, 6, -4, rng, 0.0, &g);
    double yw, pt; CHECK(m.Apply(g, &yw, &pt));
    std::printf("glissement de 6 px avec reflet : erreur %.2f deg\n", std::hypot(yw - 10, pt - 5));
    CHECK(std::hypot(yw - 10, pt - 5) < 4.0);

    // 4) sans reflet la carte refuse (valeur non fiable) au lieu de mentir
    g.glint = false;
    CHECK(!m.Apply(g, &yw, &pt));

    // 5) recentrage : le biais est retiré
    Feature c; EyeAt(0, 0, 3, 2, rng, 0.0, &c);
    Map mr = m;
    double cy, cp; mr.Apply(c, &cy, &cp);
    mr.offYaw -= cy; mr.offPitch -= cp;
    mr.Apply(c, &cy, &cp);
    CHECK(std::fabs(cy) < 1e-6 && std::fabs(cp) < 1e-6);
  }

  // 6) reflet absent par moments : on bascule sur la pupille seule
  {
    auto pts = Grid(rng, 0.4, 60, 0, 0, true);
    for (size_t i = 0; i < pts.size(); i++) for (size_t j = 0; j < pts[i].f.size(); j++) if (j % 3 == 0) pts[i].f[j].glint = false;
    Map m; double rms; int used;
    CHECK(SolveEye(pts, &m, &rms, &used));
    CHECK(!m.useGlint);
    CHECK(rms < 1.5);
  }

  // 7) clignements pendant la mesure d'un point : la médiane ignore quelques mesures aberrantes
  {
    auto pts = Grid(rng, 0.4, 60, 0, 0, true);
    for (auto& p : pts) for (int j = 0; j < 8; j++) { p.f[j].px += 80; p.f[j].py += 60; }
    Map m; double rms; int used;
    CHECK(SolveEye(pts, &m, &rms, &used));
    CHECK(rms < 1.5);
  }

  // 8) trop peu de points / pupille immobile : refus
  {
    std::vector<PointSamples> pts(3);
    for (auto& p : pts) { for (int i = 0; i < 20; i++) { Feature f; f.px = 100; f.py = 100; p.f.push_back(f); } }
    Map m; double rms; int used;
    CHECK(!SolveEye(pts, &m, &rms, &used));
  }

  // 9) lissage : stable à l'arrêt (bruit réduit), suit une saccade en quelques images
  {
    OneEuro f; std::normal_distribution<double> n(0.0, 0.5);
    double t = 0, sumRaw = 0, sumF = 0; int cnt = 0;
    for (int i = 0; i < 600; i++, t += 1.0 / 120) {
      double raw = 10.0 + n(rng);
      double o = f.Filter(raw, t);
      if (i > 120) { sumRaw += (raw - 10) * (raw - 10); sumF += (o - 10) * (o - 10); cnt++; }
    }
    std::printf("lissage : bruit %.3f -> %.3f deg (rms)\n", std::sqrt(sumRaw / cnt), std::sqrt(sumF / cnt));
    CHECK(sumF < 0.25 * sumRaw);
    int steps = 0; double o = 0;
    for (int i = 0; i < 120; i++, t += 1.0 / 120) { o = f.Filter(25.0, t); if (std::fabs(o - 25.0) < 2.0) { steps = i; break; } }
    std::printf("saccade de 15 deg : a moins de 2 deg apres %d images (%.0f ms)\n", steps, steps * 1000.0 / 120);
    CHECK(steps > 0 && steps < 24);
  }

  // 10) clignements
  {
    BlinkState b;
    for (int i = 0; i < 100; i++) b.Update(true, 1000);
    CHECK(!b.blink && b.openness > 0.99);
    b.Update(false, 0); b.Update(false, 0);
    CHECK(!b.blink);              // 2 images perdues : pas encore un clignement
    b.Update(false, 0);
    CHECK(b.blink);
    b.Update(true, 1000);
    CHECK(!b.blink);
    b.Update(true, 250);          // pupille presque cachée par la paupière
    CHECK(b.blink);
    b.Update(true, 900);
    CHECK(!b.blink && b.openness > 0.9);
  }

  // 11) paquet de regard
  {
    GazePacket g; g.flags = kValidL | kValidR | kCalibrated; g.seq = 513; g.yawL = -12.5f; g.pitchL = 3.25f; g.yawR = -11.0f; g.pitchR = 2.0f;
    g.yawC = -11.75f; g.pitchC = 2.625f; g.openL = 0.9f; g.openR = 1.0f; g.micros = 123456789u;
    uint8_t buf[kGazeSize];
    EncodeGaze(g, buf);
    GazePacket h; CHECK(DecodeGaze(buf, kGazeSize, &h));
    CHECK(h.flags == g.flags && h.seq == 513 && h.yawL == g.yawL && h.pitchR == g.pitchR && h.yawC == g.yawC && h.micros == g.micros && h.openL == g.openL);
    CHECK(!DecodeGaze(buf, kGazeSize - 1, &h));
    buf[0] ^= 1;
    CHECK(!DecodeGaze(buf, kGazeSize, &h));
  }

  // 12) découpage des commandes
  {
    auto w = SplitWords("CAL_POINT -22 15 700 800");
    CHECK(w.size() == 5 && w[0] == "CAL_POINT" && std::atof(w[1].c_str()) == -22.0);
  }

  std::printf("TEST EYE TRACKING (MATHS) OK\n");
  return 0;
}
