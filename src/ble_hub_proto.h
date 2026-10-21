#pragma once
// Protocole texte entre Unity et ar6dof_probe pour piloter des appareils Bluetooth Low Energy (aucune dépendance Windows : testable).
//
// Unity -> programme (UDP 127.0.0.1:9881), une commande par ligne :
//   PING
//   SCAN 1|0                       liste les appareils à portée (à renvoyer toutes les ~2 s : sans nouvelle, le scan s'arrête)
//   CONNECT <adresse>              enregistre l'appareil : le programme se connecte et se reconnecte seul (idempotent)
//   DISCONNECT <adresse>           oublie l'appareil
//   WRITE <adresse> <service> <caracteristique> <octets en hexa> <drapeaux>
//        drapeaux : r = écriture avec réponse ; c = "dernier gagne" (curseurs : un envoi plus récent du même lot remplace les anciens) ; - = aucun
// Programme -> Unity (UDP 127.0.0.1:9882) :
//   PONG
//   DEV <adresse> <rssi> <nom...>      appareil vu (nom vide possible)
//   STATE <adresse> connected|connecting|disconnected
//   ERR <adresse> <message>
// <adresse> = 12 chiffres hexadécimaux (ex. A1B2C3D4E5F6). UUID = forme courte (FFE1) ou longue (8-4-4-4-12).
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

namespace ar6hub {

struct Uuid {
  uint32_t d1 = 0;
  uint16_t d2 = 0, d3 = 0;
  uint8_t d4[8] = {0, 0, 0, 0, 0, 0, 0, 0};
};

inline int HexVal(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  return -1;
}

inline std::vector<std::string> SplitSpaces(const std::string& s, size_t maxParts) {
  std::vector<std::string> out;
  size_t i = 0;
  while (i < s.size()) {
    while (i < s.size() && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
    if (i >= s.size()) break;
    if (out.size() + 1 == maxParts) {   // dernier morceau : le reste de la ligne tel quel (noms avec espaces)
      size_t e = s.size();
      while (e > i && (s[e - 1] == '\r' || s[e - 1] == '\n' || s[e - 1] == ' ')) e--;
      out.push_back(s.substr(i, e - i));
      break;
    }
    size_t j = i;
    while (j < s.size() && s[j] != ' ' && s[j] != '\t' && s[j] != '\r' && s[j] != '\n') j++;
    out.push_back(s.substr(i, j - i));
    i = j;
  }
  return out;
}

// "A1B2C3D4E5F6" -> 0xA1B2C3D4E5F6 (12 chiffres exactement)
inline bool ParseAddress(const std::string& s, uint64_t* out) {
  if (s.size() != 12) return false;
  uint64_t v = 0;
  for (char c : s) {
    int h = HexVal(c);
    if (h < 0) return false;
    v = (v << 4) | (uint64_t)h;
  }
  *out = v;
  return true;
}

inline std::string FormatAddress(uint64_t a) {
  static const char* d = "0123456789ABCDEF";
  std::string s(12, '0');
  for (int i = 11; i >= 0; i--) { s[i] = d[a & 0xF]; a >>= 4; }
  return s;
}

// "a1b2c3" ou "A1 B2 C3" -> octets. Chaîne vide = aucun octet (valide).
inline bool ParseHexBytes(const std::string& s, std::vector<uint8_t>* out) {
  out->clear();
  int hi = -1;
  for (char c : s) {
    if (c == ' ' || c == '-' || c == ':') continue;
    int h = HexVal(c);
    if (h < 0) return false;
    if (hi < 0) hi = h;
    else { out->push_back((uint8_t)((hi << 4) | h)); hi = -1; }
  }
  return hi < 0;
}

// "FFE1" -> 0000FFE1-0000-1000-8000-00805F9B34FB ; forme longue 8-4-4-4-12 acceptée (accolades facultatives)
inline bool ParseUuid(std::string s, Uuid* u) {
  while (!s.empty() && (s.front() == '{' || s.front() == ' ')) s.erase(s.begin());
  while (!s.empty() && (s.back() == '}' || s.back() == ' ')) s.pop_back();
  if (s.size() == 4 || s.size() == 8) {
    uint32_t v = 0;
    for (char c : s) { int h = HexVal(c); if (h < 0) return false; v = (v << 4) | (uint32_t)h; }
    u->d1 = v; u->d2 = 0x0000; u->d3 = 0x1000;
    const uint8_t base[8] = {0x80, 0x00, 0x00, 0x80, 0x5F, 0x9B, 0x34, 0xFB};
    for (int i = 0; i < 8; i++) u->d4[i] = base[i];
    return true;
  }
  if (s.size() != 36 || s[8] != '-' || s[13] != '-' || s[18] != '-' || s[23] != '-') return false;
  std::string h;
  for (char c : s) if (c != '-') h.push_back(c);
  std::vector<uint8_t> b;
  if (!ParseHexBytes(h, &b) || b.size() != 16) return false;
  u->d1 = ((uint32_t)b[0] << 24) | ((uint32_t)b[1] << 16) | ((uint32_t)b[2] << 8) | b[3];
  u->d2 = (uint16_t)((b[4] << 8) | b[5]);
  u->d3 = (uint16_t)((b[6] << 8) | b[7]);
  for (int i = 0; i < 8; i++) u->d4[i] = b[8 + i];
  return true;
}

inline std::string UuidKey(const Uuid& u) {
  char buf[40];
  std::snprintf(buf, sizeof(buf), "%08X-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X", u.d1, u.d2, u.d3, u.d4[0], u.d4[1], u.d4[2], u.d4[3], u.d4[4], u.d4[5], u.d4[6], u.d4[7]);
  return buf;
}

struct WriteCmd {
  uint64_t addr = 0;
  Uuid service, chr;
  std::vector<uint8_t> data;
  bool withResponse = false, coalesce = false;
  std::string key;   // adresse|service|caractéristique : sert à fusionner les envois "dernier gagne"
};

inline bool ParseWrite(const std::string& line, WriteCmd* w) {
  auto p = SplitSpaces(line, 6);   // WRITE addr svc chr hex flags
  if (p.size() < 5 || p[0] != "WRITE") return false;
  if (!ParseAddress(p[1], &w->addr) || !ParseUuid(p[2], &w->service) || !ParseUuid(p[3], &w->chr)) return false;
  if (!ParseHexBytes(p[4], &w->data)) return false;
  std::string fl = p.size() >= 6 ? p[5] : "-";
  w->withResponse = fl.find('r') != std::string::npos;
  w->coalesce = fl.find('c') != std::string::npos;
  w->key = FormatAddress(w->addr) + "|" + UuidKey(w->service) + "|" + UuidKey(w->chr);
  return true;
}

// Dans un lot d'envois, ne garde que le dernier de chaque clé pour les envois "dernier gagne" ; l'ordre des autres est conservé.
inline std::vector<WriteCmd> Coalesce(const std::vector<WriteCmd>& in) {
  std::vector<WriteCmd> out;
  for (size_t i = 0; i < in.size(); i++) {
    bool superseded = false;
    if (in[i].coalesce)
      for (size_t j = i + 1; j < in.size(); j++)
        if (in[j].coalesce && in[j].key == in[i].key) { superseded = true; break; }
    if (!superseded) out.push_back(in[i]);
  }
  return out;
}

}  // namespace ar6hub
