#pragma once
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfobjects.h>
#include <cstring>
#include <string>
#include <vector>

// Liste les caméras dans l'ordre de Media Foundation, qui est aussi l'ordre
// des index d'OpenCV avec le backend CAP_MSMF.
inline std::vector<std::string> ListCameras() {
  std::vector<std::string> out;
  CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  if (FAILED(MFStartup(MF_VERSION))) return out;

  IMFAttributes* attr = nullptr;
  if (SUCCEEDED(MFCreateAttributes(&attr, 1))) {
    attr->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                  MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
    IMFActivate** devs = nullptr;
    UINT32 count = 0;
    if (SUCCEEDED(MFEnumDeviceSources(attr, &devs, &count))) {
      for (UINT32 i = 0; i < count; i++) {
        WCHAR* name = nullptr;
        UINT32 len = 0;
        std::string s = "(sans nom)";
        if (SUCCEEDED(devs[i]->GetAllocatedString(
                MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, &name, &len))) {
          int n = WideCharToMultiByte(CP_UTF8, 0, name, -1, nullptr, 0, nullptr, nullptr);
          if (n > 1) {
            s.assign(n - 1, '\0');
            WideCharToMultiByte(CP_UTF8, 0, name, -1, &s[0], n, nullptr, nullptr);
          }
          CoTaskMemFree(name);
        }
        out.push_back(s);
        devs[i]->Release();
      }
      CoTaskMemFree(devs);
    }
    attr->Release();
  }
  return out;
}

// Liste les ports COM présents (via le registre).
inline std::vector<std::string> ListComPorts() {
  std::vector<std::string> out;
  HKEY k;
  if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, "HARDWARE\\DEVICEMAP\\SERIALCOMM", 0,
                    KEY_READ, &k) != ERROR_SUCCESS)
    return out;
  for (DWORD i = 0;; i++) {
    char name[256], val[64];
    DWORD nl = sizeof(name), vl = sizeof(val) - 1, type = 0;
    if (RegEnumValueA(k, i, name, &nl, nullptr, &type,
                      reinterpret_cast<LPBYTE>(val), &vl) != ERROR_SUCCESS)
      break;
    if (type == REG_SZ) {
      val[vl] = 0;
      out.emplace_back(val);
    }
  }
  RegCloseKey(k);
  return out;
}
