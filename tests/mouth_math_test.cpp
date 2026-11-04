// Test de mouth_math.h (sans OpenCV) : g++ -std=c++17 -Isrc tests/mouth_math_test.cpp
#include <cstdio>
#include "mouth_math.h"
using namespace mouth;
static int fails = 0;
#define CHECK(c) do { if (!(c)) { std::printf("ECHEC ligne %d : %s\n", __LINE__, #c); fails++; } } while (0)
static Feature F(double a, double w, double h, double cx = 0.5, double cy = 0.5) { Feature f; f.found = true; f.area = a; f.width = w; f.height = h; f.cx = cx; f.cy = cy; return f; }
int main() {
  Cal c;
  CHECK(!Map(c, F(0.1, 0.3, 0.2)).valid);                  // pas d'etalonnage
  c.neutral = F(0.01, 0.30, 0.02); c.hasNeutral = true;
  CHECK(!Map(c, F(0.1, 0.3, 0.2)).valid);                  // il manque l'ouverture
  c.open = F(0.11, 0.34, 0.22); c.hasOpen = true;
  CHECK(c.Valid());
  Out o = Map(c, c.neutral);
  CHECK(o.valid && o.jawOpen < 0.01f && std::fabs(o.wide) < 0.01f);
  o = Map(c, c.open);
  CHECK(o.jawOpen > 0.99f);
  o = Map(c, F(0.06, 0.32, 0.12));
  CHECK(o.jawOpen > 0.4f && o.jawOpen < 0.6f);
  o = Map(c, F(0.01, 0.30 * 1.3, 0.02));                   // sourire sans etalonnage de largeur : plage par defaut 30 %
  CHECK(o.wide > 0.95f);
  c.wide = F(0.01, 0.40, 0.02); c.hasWide = true;
  o = Map(c, F(0.01, 0.35, 0.02));
  CHECK(o.wide > 0.45f && o.wide < 0.55f);
  Feature none; none.found = false;
  o = Map(c, none);
  CHECK(o.valid && o.jawOpen == 0.f && o.wide == 0.f);     // bouche fermee : mesure valide
  std::vector<Feature> v = {F(0.01, 0.3, 0.02), F(0.02, 0.3, 0.02), F(0.9, 0.9, 0.9)};
  Feature m = MedianFeature(v);
  CHECK(m.found && m.area < 0.03);                         // la valeur aberrante est ignoree
  // mode IA : etalonnage neutre + expressions, paquet 2
  {
    MlCal mc;
    float in[kShapes] = {}, out[kShapes];
    in[4] = 0.30f; in[19] = 0.05f;
    mc.hasBase = true; mc.base[4] = 0.10f; mc.base[19] = 0.05f;
    mc.hasRange = true; mc.maxv[4] = 0.70f; mc.maxv[19] = 0.10f;   // jawOpen: 0.1..0.7 ; smile : plage trop petite -> inchange
    MlApply(mc, in, out);
    CHECK(std::fabs(out[4] - (0.20f / 0.60f)) < 1e-5f);
    CHECK(out[19] == 0.f && out[0] == 0.f);
    std::vector<std::vector<float>> sm;
    for (int i = 0; i < 100; i++) sm.push_back(std::vector<float>(kShapes, (float)i / 99.f));
    float p90[kShapes];
    MlPercentile(sm, 0.9, p90);
    CHECK(std::fabs(p90[3] - 0.9f) < 0.02f);
    float rt[kShapes];
    CHECK(FloatsFromString(FloatsToString(p90, kShapes), rt, kShapes) && std::fabs(rt[7] - p90[7]) < 1e-4f);
    uint8_t pk[kPacket2Size];
    Encode2(3, 7, 123456u, out, pk);
    uint32_t mg; std::memcpy(&mg, pk, 4);
    float f4; std::memcpy(&f4, pk + 16 + 4 * 4, 4);
    CHECK(mg == kMagic2 && pk[4] == 2 && pk[5] == 3 && std::fabs(f4 - out[4]) < 1e-7f);
    CHECK(std::string(ShapeName(4)) == "jawOpen" && std::string(ShapeName(44)) == "tongueTwistRight");
  }
  if (!fails) std::printf("mouth_math_test : OK\n");
  return fails;
}
