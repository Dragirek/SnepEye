#pragma once
// Canal de commande minimal (UDP, 127.0.0.1) : Unity envoie un caractère, ar6dof_probe l'exécute comme une touche.
// À inclure APRÈS <winsock2.h>.
#include <winsock2.h>
#include <ws2tcpip.h>

class UdpControl {
 public:
  ~UdpControl() { Close(); }
  bool Open(int port) {
    WSADATA w;
    if (WSAStartup(MAKEWORD(2, 2), &w) != 0) return false;
    started_ = true;
    s_ = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s_ == INVALID_SOCKET) return false;
    u_long nb = 1;
    ioctlsocket(s_, FIONBIO, &nb);  // lecture sans attente : on interroge à chaque tour de boucle
    sockaddr_in a{};
    a.sin_family = AF_INET;
    a.sin_port = htons((u_short)port);
    inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
    if (bind(s_, reinterpret_cast<sockaddr*>(&a), sizeof(a)) != 0) { Close(); return false; }
    return true;
  }
  // Prochaine commande (1 caractère) ou -1 s'il n'y en a pas.
  int Poll() {
    if (s_ == INVALID_SOCKET) return -1;
    char c[16];
    int n = recvfrom(s_, c, sizeof(c), 0, nullptr, nullptr);
    return n > 0 ? (unsigned char)c[0] : -1;
  }
  void Close() {
    if (s_ != INVALID_SOCKET) closesocket(s_);
    s_ = INVALID_SOCKET;
    if (started_) WSACleanup();
    started_ = false;
  }
 private:
  SOCKET s_ = INVALID_SOCKET;
  bool started_ = false;
};
