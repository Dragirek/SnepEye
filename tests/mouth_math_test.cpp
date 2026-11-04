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
  if (!fails) std::printf("mouth_math_test : OK\n");
  return fails;
}
