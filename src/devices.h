#pragma once
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mf.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "setupapi.lib")
#include <windows.h>
#include <setupapi.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfobjects.h>
#include <mferror.h>
#include <mfreadwrite.h>
#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <cwctype>
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

// Un mode que la camera annonce reellement (type natif Media Foundation).
struct CamMode {
  int w = 0, h = 0, fps = 0;
  unsigned long fourcc = 0;   // 'MJPG' = 0x47504A4D, 'YUY2' = 0x32595559, 'NV12'... (0 : format inconnu)
};
static const unsigned long kFourccMjpg = 0x47504A4DUL;

// Liste les modes (taille, cadence, format) que la camera numero `index` (meme numerotation que ListCameras) propose.
// A appeler depuis un fil sans fenetre (Media Foundation y initialise COM). Vide si la camera ne repond pas (deja utilisee...).
inline std::vector<CamMode> ListCameraModes(int index) {
  std::vector<CamMode> out;
  CoInitializeEx(nullptr, COINIT_MULTITHREADED);
  if (FAILED(MFStartup(MF_VERSION))) return out;
  IMFAttributes* attr = nullptr;
  if (SUCCEEDED(MFCreateAttributes(&attr, 1))) {
    attr->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
    IMFActivate** devs = nullptr;
    UINT32 count = 0;
    if (SUCCEEDED(MFEnumDeviceSources(attr, &devs, &count))) {
      if (index >= 0 && (UINT32)index < count) {
        IMFMediaSource* src = nullptr;
        if (SUCCEEDED(devs[index]->ActivateObject(IID_PPV_ARGS(&src))) && src) {
          IMFSourceReader* rd = nullptr;
          if (SUCCEEDED(MFCreateSourceReaderFromMediaSource(src, nullptr, &rd)) && rd) {
            for (DWORD i = 0; i < 512; i++) {
              IMFMediaType* mt = nullptr;
              HRESULT hr = rd->GetNativeMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, i, &mt);
              if (hr == MF_E_NO_MORE_TYPES) break;
              if (FAILED(hr) || !mt) continue;
              GUID sub = {};
              UINT32 w = 0, h = 0, n = 0, d = 1;
              if (SUCCEEDED(mt->GetGUID(MF_MT_SUBTYPE, &sub)) && SUCCEEDED(MFGetAttributeSize(mt, MF_MT_FRAME_SIZE, &w, &h)) && w > 0 && h > 0) {
                MFGetAttributeRatio(mt, MF_MT_FRAME_RATE, &n, &d);
                CamMode m;
                m.w = (int)w;
                m.h = (int)h;
                m.fps = d ? (int)((double)n / (double)d + 0.5) : 0;
                m.fourcc = (sub.Data1 > 0xFFFF) ? (unsigned long)sub.Data1 : 0UL;   // les petits codes sont des formats D3D (RGB...), pas des FOURCC
                if (m.fps > 0) out.push_back(m);
              }
              mt->Release();
            }
            rd->Release();
          }
          src->Shutdown();
          src->Release();
        }
      }
      for (UINT32 i = 0; i < count; i++) devs[i]->Release();
      CoTaskMemFree(devs);
    }
    attr->Release();
  }
  MFShutdown();
  std::sort(out.begin(), out.end(), [](const CamMode& a, const CamMode& b) {
    if (a.w != b.w) return a.w > b.w;
    if (a.h != b.h) return a.h > b.h;
    if (a.fps != b.fps) return a.fps > b.fps;
    return a.fourcc < b.fourcc;
  });
  out.erase(std::unique(out.begin(), out.end(), [](const CamMode& a, const CamMode& b) {
    return a.w == b.w && a.h == b.h && a.fps == b.fps && a.fourcc == b.fourcc;
  }), out.end());
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

// Un port COM avec le nom que Windows lui donne (Gestionnaire de peripheriques), au lieu d'un simple « COM5 ».
struct ComPortInfo {
  std::string id;      // « COM5 » : ce qui est enregistre dans la configuration et donne a ar6dof_probe
  std::string label;   // « ESP32 / USB natif (COM5) », « Silicon Labs CP210x USB to UART Bridge (COM3) »...
};

namespace devices_detail {
inline std::string WideToUtf8(const wchar_t* w) {
  const int n = WideCharToMultiByte(CP_UTF8, 0, w, -1, nullptr, 0, nullptr, nullptr);
  std::string s;
  if (n > 1) { s.assign((size_t)n - 1, '\0'); WideCharToMultiByte(CP_UTF8, 0, w, -1, &s[0], n, nullptr, nullptr); }
  return s;
}
// « USB Serial Device (COM5) » -> « COM5 » ; vide si le nom ne finit pas par (COMn)
inline std::string PortFromName(const std::string& n) {
  const size_t a = n.rfind("(COM");
  if (a == std::string::npos) return "";
  const size_t b = n.find(')', a);
  return b == std::string::npos ? "" : n.substr(a + 1, b - a - 1);
}
}  // namespace devices_detail

// Ports COM presents avec leur nom. Interroge Windows (SetupAPI) : a appeler seulement au demarrage et sur « Actualiser », jamais en boucle.
// Les ports que SetupAPI ne decrit pas (rare) sont quand meme listes, avec leur seul numero.
inline std::vector<ComPortInfo> ListComPortInfo() {
  std::vector<ComPortInfo> out;
  // classe « Ports (COM et LPT) »
  static const GUID kPorts = {0x4D36E978, 0xE325, 0x11CE, {0xBF, 0xC1, 0x08, 0x00, 0x2B, 0xE1, 0x03, 0x18}};
  HDEVINFO set = SetupDiGetClassDevsW(&kPorts, nullptr, nullptr, DIGCF_PRESENT);
  if (set != INVALID_HANDLE_VALUE) {
    SP_DEVINFO_DATA d;
    d.cbSize = sizeof(d);
    for (DWORD i = 0; SetupDiEnumDeviceInfo(set, i, &d); i++) {
      wchar_t fn[256] = {}, hw[512] = {};
      DWORD got = 0;
      if (!SetupDiGetDeviceRegistryPropertyW(set, &d, SPDRP_FRIENDLYNAME, nullptr, (PBYTE)fn, sizeof(fn) - 2, &got)) continue;
      std::string name = devices_detail::WideToUtf8(fn);
      const std::string id = devices_detail::PortFromName(name);
      if (id.empty()) continue;   // port sans numero COM (LPT...)
      std::string label = name;
      // L'USB natif d'un ESP32 (C3, S3...) s'appelle juste « USB Serial Device » ou « USB JTAG/serial debug unit » : son nom USB ne se change pas,
      // mais son identifiant Espressif (VID 303A) permet de l'annoncer clairement.
      if (SetupDiGetDeviceRegistryPropertyW(set, &d, SPDRP_HARDWAREID, nullptr, (PBYTE)hw, sizeof(hw) - 2, &got)) {
        std::wstring h(hw);
        for (auto& c : h) c = (wchar_t)std::towupper(c);
        if (h.find(L"VID_303A") != std::wstring::npos) label = "ESP32 (USB natif) (" + id + ")";
      }
      out.push_back({id, label});
    }
    SetupDiDestroyDeviceInfoList(set);
  }
  // ports connus du registre mais absents de la liste ci-dessus
  for (const auto& p : ListComPorts()) {
    bool known = false;
    for (const auto& o : out) if (o.id == p) known = true;
    if (!known) out.push_back({p, p});
  }
  std::sort(out.begin(), out.end(), [](const ComPortInfo& a, const ComPortInfo& b) {
    const int na = a.id.size() > 3 ? atoi(a.id.c_str() + 3) : 0, nb = b.id.size() > 3 ? atoi(b.id.c_str() + 3) : 0;
    return na != nb ? na < nb : a.id < b.id;
  });
  return out;
}
