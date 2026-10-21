// Passerelle BLE générique (C++/WinRT). Fichier isolé : ni OpenCV ni le reste du programme.
#ifndef _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS
#define _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS 1
#endif
#include "udp_sender.h"   // Winsock d'abord, avant tout en-tête Windows
#include "ble_hub.h"
#include "ble_hub_proto.h"

#include <atomic>
#include <chrono>
#include <map>
#include <memory>
#include <mutex>
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

namespace ar6hub {
namespace {

constexpr int kCmdPort = 9881, kEvtPort = 9882;
using Clock = std::chrono::steady_clock;
int64_t NowMs() { return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now().time_since_epoch()).count(); }
void SleepMs(int ms) { std::this_thread::sleep_for(std::chrono::milliseconds(ms)); }

std::atomic<bool> g_stop{false};
std::atomic<int> g_workers{0};
std::thread g_manager, g_cmd;
LogFn g_log;
UdpSender g_evt;
std::mutex g_evtM;

void Emit(const std::string& s) {
  std::lock_guard<std::mutex> l(g_evtM);
  std::string line = s + "\n";
  g_evt.Send(reinterpret_cast<const uint8_t*>(line.data()), (int)line.size());
}

winrt::guid ToGuid(const Uuid& u) {
  return winrt::guid{u.d1, u.d2, u.d3, {u.d4[0], u.d4[1], u.d4[2], u.d4[3], u.d4[4], u.d4[5], u.d4[6], u.d4[7]}};
}

// ---- appareils connectés ----
struct Conn {
  uint64_t addr = 0;
  std::atomic<bool> connecting{false};
  std::atomic<int64_t> lastTry{0};
  std::mutex m;   // protège dev, session, chars
  wb::BluetoothLEDevice dev{nullptr};
  wg::GattSession session{nullptr};
  std::map<std::string, wg::GattCharacteristic> chars;
  std::string lastState;
  int64_t lastStateSent = 0;
};
std::mutex g_connM;
std::map<uint64_t, std::shared_ptr<Conn>> g_conns;

// ---- scan ----
std::atomic<int64_t> g_scanUntil{0};   // le scan reste actif tant que Unity le redemande
struct SeenDev { std::string name; int64_t lastSent = 0; };
std::mutex g_seenM;
std::map<uint64_t, SeenDev> g_seen;

std::string ToUtf8(const winrt::hstring& h) { return winrt::to_string(h); }

void ConnectWorker(std::shared_ptr<Conn> c) {
  g_workers++;
  try {
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    wb::BluetoothLEDevice dev = wb::BluetoothLEDevice::FromBluetoothAddressAsync(c->addr).get();
    if (dev && !g_stop) {
      wg::GattSession session = wg::GattSession::FromDeviceIdAsync(dev.BluetoothDeviceId()).get();
      session.MaintainConnection(true);   // garde la liaison ouverte
      auto r = dev.GetGattServicesAsync(wb::BluetoothCacheMode::Uncached).get();   // force la connexion réelle
      if (r.Status() == wg::GattCommunicationStatus::Success) {
        std::lock_guard<std::mutex> l(c->m);
        c->dev = dev;
        c->session = session;
        c->chars.clear();
      } else {
        try { session.Close(); } catch (...) {}
        try { dev.Close(); } catch (...) {}
      }
    }
  } catch (...) {
  }
  c->lastTry = NowMs();
  c->connecting = false;
  g_workers--;
}

void CloseConn(const std::shared_ptr<Conn>& c) {
  std::lock_guard<std::mutex> l(c->m);
  c->chars.clear();
  try { if (c->session) c->session.Close(); } catch (...) {}
  try { if (c->dev) c->dev.Close(); } catch (...) {}
  c->session = nullptr;
  c->dev = nullptr;
}

// Boucle de gestion : états, reconnexions, scan
void ManagerLoop() {
  winrt::init_apartment(winrt::apartment_type::multi_threaded);
  wa::BluetoothLEAdvertisementWatcher watcher{nullptr};
  winrt::event_token tok{};
  bool scanning = false;
  auto scanMode = wa::BluetoothLEScanningMode::Active;   // certains adaptateurs refusent le scan actif : repli sur le scan passif
  while (!g_stop) {
    int64_t now = NowMs();
    // scan à la demande
    bool want = now < g_scanUntil.load();
    if (want && !scanning) {
      try {
        watcher = wa::BluetoothLEAdvertisementWatcher();
        watcher.ScanningMode(scanMode);
        tok = watcher.Received([](wa::BluetoothLEAdvertisementWatcher const&, wa::BluetoothLEAdvertisementReceivedEventArgs const& a) {
          uint64_t addr = a.BluetoothAddress();
          std::string name = ToUtf8(a.Advertisement().LocalName());
          int rssi = (int)a.RawSignalStrengthInDBm();
          int64_t t = NowMs();
          bool send = false;
          {
            std::lock_guard<std::mutex> l(g_seenM);
            SeenDev& s = g_seen[addr];
            if (!name.empty() && name != s.name) { s.name = name; send = true; }
            if (t - s.lastSent > 1000) send = true;
            if (send) { s.lastSent = t; name = s.name; }
          }
          if (send) Emit("DEV " + FormatAddress(addr) + " " + std::to_string(rssi) + " " + name);
        });
        watcher.Start();
        scanning = true;
      } catch (...) {
        scanning = false;
        if (scanMode == wa::BluetoothLEScanningMode::Active) scanMode = wa::BluetoothLEScanningMode::Passive;
      }
    } else if (!want && scanning) {
      try { watcher.Stop(); watcher.Received(tok); } catch (...) {}
      scanning = false;
      std::lock_guard<std::mutex> l(g_seenM);
      g_seen.clear();
    }

    // états et reconnexions
    std::vector<std::shared_ptr<Conn>> list;
    {
      std::lock_guard<std::mutex> l(g_connM);
      for (auto& kv : g_conns) list.push_back(kv.second);
    }
    for (auto& c : list) {
      bool up = false;
      {
        std::lock_guard<std::mutex> l(c->m);
        try { up = c->dev && c->dev.ConnectionStatus() == wb::BluetoothConnectionStatus::Connected; } catch (...) { up = false; }
      }
      std::string st = up ? "connected" : (c->connecting ? "connecting" : "disconnected");
      if (st != c->lastState || now - c->lastStateSent > 2000) {
        if (st != c->lastState && g_log) g_log("[ble] " + FormatAddress(c->addr) + " : " + st);
        c->lastState = st;
        c->lastStateSent = now;
        Emit("STATE " + FormatAddress(c->addr) + " " + st);
      }
      if (!up && !c->connecting && now - c->lastTry > 3000) {
        {
          std::lock_guard<std::mutex> l(c->m);
          c->chars.clear();
          if (c->dev) { try { c->session.Close(); } catch (...) {} try { c->dev.Close(); } catch (...) {} c->dev = nullptr; c->session = nullptr; }
        }
        c->connecting = true;
        std::thread(ConnectWorker, c).detach();
      }
    }
    SleepMs(200);
  }
  if (scanning) { try { watcher.Stop(); watcher.Received(tok); } catch (...) {} }
}

bool DoWrite(const WriteCmd& w) {
  std::shared_ptr<Conn> c;
  {
    std::lock_guard<std::mutex> l(g_connM);
    auto it = g_conns.find(w.addr);
    if (it == g_conns.end()) return false;
    c = it->second;
  }
  wg::GattCharacteristic ch{nullptr};
  {
    std::lock_guard<std::mutex> l(c->m);
    if (!c->dev) return false;
    auto it = c->chars.find(w.key);
    if (it != c->chars.end()) ch = it->second;
  }
  if (!ch) {
    wb::BluetoothLEDevice dev{nullptr};
    { std::lock_guard<std::mutex> l(c->m); dev = c->dev; }
    if (!dev) return false;
    auto sr = dev.GetGattServicesForUuidAsync(ToGuid(w.service), wb::BluetoothCacheMode::Uncached).get();
    if (sr.Status() != wg::GattCommunicationStatus::Success || sr.Services().Size() == 0) return false;
    auto cr = sr.Services().GetAt(0).GetCharacteristicsForUuidAsync(ToGuid(w.chr), wb::BluetoothCacheMode::Uncached).get();
    if (cr.Status() != wg::GattCommunicationStatus::Success || cr.Characteristics().Size() == 0) return false;
    ch = cr.Characteristics().GetAt(0);
    std::lock_guard<std::mutex> l(c->m);
    c->chars.erase(w.key);
    c->chars.emplace(w.key, ch);   // pas de operator[] : les types WinRT n'ont pas de constructeur par défaut
  }
  ws::DataWriter dw;
  if (!w.data.empty()) dw.WriteBytes(winrt::array_view<const uint8_t>(w.data.data(), w.data.data() + w.data.size()));
  ws::IBuffer buf = dw.DetachBuffer();
  auto first = w.withResponse ? wg::GattWriteOption::WriteWithResponse : wg::GattWriteOption::WriteWithoutResponse;
  auto second = w.withResponse ? wg::GattWriteOption::WriteWithoutResponse : wg::GattWriteOption::WriteWithResponse;
  if (ch.WriteValueAsync(buf, first).get() == wg::GattCommunicationStatus::Success) return true;
  return ch.WriteValueAsync(buf, second).get() == wg::GattCommunicationStatus::Success;   // la caractéristique n'accepte peut-être que l'autre mode
}

void Handle(const std::string& line) {
  auto p = SplitSpaces(line, 3);
  if (p.empty()) return;
  if (p[0] == "PING") { Emit("PONG"); return; }
  if (p[0] == "SCAN" && p.size() >= 2) {
    if (p[1] == "1") g_scanUntil = NowMs() + 6000;
    else g_scanUntil = 0;
    return;
  }
  uint64_t addr;
  if ((p[0] == "CONNECT" || p[0] == "DISCONNECT") && p.size() >= 2 && ParseAddress(p[1], &addr)) {
    std::lock_guard<std::mutex> l(g_connM);
    if (p[0] == "CONNECT") {
      if (!g_conns.count(addr)) { auto c = std::make_shared<Conn>(); c->addr = addr; g_conns[addr] = c; }
    } else {
      auto it = g_conns.find(addr);
      if (it != g_conns.end()) { CloseConn(it->second); g_conns.erase(it); Emit("STATE " + FormatAddress(addr) + " disconnected"); }
    }
  }
}

// Reçoit les commandes ; fusionne les envois "dernier gagne" d'un même lot avant de les exécuter.
void CommandLoop() {
  winrt::init_apartment(winrt::apartment_type::multi_threaded);
  SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
  if (s == INVALID_SOCKET) { if (g_log) g_log("[ble] socket de commandes impossible"); return; }
  sockaddr_in a = {};
  a.sin_family = AF_INET;
  a.sin_port = htons(kCmdPort);
  inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
  if (bind(s, reinterpret_cast<sockaddr*>(&a), sizeof(a)) != 0) {
    if (g_log) g_log("[ble] port UDP 9881 deja utilise : passerelle Bluetooth des appareils desactivee");
    closesocket(s);
    return;
  }
  DWORD to = 200;
  setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char*>(&to), sizeof(to));
  char buf[4096];
  while (!g_stop) {
    std::vector<std::string> lines;
    for (int i = 0; i < 64; i++) {   // un lot : on vide ce qui est déjà arrivé
      int n = recv(s, buf, sizeof(buf) - 1, 0);
      if (n <= 0) break;
      buf[n] = 0;
      std::string all(buf, n), cur;
      for (char ch : all) { if (ch == '\n') { lines.push_back(cur); cur.clear(); } else cur.push_back(ch); }
      if (!cur.empty()) lines.push_back(cur);
      u_long avail = 0;
      ioctlsocket(s, FIONREAD, &avail);
      if (avail == 0) break;
    }
    std::vector<WriteCmd> writes;
    for (auto& l : lines) {
      if (l.rfind("WRITE", 0) == 0) { WriteCmd w; if (ParseWrite(l, &w)) writes.push_back(w); }
      else Handle(l);
    }
    if (!writes.empty()) {
      for (auto& w : Coalesce(writes)) {
        bool ok = false;
        try { ok = DoWrite(w); } catch (...) { ok = false; }
        if (!ok) Emit("ERR " + FormatAddress(w.addr) + " ecriture impossible");
      }
    }
  }
  closesocket(s);
}

}  // namespace

void Start(LogFn log) {
  if (g_manager.joinable()) return;
  g_log = std::move(log);
  g_stop = false;
  g_evt.Open("127.0.0.1", kEvtPort);
  g_manager = std::thread(ManagerLoop);
  g_cmd = std::thread(CommandLoop);
}

void Stop() {
  g_stop = true;
  if (g_cmd.joinable()) g_cmd.join();
  if (g_manager.joinable()) g_manager.join();
  for (int i = 0; i < 30 && g_workers > 0; i++) SleepMs(100);   // laisse finir les connexions en cours
  std::lock_guard<std::mutex> l(g_connM);
  for (auto& kv : g_conns) CloseConn(kv.second);
  g_conns.clear();
}

}  // namespace ar6hub
