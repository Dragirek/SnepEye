#include <atomic>
#include <cassert>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <thread>
#include <vector>
#include "../src/frame_share_core.h"

int main() {
  const uint32_t W = 640, H = 120, CH = 4;  // taille réduite, protocole identique
  size_t total = fshare::TotalBytes(W, H, CH);
  uint8_t* mem = static_cast<uint8_t*>(std::aligned_alloc(64, (total + 63) / 64 * 64));
  fshare::Init(mem, W, H, CH);

  std::atomic<bool> run{true};
  std::thread writer([&] {
    std::vector<uint8_t> px((size_t)W * CH * H);
    uint64_t id = 0;
    while (run) {
      ++id;
      std::fill(px.begin(), px.end(), (uint8_t)(id & 0xFF));  // toute l'image = id : une image déchirée se voit
      uint8_t* dst = fshare::BeginWrite(mem, id);
      std::memcpy(dst, px.data(), px.size());
      fshare::EndWrite(mem, id, (double)id, (double)id + 0.5);
    }
  });

  std::vector<uint8_t> out((size_t)W * CH * H);
  uint64_t last = 0, fid = 0, accepted = 0, torn_seen = 0;
  double t, tc;
  auto t0 = std::chrono::steady_clock::now();
  while (std::chrono::steady_clock::now() - t0 < std::chrono::seconds(3)) {
    if (fshare::Read(mem, out.data(), out.size(), last, fid, t, tc)) {
      ++accepted;
      uint8_t v = (uint8_t)(fid & 0xFF);
      for (uint8_t b : out) if (b != v) { ++torn_seen; break; }   // image acceptée mais incohérente = BUG
      assert(t == (double)fid && tc == (double)fid + 0.5);
    }
  }
  run = false;
  writer.join();
  std::printf("images acceptees: %llu, images dechirees acceptees: %llu (doit etre 0)\n",
              (unsigned long long)accepted, (unsigned long long)torn_seen);
  assert(accepted > 100 && torn_seen == 0);

  // Disposition des champs (doit correspondre au lecteur C#)
  uint32_t v;
  std::memcpy(&v, mem, 4);      assert(v == 0x46365241u);
  std::memcpy(&v, mem + 8, 4);  assert(v == W);
  std::memcpy(&v, mem + 12, 4); assert(v == H);
  std::memcpy(&v, mem + 20, 4); assert(v == W * CH);
  std::memcpy(&v, mem + 24, 4); assert(v == 64 + W * CH * H);
  std::free(mem);
  std::puts("TEST MEMOIRE PARTAGEE OK");
  return 0;
}
