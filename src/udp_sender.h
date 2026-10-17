#pragma once
// Envoi UDP minimal (Winsock) vers Unity sur la même machine. À inclure AVANT <windows.h>.
#include <winsock2.h>
#include <ws2tcpip.h>
#include <cstdint>

class UdpSender {
 public:
  ~UdpSender() { Close(); }
  bool Open(const char* ip, int port) {
    WSADATA w;
    if (WSAStartup(MAKEWORD(2, 2), &w) != 0) return false;
    started_ = true;
    s_ = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s_ == INVALID_SOCKET) return false;
    addr_ = {};
    addr_.sin_family = AF_INET;
    addr_.sin_port = htons((u_short)port);
    if (inet_pton(AF_INET, ip, &addr_.sin_addr) != 1) return false;
    open_ = true;
    return true;
  }
  bool IsOpen() const { return open_; }
  void Send(const uint8_t* d, int n) {
    if (open_) sendto(s_, reinterpret_cast<const char*>(d), n, 0, reinterpret_cast<const sockaddr*>(&addr_), sizeof(addr_));
  }
  void Close() {
    if (s_ != INVALID_SOCKET) closesocket(s_);
    s_ = INVALID_SOCKET;
    open_ = false;
    if (started_) WSACleanup();
    started_ = false;
  }
 private:
  SOCKET s_ = INVALID_SOCKET;
  sockaddr_in addr_{};
  bool open_ = false, started_ = false;
};
