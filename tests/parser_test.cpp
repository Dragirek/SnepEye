#include <cassert>
#include <cstdio>
#include <cstdlib>
#include <string>
#include "../src/imu_parser.h"

static std::vector<uint8_t> make(uint8_t type, uint32_t t, float x, float y, float z) {
  std::vector<uint8_t> b(20);
  float v[3] = {x, y, z};
  b[0] = 0xAA; b[1] = 0x55; b[2] = type;
  memcpy(&b[3], &t, 4);
  memcpy(&b[7], v, 12);
  uint8_t c = 0;
  for (int i = 2; i < 19; i++) c ^= b[i];
  b[19] = c;
  return b;
}

int main() {
  std::vector<uint8_t> stream;
  const char* junk = "BNO085 NOT FOUND\r\n";
  stream.insert(stream.end(), junk, junk + strlen(junk));

  // 200 paquets, timestamps qui passent le bouclage 2^32
  uint32_t t = 0xFFFFFFFFu - 100000u;
  for (int i = 0; i < 200; i++) {
    auto p = make(1 + (i & 1), t, 0.f, 0.f, 9.81f + i);
    if (i == 50) p[10] ^= 0xFF;  // paquet corrompu -> doit être rejeté
    stream.insert(stream.end(), p.begin(), p.end());
    t += 5000;
  }

  ImuParser parser;
  int got = 0;
  int64_t last = -1;
  bool monotonic = true;
  size_t i = 0;
  srand(1);
  while (i < stream.size()) {  // découpage en morceaux aléatoires
    size_t n = 1 + rand() % 37;
    if (i + n > stream.size()) n = stream.size() - i;
    parser.feed(stream.data() + i, n, [&](const ImuSample& s) {
      if (s.t_us <= last) monotonic = false;
      last = s.t_us;
      ++got;
    });
    i += n;
  }
  std::printf("valides=%d (attendu 199) bad=%llu monotone=%d\n", got,
              (unsigned long long)parser.bad_checksum, (int)monotonic);
  assert(got == 199);
  assert(monotonic);
  assert(last > 0xFFFFFFFFLL);  // le timestamp a bien été déroulé
  std::puts("OK");
  return 0;
}
