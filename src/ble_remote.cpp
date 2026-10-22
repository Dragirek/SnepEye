// Pont BLE -> UDP (C++/WinRT). Fichier isolé : il n'inclut ni OpenCV ni le reste du programme.
#ifndef _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS
#define _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS 1   // cppwinrt en C++17 utilise <experimental/coroutine>
#endif
#include "udp_sender.h"   // Winsock d'abord, avant tout en-tête Windows
#include "ble_remote.h"

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <thread>

#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Devices.Bluetooth.h>
#include <winrt/Windows.Devices.Bluetooth.Advertisement.h>
#include <winrt/Windows.Devices.Bluetooth.GenericAttributeProfile.h>

namespace wb = winrt::Windows::Devices::Bluetooth;
namespace wa = winrt::Windows::Devices::Bluetooth::Advertisement;
namespace wg = winrt::Windows::Devices::Bluetooth::GenericAttributeProfile;
namespace ws = winrt::Windows::Storage::Streams;

namespace ar6ble {
namespace {

// Doivent être identiques à ceux du firmware ar6dof_remote.ino
const winrt::guid kService{0xa6d0f001, 0x5a1e, 0x4c7b, {0x9d, 0x3e, 0x6f, 0x2a, 0x8c, 0x1b, 0x00, 0x01}};
const winrt::guid kChar{0xa6d0f001, 0x5a1e, 0x4c7b, {0x9d, 0x3e, 0x6f, 0x2a, 0x8c, 0x1b, 0x00, 0x02}};

std::atomic<bool> g_stop{false};
std::thread g_thread;

using Clock = std::chrono::steady_clock;
int64_t NowMs() { return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now().time_since_epoch()).count(); }
void SleepMs(int ms) { std::this_thread::sleep_for(std::chrono::milliseconds(ms)); }

// Partagé avec le gestionnaire d'événements (qui peut s'exécuter sur un autre thread)
struct Ctx {
  UdpSender* udp = nullptr;
  std::atomic<int64_t> lastMs{0};
  std::atomic<uint32_t> count{0};
  uint16_t seq = 0;
};

// Paquet BLE (14 octets) : u8 boutons | u8 compteur | 4 x int16 quaternion (w,x,y,z) / 32767 | u32 micros
// Paquet UDP (28 octets) : voir Ar6dofRemote.cs
void Forward(Ctx& c, const uint8_t* b, uint32_t n) {
  if (n < 14) return;
  int16_t qi[4];
  std::memcpy(qi, b + 2, 8);
  float q[4];
  for (int i = 0; i < 4; i++) q[i] = (float)qi[i] / 32767.0f;
  uint32_t magic = 0x52365241u, us;
  std::memcpy(&us, b + 10, 4);
  uint8_t out[28];
  std::memcpy(out, &magic, 4);
  out[4] = 1;
  out[5] = b[0];
  std::memcpy(out + 6, &c.seq, 2);
  c.seq++;
  std::memcpy(out + 8, q, 16);
  std::memcpy(out + 24, &us, 4);
  c.udp->Send(out, 28);
  c.lastMs = NowMs();
  c.count++;
}

const char* g_step = "";   // dernière étape tentée (pour savoir où une erreur Windows s'est produite)

uint64_t FindRemote(const LogFn& log) {
  std::atomic<uint64_t> found{0};
  std::atomic<uint32_t> seen{0};
  std::atomic<int> stopErr{0};
  std::mutex namesM;
  std::set<std::string> names;   // noms des appareils vus (pour le diagnostic)
  wa::BluetoothLEAdvertisementWatcher watcher{nullptr};
  winrt::event_token tok{}, tokStop{};
  bool started = false;
  std::string lastError;
  // Scan actif d'abord (reçoit aussi le nom) ; certains adaptateurs le refusent ("le périphérique ne reconnaît pas la commande") :
  // on retombe alors sur le scan passif, suffisant car le service est dans l'annonce elle-même.
  const wa::BluetoothLEScanningMode modes[2] = {wa::BluetoothLEScanningMode::Active, wa::BluetoothLEScanningMode::Passive};
  for (int mi = 0; mi < 2 && !started; mi++) {
    try {
      watcher = wa::BluetoothLEAdvertisementWatcher();
      watcher.ScanningMode(modes[mi]);
      tokStop = watcher.Stopped([&stopErr](wa::BluetoothLEAdvertisementWatcher const&, wa::BluetoothLEAdvertisementWatcherStoppedEventArgs const& e) {
        stopErr = static_cast<int>(e.Error());   // 0 = arrêt normal ; sinon l'adaptateur Bluetooth est absent, coupé ou refusé
      });
      tok = watcher.Received([&found, &seen, &namesM, &names](wa::BluetoothLEAdvertisementWatcher const&, wa::BluetoothLEAdvertisementReceivedEventArgs const& a) {
        seen++;
        auto adv = a.Advertisement();
        {
          std::string nm = winrt::to_string(adv.LocalName());
          if (!nm.empty()) { std::lock_guard<std::mutex> l(namesM); if (names.size() < 12) names.insert(nm); }
        }
        bool ok = false;
        for (auto const& u : adv.ServiceUuids())
          if (u == kService) ok = true;
        if (!ok && adv.LocalName() == L"AR6DoF-Remote") ok = true;
        if (ok) found = a.BluetoothAddress();
      });
      watcher.Start();
      started = true;
      if (mi == 1) log("[ble] scan actif refuse par l'adaptateur : scan passif utilise");
    } catch (winrt::hresult_error const& e) {
      lastError = winrt::to_string(e.message());
      try { watcher.Received(tok); watcher.Stopped(tokStop); } catch (...) {}
    }
  }
  if (!started) {
    log("[ble] impossible de lancer la recherche Bluetooth : " + lastError + " (adaptateur absent, Bluetooth coupe ou pilote incompatible ?)");
    SleepMs(3000);
    return 0;
  }

  int64_t t0 = NowMs();
  bool said = false;
  while (!g_stop && found == 0) {
    SleepMs(100);
    if (!said && NowMs() - t0 > 5000) {
      if (stopErr != 0) log("[ble] adaptateur Bluetooth inutilisable (code " + std::to_string(stopErr.load()) + ") : branche-le, active le Bluetooth de Windows, installe son pilote");
      else if (seen == 0) log("[ble] aucune annonce Bluetooth recue : l'adaptateur ne voit rien (pilote ? Bluetooth coupe dans Windows ?), je continue de chercher");
      else {
        std::string list;
        { std::lock_guard<std::mutex> l(namesM); for (auto const& n : names) list += (list.empty() ? "" : ", ") + n; }
        log("[ble] " + std::to_string(seen.load()) + " annonces recues mais pas la telecommande. Noms vus : " + (list.empty() ? "(aucun nom)" : list) + ". Allume-la et regarde sa console serie ; je continue de chercher");
      }
      said = true;
    }
  }
  watcher.Stop();
  watcher.Received(tok);
  watcher.Stopped(tokStop);
  return found;
}

void RunOnce(UdpSender& udp, const LogFn& log) {
  log("[ble] recherche de la telecommande...");
  g_step = "recherche";
  uint64_t addr = FindRemote(log);
  if (g_stop || addr == 0) return;
  log("[ble] telecommande trouvee, connexion...");

  g_step = "ouverture de l'appareil";
  wb::BluetoothLEDevice dev = wb::BluetoothLEDevice::FromBluetoothAddressAsync(addr).get();
  if (!dev) { log("[ble] connexion impossible"); SleepMs(1500); return; }

#ifdef AR6DOF_BLE_PREFERRED_PARAMS   // desactive par defaut : certains pilotes (Realtek) refusent cette commande HCI
  // Demande à Windows un intervalle de connexion court (faible latence). Facultatif : ignoré si non supporté.
  wb::BluetoothLEPreferredConnectionParametersRequest prefReq{nullptr};
  try { prefReq = dev.RequestPreferredConnectionParameters(wb::BluetoothLEPreferredConnectionParameters::ThroughputOptimized()); } catch (...) {}
#endif

  g_step = "lecture du service GATT";
  // Certains pilotes Bluetooth (Realtek...) refusent « GetGattServicesForUuid » avec l'erreur 0x80070016 : on essaie plusieurs méthodes,
  // plusieurs fois (la première découverte peut échouer pendant que la connexion s'établit).
  wg::GattDeviceService svc{nullptr};
  std::string lastErr;
  for (int attempt = 0; attempt < 3 && !svc && !g_stop; attempt++) {
    for (int method = 0; method < 3 && !svc; method++) {
      try {
        wg::GattDeviceServicesResult sr{nullptr};
        if (method == 0) sr = dev.GetGattServicesForUuidAsync(kService, wb::BluetoothCacheMode::Uncached).get();
        else if (method == 1) sr = dev.GetGattServicesAsync(wb::BluetoothCacheMode::Uncached).get();
        else sr = dev.GetGattServicesAsync(wb::BluetoothCacheMode::Cached).get();
        if (sr.Status() == wg::GattCommunicationStatus::Success)
          for (auto const& sv : sr.Services())
            if (sv.Uuid() == kService) { svc = sv; break; }
      } catch (winrt::hresult_error const& e) {
        lastErr = "methode " + std::to_string(method + 1) + " : " + winrt::to_string(e.message()) + " (code " + std::to_string((unsigned)e.code().value) + ")";
      }
    }
    if (!svc) SleepMs(800);
  }
  if (!svc) {
    log(std::string("[ble] service de la telecommande introuvable") + (lastErr.empty() ? "" : " (" + lastErr + ")") +
        " : si l'appareil AR6DoF-Remote est dans les parametres Bluetooth de Windows, SUPPRIME-le (Peripheriques > ... > Supprimer) puis rallume la telecommande ; nouvel essai");
    dev.Close();
    SleepMs(1500);
    return;
  }
  g_step = "lecture de la caracteristique";
  auto cr = svc.GetCharacteristicsForUuidAsync(kChar, wb::BluetoothCacheMode::Uncached).get();
  if (cr.Status() != wg::GattCommunicationStatus::Success || cr.Characteristics().Size() == 0) {
    log("[ble] caracteristique introuvable, nouvel essai");
    svc.Close();
    dev.Close();
    SleepMs(1500);
    return;
  }
  wg::GattCharacteristic ch = cr.Characteristics().GetAt(0);

  auto ctx = std::make_shared<Ctx>();
  ctx->udp = &udp;
  ctx->lastMs = NowMs();
  auto tok = ch.ValueChanged([ctx](wg::GattCharacteristic const&, wg::GattValueChangedEventArgs const& a) {
    auto reader = ws::DataReader::FromBuffer(a.CharacteristicValue());
    uint32_t n = reader.UnconsumedBufferLength();
    uint8_t buf[32];
    if (n > sizeof(buf)) n = sizeof(buf);
    reader.ReadBytes(winrt::array_view<uint8_t>(buf, buf + n));
    Forward(*ctx, buf, n);
  });
  g_step = "activation des notifications";
  auto st = ch.WriteClientCharacteristicConfigurationDescriptorAsync(wg::GattClientCharacteristicConfigurationDescriptorValue::Notify).get();
  if (st != wg::GattCommunicationStatus::Success) {
    log("[ble] activation des notifications refusee, nouvel essai");
    ch.ValueChanged(tok);
    svc.Close();
    dev.Close();
    SleepMs(1500);
    return;
  }
  log("[ble] telecommande connectee");

  // Reste connecté tant que des paquets arrivent
  while (!g_stop) {
    SleepMs(200);
    if (dev.ConnectionStatus() != wb::BluetoothConnectionStatus::Connected) break;
    if (NowMs() - ctx->lastMs > 3000) break;
  }
  log("[ble] telecommande deconnectee");
  try { ch.ValueChanged(tok); } catch (...) {}
  try { svc.Close(); } catch (...) {}
  try { dev.Close(); } catch (...) {}
  SleepMs(500);
}

void Worker(int port, LogFn log) {
  winrt::init_apartment(winrt::apartment_type::multi_threaded);
  UdpSender udp;
  if (!udp.Open("127.0.0.1", port)) { log("[ble] socket UDP local impossible"); return; }
  while (!g_stop) {
    try {
      RunOnce(udp, log);
    } catch (winrt::hresult_error const& e) {
      log("[ble] erreur Bluetooth a l'etape \"" + std::string(g_step) + "\" : " + winrt::to_string(e.message()) + " (code " + std::to_string((unsigned)e.code().value) + ")");
      for (int i = 0; i < 30 && !g_stop; i++) SleepMs(100);
    } catch (...) {
      log("[ble] erreur inconnue");
      for (int i = 0; i < 30 && !g_stop; i++) SleepMs(100);
    }
  }
}

}  // namespace

void Start(int udpPort, LogFn log) {
  if (g_thread.joinable()) return;
  g_stop = false;
  g_thread = std::thread(Worker, udpPort, std::move(log));
}

void Stop() {
  g_stop = true;
  if (g_thread.joinable()) g_thread.join();
}

}  // namespace ar6ble
