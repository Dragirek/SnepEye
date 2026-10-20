#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cmath>
int main() {
  // même conversion que ble_remote.cpp::Forward et firmware ToI16
  float q0[4] = {0.7071f, 0.0f, 0.7071f, 0.0f};
  uint8_t b[14] = {2, 7};
  for (int i = 0; i < 4; i++) { int16_t v = (int16_t)lroundf(q0[i] * 32767.0f); memcpy(b + 2 + 2 * i, &v, 2); }
  uint32_t us = 123456; memcpy(b + 10, &us, 4);
  int16_t qi[4]; memcpy(qi, b + 2, 8);
  float q[4]; for (int i = 0; i < 4; i++) q[i] = (float)qi[i] / 32767.0f;
  for (int i = 0; i < 4; i++) assert(fabsf(q[i] - q0[i]) < 5e-5f);
  uint8_t out[28]; uint32_t magic = 0x52365241u; uint16_t seq = 3;
  memcpy(out, &magic, 4); out[4] = 1; out[5] = b[0]; memcpy(out + 6, &seq, 2); memcpy(out + 8, q, 16); uint32_t u2; memcpy(&u2, b + 10, 4); memcpy(out + 24, &u2, 4);
  float rq[4]; memcpy(rq, out + 8, 16); uint32_t um; memcpy(&um, out + 24, 4);
  assert(out[5] == 2 && um == 123456 && fabsf(rq[2] - 0.7071f) < 5e-5f);
  puts("TEST CONVERSION BLE OK");
}
