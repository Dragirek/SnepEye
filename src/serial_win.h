#pragma once
#include <windows.h>
#include <cstdint>
#include <string>

class SerialPort {
 public:
  ~SerialPort() { close(); }

  bool open(const std::string& name, DWORD baud = 2000000) {
    std::string path = "\\\\.\\" + name;  // nécessaire pour COM10 et plus
    h_ = CreateFileA(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                     OPEN_EXISTING, 0, nullptr);
    if (h_ == INVALID_HANDLE_VALUE) return false;
    SetupComm(h_, 1 << 16, 1 << 16);

    DCB dcb{};
    dcb.DCBlength = sizeof(dcb);
    GetCommState(h_, &dcb);
    dcb.BaudRate = baud;  // sans effet réel en USB CDC
    dcb.ByteSize = 8;
    dcb.Parity = NOPARITY;
    dcb.StopBits = ONESTOPBIT;
    dcb.fBinary = TRUE;
    dcb.fOutxCtsFlow = FALSE;
    dcb.fOutxDsrFlow = FALSE;
    dcb.fOutX = FALSE;
    dcb.fInX = FALSE;
    dcb.fDtrControl = DTR_CONTROL_ENABLE;  // certains firmwares attendent DTR
    dcb.fRtsControl = RTS_CONTROL_DISABLE;
    SetCommState(h_, &dcb);  // échec toléré (CDC accepte tout débit)

    // Lecture : retourne dès qu'il y a des octets, sinon attend max 20 ms.
    COMMTIMEOUTS to{};
    to.ReadIntervalTimeout = MAXDWORD;
    to.ReadTotalTimeoutMultiplier = MAXDWORD;
    to.ReadTotalTimeoutConstant = 20;
    SetCommTimeouts(h_, &to);
    PurgeComm(h_, PURGE_RXCLEAR | PURGE_TXCLEAR);
    return true;
  }

  // Retourne le nombre d'octets lus (0 = timeout), -1 = erreur.
  int read(uint8_t* buf, size_t n) {
    DWORD got = 0;
    if (!ReadFile(h_, buf, static_cast<DWORD>(n), &got, nullptr)) return -1;
    return static_cast<int>(got);
  }

  void close() {
    if (h_ != INVALID_HANDLE_VALUE) CloseHandle(h_);
    h_ = INVALID_HANDLE_VALUE;
  }

 private:
  HANDLE h_ = INVALID_HANDLE_VALUE;
};
