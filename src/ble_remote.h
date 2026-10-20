#pragma once
// Pont Bluetooth Low Energy -> UDP local : reçoit la télécommande gyroscopique (ESP32-C3) en Bluetooth et la
// retransmet à Unity (127.0.0.1) dans le paquet de 28 octets déjà utilisé par Ar6dofRemote. Aucun appairage Windows,
// aucun mot de passe : le programme cherche tout seul l'appareil "AR6DoF-Remote" et se reconnecte s'il disparaît.
#include <functional>
#include <string>

namespace ar6ble {
using LogFn = std::function<void(const std::string&)>;
void Start(int udpPort, LogFn log);   // lance le thread Bluetooth (sans effet s'il tourne déjà)
void Stop();                          // arrête et attend la fin du thread
}  // namespace ar6ble
