#pragma once
// Parseur du flux série du BNO085 (firmware v2). Portable : aucune dépendance
// Windows, donc testable partout (voir tests/parser_test.cpp).
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

struct ImuSample {
  uint8_t type;    // 1 = accel (m/s^2), 2 = gyro (rad/s)
  int64_t t_us;    // horloge de l'ESP32, "déroulée" (ne reboucle plus)
  float v[3];
};

class ImuParser {
 public:
  static constexpr size_t kLen = 20;
  uint64_t packets_ok = 0;
  uint64_t bad_checksum = 0;
  uint64_t bytes_total = 0;

  // Appelle cb(const ImuSample&) pour chaque paquet valide trouvé.
  template <class F>
  void feed(const uint8_t* data, size_t n, F&& cb) {
    bytes_total += n;
    buf_.insert(buf_.end(), data, data + n);
    size_t pos = 0;
    while (buf_.size() - pos >= kLen) {
      const uint8_t* p = &buf_[pos];
      if (p[0] != 0xAA || p[1] != 0x55) { ++pos; continue; }
      uint8_t c = 0;
      for (size_t i = 2; i < 19; i++) c ^= p[i];
      if (c != p[19] || (p[2] != 1 && p[2] != 2)) {
        ++bad_checksum;
        ++pos;
        continue;
      }
      ImuSample s;
      s.type = p[2];
      uint32_t t32;
      std::memcpy(&t32, p + 3, 4);
      std::memcpy(s.v, p + 7, 12);
      if (!have_t_) {
        t64_ = t32;
        have_t_ = true;
      } else {
        t64_ += static_cast<int32_t>(t32 - last32_);  // gère le bouclage 32 bits
      }
      last32_ = t32;
      s.t_us = t64_;
      ++packets_ok;
      pos += kLen;
      cb(s);
    }
    buf_.erase(buf_.begin(), buf_.begin() + pos);
  }

 private:
  std::vector<uint8_t> buf_;
  bool have_t_ = false;
  uint32_t last32_ = 0;
  int64_t t64_ = 0;
};
