#pragma once
// Passerelle Bluetooth Low Energy générique pour Unity : liste les appareils à portée, se connecte à ceux que Unity enregistre
// (reconnexion automatique) et écrit dans leurs caractéristiques GATT. Protocole : voir ble_hub_proto.h.
#include <functional>
#include <string>

namespace ar6hub {
using LogFn = std::function<void(const std::string&)>;
void Start(LogFn log);   // ports UDP locaux 9881 (commandes) et 9882 (évènements)
void Stop();
}  // namespace ar6hub
