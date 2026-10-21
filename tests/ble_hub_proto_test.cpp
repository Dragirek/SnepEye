#include <cassert>
#include <cstdio>
#include "ble_hub_proto.h"
using namespace ar6hub;
int main() {
  uint64_t a;
  assert(ParseAddress("A1B2C3D4E5F6", &a) && a == 0xA1B2C3D4E5F6ull);
  assert(FormatAddress(a) == "A1B2C3D4E5F6" && FormatAddress(0x1) == "000000000001");
  assert(!ParseAddress("A1B2C3D4E5F", &a) && !ParseAddress("A1B2C3D4E5FG", &a));
  std::vector<uint8_t> b;
  assert(ParseHexBytes("7e 00 ff", &b) && b.size() == 3 && b[0] == 0x7E && b[2] == 0xFF);
  assert(ParseHexBytes("", &b) && b.empty());
  assert(!ParseHexBytes("7e0", &b) && !ParseHexBytes("zz", &b));
  Uuid u;
  assert(ParseUuid("FFE1", &u) && UuidKey(u) == "0000FFE1-0000-1000-8000-00805F9B34FB");
  assert(ParseUuid("6e400001-b5a3-f393-e0a9-e50e24dcca9e", &u) && UuidKey(u) == "6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
  assert(ParseUuid("{6E400001-B5A3-F393-E0A9-E50E24DCCA9E}", &u) && u.d1 == 0x6E400001u && u.d2 == 0xB5A3 && u.d4[7] == 0x9E);
  assert(!ParseUuid("12345", &u));
  auto p = SplitSpaces("DEV A1B2C3D4E5F6 -60 Mon  appareil  LED ", 4);
  assert(p.size() == 4 && p[3] == "Mon  appareil  LED");
  WriteCmd w;
  assert(ParseWrite("WRITE A1B2C3D4E5F6 FFE0 FFE1 7e00ff rc", &w) && w.data.size() == 3 && w.withResponse && w.coalesce);
  assert(ParseWrite("WRITE A1B2C3D4E5F6 FFE0 FFE1 7e00ff", &w) && !w.withResponse && !w.coalesce);
  std::vector<WriteCmd> v(4);
  assert(ParseWrite("WRITE A1B2C3D4E5F6 FFE0 FFE1 01 c", &v[0]) && ParseWrite("WRITE A1B2C3D4E5F6 FFE0 FFE1 02 c", &v[1]) &&
         ParseWrite("WRITE A1B2C3D4E5F6 FFE0 FFE1 03 -", &v[2]) && ParseWrite("WRITE A1B2C3D4E5F6 FFE0 FFE1 04 c", &v[3]));
  auto o = Coalesce(v);
  assert(o.size() == 2 && o[0].data[0] == 0x03 && o[1].data[0] == 0x04);   // 01 et 02 remplacés par 04 ; 03 (pas "c") conservé
  puts("TEST PROTOCOLE BLE OK");
}
