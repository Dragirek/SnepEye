#pragma once
// Mémoire partagée "dernière image de la caméra" : ar6dof_probe écrit, Unity lit (sans copie par socket).
// Protocole sans verrou (seqlock) à 2 emplacements : le lecteur n'attend jamais l'écrivain.
//
// En-tête (64 octets)                      Emplacement i (i = frameId % 2), à 64 + i*slotBytes
//   0  uint32 magic 0x46365241 ('AR6F')       +0   uint64 seq   (impair = en cours d'écriture)
//   4  uint32 version = 1                      +8   uint64 frameId
//   8  uint32 largeur                          +16  double tWrite (secondes, horloge QueryPerformanceCounter)
//  12  uint32 hauteur                          +24  double tCapture (idem : instant d'arrivée lissé, sans gigue de décodage)
//  16  uint32 canaux (4 : BGRA)                +64  pixels BGRA 8 bits, ligne par ligne, du HAUT vers le bas
//  20  uint32 stride (octets par ligne)
//  24  uint32 slotBytes (= 64 + stride*hauteur)
//  28  uint32 slotCount = 2
//  32  uint64 latestFrameId   40  int32 latestSlot
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace fshare {
constexpr uint32_t kMagic = 0x46365241u;
constexpr uint32_t kVersion = 1;
constexpr size_t kHeaderBytes = 64, kSlotHeaderBytes = 64;
constexpr uint32_t kSlots = 2;

inline size_t SlotBytes(uint32_t w, uint32_t h, uint32_t ch) { return kSlotHeaderBytes + (size_t)w * ch * h; }
inline size_t TotalBytes(uint32_t w, uint32_t h, uint32_t ch) { return kHeaderBytes + kSlots * SlotBytes(w, h, ch); }

inline std::atomic<uint64_t>* A64(uint8_t* p) { return reinterpret_cast<std::atomic<uint64_t>*>(p); }
inline const std::atomic<uint64_t>* A64(const uint8_t* p) { return reinterpret_cast<const std::atomic<uint64_t>*>(p); }

// `base` doit être aligné sur 8 octets (une vue de fichier mappé l'est toujours).
inline void Init(uint8_t* base, uint32_t w, uint32_t h, uint32_t ch) {
  std::memset(base, 0, kHeaderBytes);
  uint32_t v[8] = {0, kVersion, w, h, ch, w * ch, (uint32_t)SlotBytes(w, h, ch), kSlots};
  std::memcpy(base + 4, v + 1, 28);
  for (uint32_t i = 0; i < kSlots; i++) std::memset(base + kHeaderBytes + i * SlotBytes(w, h, ch), 0, kSlotHeaderBytes);
  std::atomic_thread_fence(std::memory_order_release);
  std::memcpy(base, &kMagic, 4);  // le magic est écrit EN DERNIER : un lecteur ne voit jamais un en-tête à moitié écrit
}

// Écriture en deux temps : BeginWrite rend le pointeur où écrire directement les pixels (par exemple le résultat
// d'une conversion de format) : aucune copie intermédiaire. EndWrite publie l'image.
inline uint8_t* BeginWrite(uint8_t* base, uint64_t frameId) {
  uint32_t slot = (uint32_t)(frameId % kSlots);
  uint32_t w, h, ch;
  std::memcpy(&w, base + 8, 4);
  std::memcpy(&h, base + 12, 4);
  std::memcpy(&ch, base + 16, 4);
  uint8_t* s = base + kHeaderBytes + slot * SlotBytes(w, h, ch);
  uint64_t seq = A64(s)->load(std::memory_order_relaxed);
  A64(s)->store(seq + 1, std::memory_order_release);  // impair : écriture en cours
  std::atomic_thread_fence(std::memory_order_seq_cst);
  return s + kSlotHeaderBytes;
}

inline void EndWrite(uint8_t* base, uint64_t frameId, double tWrite, double tCapture) {
  uint32_t slot = (uint32_t)(frameId % kSlots);
  uint32_t w, h, ch;
  std::memcpy(&w, base + 8, 4);
  std::memcpy(&h, base + 12, 4);
  std::memcpy(&ch, base + 16, 4);
  uint8_t* s = base + kHeaderBytes + slot * SlotBytes(w, h, ch);
  std::memcpy(s + 8, &frameId, 8);
  std::memcpy(s + 16, &tWrite, 8);
  std::memcpy(s + 24, &tCapture, 8);
  std::atomic_thread_fence(std::memory_order_seq_cst);
  uint64_t seq = A64(s)->load(std::memory_order_relaxed);
  A64(s)->store(seq + 1, std::memory_order_release);  // pair : emplacement cohérent
  A64(base + 32)->store(frameId, std::memory_order_release);
  std::memcpy(base + 40, &slot, 4);
}

inline void Write(uint8_t* base, const uint8_t* pixels, uint64_t frameId, double tWrite, double tCapture) {
  uint32_t w, h, ch;
  std::memcpy(&w, base + 8, 4);
  std::memcpy(&h, base + 12, 4);
  std::memcpy(&ch, base + 16, 4);
  std::memcpy(BeginWrite(base, frameId), pixels, (size_t)w * ch * h);
  EndWrite(base, frameId, tWrite, tCapture);
}

// Lecture de la dernière image (même protocole que le C#). Retourne vrai si une NOUVELLE image
// cohérente a été copiée dans out (taille >= w*h*ch).
inline bool Read(const uint8_t* base, uint8_t* out, size_t outBytes, uint64_t& lastId, uint64_t& frameId, double& tWrite, double& tCapture) {
  uint32_t magic, w, h, ch;
  std::memcpy(&magic, base, 4);
  if (magic != kMagic) return false;
  std::memcpy(&w, base + 8, 4);
  std::memcpy(&h, base + 12, 4);
  std::memcpy(&ch, base + 16, 4);
  size_t sb = SlotBytes(w, h, ch), n = (size_t)w * ch * h;
  if (outBytes < n) return false;
  uint64_t latest = A64(base + 32)->load(std::memory_order_acquire);
  if (latest == 0 || latest == lastId) return false;
  uint32_t slot;
  std::memcpy(&slot, base + 40, 4);
  const uint8_t* s = base + kHeaderBytes + (slot % kSlots) * sb;
  uint64_t s1 = A64(s)->load(std::memory_order_acquire);
  if (s1 & 1) return false;
  std::memcpy(out, s + kSlotHeaderBytes, n);
  std::atomic_thread_fence(std::memory_order_seq_cst);
  uint64_t s2 = A64(s)->load(std::memory_order_acquire);
  if (s1 != s2) return false;  // l'écrivain a repris cet emplacement pendant la copie : on jette
  std::memcpy(&frameId, s + 8, 8);
  std::memcpy(&tWrite, s + 16, 8);
  std::memcpy(&tCapture, s + 24, 8);
  lastId = frameId;
  return true;
}
}  // namespace fshare
