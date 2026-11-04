// ar6dof_app : centre de contrôle Windows d'ar6dof_probe (aucune console, aucune question à taper).
//
//  - choix de la langue (français / English), caméra et port de l'IMU dans des listes, cases à cocher pour les options
//  - écrit ar6dof_config.txt (le même fichier que lit ar6dof_probe --auto), lance / arrête ar6dof_probe.exe
//  - boutons à la place des touches du clavier (recentrer, sol, cap, calibration du montage de l'IMU...) : ils envoient la touche par UDP 9877
//  - affiche le journal d'ar6dof_probe (ar6dof_probe.log) et ses statistiques en direct
//
// Compilé avec le reste du projet (CMakeLists.txt) ; à placer à côté d'ar6dof_probe.exe.
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <objbase.h>
#include <ole2.h>
#include <commctrl.h>
#include <shlobj.h>
#include <shellapi.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <map>
#include <string>
#include <thread>
#include <vector>

#include "devices.h"

#pragma comment(linker, "\"/manifestdependency:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")

namespace {

// ------------------------------------------------------------------ état global
bool g_en = false;                 // langue : false = français, true = English
HWND g_wnd = nullptr;
HFONT g_font = nullptr;
int g_dpi = 96;
bool g_loading = false;            // vrai pendant qu'on remplit les champs (pas de réaction aux changements)
bool g_running = false;
DWORD g_stopDeadline = 0;          // arrêt demandé : instant (GetTickCount) après lequel on force
DWORD g_msgUntil = 0;              // message temporaire affiché dans la barre du bas jusqu'à cet instant
HANDLE g_ourProc = nullptr;        // processus lancé par cette fenêtre
std::string g_lastStats;
std::string g_lastLogText;
ULONGLONG g_lastLogSize = ~0ull;

std::vector<std::string> g_cams;
std::vector<std::string> g_ports;

// Modes de la liste « Mode » : ceux que la caméra annonce vraiment (Media Foundation), pas des réglages écrits d'avance.
struct ModeEntry { int w, h, fps; };
std::vector<ModeEntry> g_modeList;          // contenu de la liste (sans l'entrée « personnalisé »)
std::map<int, std::vector<CamMode>> g_modeCache;   // modes déjà lus, par numéro de caméra
int g_modeState = 0;                        // 0 = modes lus sur la caméra ; 1 = la caméra n'a pas répondu (modes courants) ; 2 = deux caméras sans mode commun
bool g_modeNonMjpg = false;                 // la caméra n'annonce pas de MJPEG
bool g_prevTwo = false;                     // type de caméra avant le dernier changement (pour convertir la largeur)
HWND g_lblCam = nullptr;
std::map<std::string, std::string> g_cfg;

int S(int v) { return MulDiv(v, g_dpi, 96); }

struct Loc { HWND h; const wchar_t* fr; const wchar_t* en; };
std::vector<Loc> g_loc;

enum {
  ID_LANG = 100, ID_CAMTYPE, ID_CAM, ID_CAM_REFRESH, ID_CAMR, ID_CAMHINT, ID_MODE, ID_W, ID_H, ID_FPS, ID_BACKEND, ID_MFDIRECT,
  ID_PORT, ID_PORT_REFRESH, ID_CALDIR, ID_CALBROWSE, ID_BASE,
  ID_UDP, ID_SHARE, ID_BLE, ID_AUTOTRACK,
  ID_EYE, ID_EYEL, ID_EYER, ID_EYEFPS, ID_EYEBACK,
  ID_DIAG, ID_THREADS, ID_KEEP,
  ID_A_RESTART, ID_A_FLOOR, ID_A_YAW, ID_A_SWAP, ID_A_CHECK,
  ID_M_STEP1, ID_M_STEP2, ID_M_RESET,
  ID_LOG, ID_STATS, ID_SAVE, ID_START, ID_OPENDIR, ID_STATUS
};

// Modes courants proposés seulement quand la caméra ne répond pas à la question « quels modes as-tu ? » (déjà utilisée par ar6dof_probe, par exemple).
struct Preset { int w, h, fps; };
const Preset kPresets[] = {
  {3200, 1200, 60}, {2560, 720, 60}, {1600, 600, 120}, {1280, 480, 120}, {640, 240, 120},
};
const int kPresetCount = 5;

const wchar_t* Tr(const wchar_t* fr, const wchar_t* en) { return g_en ? en : fr; }

// ------------------------------------------------------------------ textes et chemins
std::wstring A2W(const std::string& s, UINT cp = CP_ACP) {
  if (s.empty()) return L"";
  int n = MultiByteToWideChar(cp, 0, s.c_str(), (int)s.size(), nullptr, 0);
  std::wstring w(n, L'\0');
  MultiByteToWideChar(cp, 0, s.c_str(), (int)s.size(), &w[0], n);
  return w;
}

std::string W2A(const std::wstring& w, UINT cp = CP_ACP) {
  if (w.empty()) return "";
  int n = WideCharToMultiByte(cp, 0, w.c_str(), (int)w.size(), nullptr, 0, nullptr, nullptr);
  std::string s(n, '\0');
  WideCharToMultiByte(cp, 0, w.c_str(), (int)w.size(), &s[0], n, nullptr, nullptr);
  return s;
}

// Texte du journal : UTF-8 (le programme est compilé en /utf-8) ; si ce n'est pas de l'UTF-8 valide, page de code de Windows.
std::wstring LogToW(const std::string& s) {
  if (s.empty()) return L"";
  int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.c_str(), (int)s.size(), nullptr, 0);
  if (n > 0) {
    std::wstring w(n, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.c_str(), (int)s.size(), &w[0], n);
    return w;
  }
  return A2W(s, CP_ACP);
}

std::wstring ExeDir() {
  wchar_t p[MAX_PATH];
  DWORD n = GetModuleFileNameW(nullptr, p, MAX_PATH);
  std::wstring s(p, n);
  size_t k = s.find_last_of(L"\\/");
  return k == std::wstring::npos ? L"." : s.substr(0, k);
}

std::wstring ConfigPath() { return ExeDir() + L"\\ar6dof_config.txt"; }
std::wstring LogPath() { return ExeDir() + L"\\ar6dof_probe.log"; }

bool FileExists(const std::wstring& p) {
  DWORD a = GetFileAttributesW(p.c_str());
  return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

// ------------------------------------------------------------------ configuration (même fichier que ar6dof_probe)
void LoadCfg() {
  g_cfg.clear();
  std::ifstream f{std::filesystem::path(ConfigPath())};
  std::string line;
  while (std::getline(f, line)) {
    if (!line.empty() && line.back() == '\r') line.pop_back();
    size_t e = line.find('=');
    if (e != std::string::npos) g_cfg[line.substr(0, e)] = line.substr(e + 1);
  }
}

bool SaveCfg() {
  std::ofstream f(std::filesystem::path(ConfigPath()), std::ios::trunc);
  if (!f) return false;
  for (const auto& kv : g_cfg) f << kv.first << "=" << kv.second << "\n";
  return (bool)f;
}

int CfgInt(const char* key, int def) {
  auto it = g_cfg.find(key);
  if (it == g_cfg.end()) return def;
  try { return std::stoi(it->second); } catch (...) { return def; }
}

std::string CfgStr(const char* key, const std::string& def) {
  auto it = g_cfg.find(key);
  return it == g_cfg.end() ? def : it->second;
}

// ------------------------------------------------------------------ création des contrôles
HWND Mk(const wchar_t* cls, const wchar_t* fr, const wchar_t* en, DWORD style, int x, int y, int w, int h, int id, DWORD ex = 0) {
  HWND c = CreateWindowExW(ex, cls, fr ? Tr(fr, en) : L"", WS_CHILD | WS_VISIBLE | style, S(x), S(y), S(w), S(h), g_wnd,
                           (HMENU)(INT_PTR)id, GetModuleHandleW(nullptr), nullptr);
  SendMessageW(c, WM_SETFONT, (WPARAM)g_font, TRUE);
  if (fr) g_loc.push_back({c, fr, en});
  return c;
}

HWND Group(const wchar_t* fr, const wchar_t* en, int x, int y, int w, int h) { return Mk(L"BUTTON", fr, en, BS_GROUPBOX, x, y, w, h, -1); }
HWND Label(const wchar_t* fr, const wchar_t* en, int x, int y, int w, int h = 20) { return Mk(L"STATIC", fr, en, SS_LEFT | SS_NOPREFIX, x, y, w, h, -1); }
HWND Check(const wchar_t* fr, const wchar_t* en, int x, int y, int w, int id) { return Mk(L"BUTTON", fr, en, BS_AUTOCHECKBOX | WS_TABSTOP, x, y, w, 22, id); }
HWND Button(const wchar_t* fr, const wchar_t* en, int x, int y, int w, int h, int id) { return Mk(L"BUTTON", fr, en, BS_PUSHBUTTON | WS_TABSTOP, x, y, w, h, id); }
HWND Combo(int x, int y, int w, int id) { return Mk(L"COMBOBOX", nullptr, nullptr, CBS_DROPDOWNLIST | WS_VSCROLL | WS_TABSTOP, x, y, w, 260, id); }
HWND Edit(int x, int y, int w, int id, bool number = false) {
  return Mk(L"EDIT", nullptr, nullptr, ES_AUTOHSCROLL | WS_TABSTOP | (number ? ES_NUMBER : 0), x, y, w, 24, id, WS_EX_CLIENTEDGE);
}

HWND H(int id) { return GetDlgItem(g_wnd, id); }

void SetText(int id, const std::wstring& t) { SetWindowTextW(H(id), t.c_str()); }
// Évite de réécrire (et de faire clignoter) un texte qui n'a pas changé.
void SetTextIfChanged(HWND h, const std::wstring& t) {
  int n = GetWindowTextLengthW(h);
  std::wstring cur(n + 1, L'\0');
  GetWindowTextW(h, &cur[0], n + 1);
  cur.resize(n);
  if (cur != t) SetWindowTextW(h, t.c_str());
}
std::wstring GetText(int id) {
  int n = GetWindowTextLengthW(H(id));
  std::wstring w(n + 1, L'\0');
  GetWindowTextW(H(id), &w[0], n + 1);
  w.resize(n);
  return w;
}
bool IsChecked(int id) { return SendMessageW(H(id), BM_GETCHECK, 0, 0) == BST_CHECKED; }
void SetChecked(int id, bool v) { SendMessageW(H(id), BM_SETCHECK, v ? BST_CHECKED : BST_UNCHECKED, 0); }
int GetSel(int id) { return (int)SendMessageW(H(id), CB_GETCURSEL, 0, 0); }
void SetSel(int id, int i) { SendMessageW(H(id), CB_SETCURSEL, i, 0); }
void ClearCombo(int id) { SendMessageW(H(id), CB_RESETCONTENT, 0, 0); }
void AddItem(int id, const std::wstring& t) { SendMessageW(H(id), CB_ADDSTRING, 0, (LPARAM)t.c_str()); }

// ------------------------------------------------------------------ remplissage des listes (dépend de la langue)
void ReadSizeFields(int* w, int* h, int* f) {
  *w = _wtoi(GetText(ID_W).c_str());
  *h = _wtoi(GetText(ID_H).c_str());
  *f = _wtoi(GetText(ID_FPS).c_str());
}

int ModeIndexFor(int w, int h, int fps) {
  for (size_t i = 0; i < g_modeList.size(); i++)
    if (g_modeList[i].w == w && g_modeList[i].h == h && g_modeList[i].fps == fps) return (int)i;
  return (int)g_modeList.size();   // « personnalisé »
}

void FillModes() {
  ClearCombo(ID_MODE);
  for (const auto& m : g_modeList)
    AddItem(ID_MODE, std::to_wstring(m.w) + L" × " + std::to_wstring(m.h) + Tr(L" à ", L" at ") + std::to_wstring(m.fps) + Tr(L" i/s", L" fps"));
  AddItem(ID_MODE, Tr(L"Personnalisé (valeurs ci-dessous)", L"Custom (values below)"));
  int w, h, f;
  ReadSizeFields(&w, &h, &f);
  SetSel(ID_MODE, ModeIndexFor(w, h, f));
}

void FillCamTypes() {
  int sel = GetSel(ID_CAMTYPE);
  ClearCombo(ID_CAMTYPE);
  AddItem(ID_CAMTYPE, Tr(L"Un module double (image côte à côte)", L"One dual module (side-by-side image)"));
  AddItem(ID_CAMTYPE, Tr(L"Deux caméras séparées (gauche + droite)", L"Two separate cameras (left + right)"));
  SetSel(ID_CAMTYPE, sel == 1 ? 1 : 0);
}

void FillBackends(int id) {
  int sel = GetSel(id);
  ClearCombo(id);
  AddItem(id, Tr(L"MSMF (Media Foundation, conseillé)", L"MSMF (Media Foundation, recommended)"));
  AddItem(id, L"DirectShow");
  SetSel(id, sel >= 0 ? sel : 0);
}

void FillCams(int id, bool withNone) {
  int sel = GetSel(id);
  ClearCombo(id);
  if (withNone) AddItem(id, Tr(L"Aucune", L"None"));
  for (size_t i = 0; i < g_cams.size(); i++) AddItem(id, L"[" + std::to_wstring(i) + L"] " + A2W(g_cams[i], CP_UTF8));
  SetSel(id, sel >= 0 ? sel : 0);
}

void FillPorts() {
  std::wstring cur;
  int sel = GetSel(ID_PORT);
  if (sel > 0 && (size_t)sel - 1 < g_ports.size()) cur = A2W(g_ports[sel - 1]);
  ClearCombo(ID_PORT);
  AddItem(ID_PORT, Tr(L"Pas d'IMU", L"No IMU"));
  for (const auto& p : g_ports) AddItem(ID_PORT, A2W(p));
  int idx = 0;
  for (size_t i = 0; i < g_ports.size(); i++)
    if (A2W(g_ports[i]) == cur) idx = (int)i + 1;
  SetSel(ID_PORT, idx);
}

void ScanDevices() {
  // Media Foundation veut son propre thread : la fenêtre, elle, est en appartement monothread (boîtes de dialogue)
  std::vector<std::string> cams;
  std::thread t([&cams] { cams = ListCameras(); });
  t.join();
  g_cams = cams;
  g_ports = ListComPorts();
}

// ------------------------------------------------------------------ modes réellement proposés par la ou les caméras
bool IsTwoCams() { return GetSel(ID_CAMTYPE) == 1; }

std::vector<CamMode> QueryModes(int idx) {
  auto it = g_modeCache.find(idx);
  if (it != g_modeCache.end()) return it->second;
  std::vector<CamMode> v;
  std::thread t([&v, idx] { v = ListCameraModes(idx); });   // Media Foundation sur son propre fil
  t.join();
  if (!v.empty()) g_modeCache[idx] = v;   // un échec (caméra occupée) n'est pas mémorisé : on réessaiera
  return v;
}

// MJPEG seulement (ar6dof_probe demande du MJPEG) ; si la caméra n'en annonce aucun, tous ses modes, avec un avertissement.
std::vector<ModeEntry> ModesOf(const std::vector<CamMode>& v, bool* nonMjpg) {
  bool anyMjpg = false;
  for (const auto& m : v) if (m.fourcc == kFourccMjpg) anyMjpg = true;
  if (nonMjpg) *nonMjpg = !v.empty() && !anyMjpg;
  std::vector<ModeEntry> out;
  for (const auto& m : v) {
    if (anyMjpg && m.fourcc != kFourccMjpg) continue;
    bool dup = false;
    for (const auto& e : out) if (e.w == m.w && e.h == m.h && e.fps == m.fps) dup = true;
    if (!dup) out.push_back({m.w, m.h, m.fps});
  }
  return out;
}

// Mode proposé d'office : cadence d'au moins 60 i/s si possible, taille proche de 1600 × 600 (l'image côte à côte) -> fluide et léger.
int PickDefaultMode(bool two) {
  double target = (two ? 800.0 : 1600.0) * 600.0;
  int best = -1;
  double bestScore = 1e30;
  for (size_t i = 0; i < g_modeList.size(); i++) {
    const ModeEntry& m = g_modeList[i];
    double score = std::fabs(std::log((double)m.w * m.h / target)) + (m.fps >= 60 ? 0.0 : 5.0) - 0.001 * m.fps;
    if (score < bestScore) { bestScore = score; best = (int)i; }
  }
  return best;
}

void UpdateCamType() {
  EnableWindow(H(ID_CAMR), IsTwoCams());
  if (g_lblCam) SetTextIfChanged(g_lblCam, IsTwoCams() ? Tr(L"Caméra gauche", L"Left camera") : Tr(L"Caméra", L"Camera"));
}

void UpdateCamHint() {
  int w, h, f;
  ReadSizeFields(&w, &h, &f);
  bool two = IsTwoCams();
  bool known = ModeIndexFor(w, h, f) < (int)g_modeList.size();
  std::wstring t;
  if (g_modeState == 1)
    t = Tr(L"La caméra n'a pas répondu (déjà utilisée ?) : modes courants affichés. Cliquer sur Actualiser pour réessayer.",
           L"The camera did not answer (already in use?): common modes shown. Click Refresh to try again.");
  else if (g_modeState == 2)
    t = Tr(L"Aucun mode commun aux deux caméras : saisis une taille à la main.", L"No mode common to both cameras: type a size by hand.");
  else if (g_modeNonMjpg)
    t = Tr(L"Cette caméra n'annonce pas de MJPEG : le mode choisi peut ne pas fonctionner.", L"This camera does not advertise MJPEG: the chosen mode may not work.");
  else if (!known)
    t = Tr(L"Ce mode n'est pas annoncé par la caméra : choisis-en un dans la liste.", L"The camera does not advertise this mode: pick one from the list.");
  else if (two)
    t = std::wstring(Tr(L"Image assemblée : ", L"Combined image: ")) + std::to_wstring(w * 2) + L" × " + std::to_wstring(h) + Tr(L" (gauche | droite)", L" (left | right)");
  else
    t = std::wstring(Tr(L"Chaque œil : ", L"Each eye: ")) + std::to_wstring(w / 2) + L" × " + std::to_wstring(h);
  SetTextIfChanged(H(ID_CAMHINT), t);
}

// Relit les modes de la ou des caméras choisies et remplit la liste. autoPick : si la taille saisie n'existe pas sur cette caméra, en choisir une qui existe.
void RebuildModes(bool autoPick) {
  bool prevLoading = g_loading;
  g_loading = true;
  bool two = IsTwoCams();
  SetCursor(LoadCursorW(nullptr, IDC_WAIT));
  bool nonL = false, nonR = false;
  std::vector<ModeEntry> a = ModesOf(QueryModes(std::max(0, GetSel(ID_CAM))), &nonL);
  std::vector<ModeEntry> b;
  if (two) b = ModesOf(QueryModes(std::max(0, GetSel(ID_CAMR))), &nonR);
  SetCursor(LoadCursorW(nullptr, IDC_ARROW));
  g_modeList.clear();
  g_modeNonMjpg = nonL || nonR;
  g_modeState = 0;
  if (a.empty() || (two && b.empty())) {
    g_modeState = 1;
    g_modeNonMjpg = false;
    for (int i = 0; i < kPresetCount; i++) g_modeList.push_back({two ? kPresets[i].w / 2 : kPresets[i].w, kPresets[i].h, kPresets[i].fps});
  } else if (!two) {
    g_modeList = a;
  } else {
    for (const auto& m : a)
      for (const auto& n : b)
        if (m.w == n.w && m.h == n.h && m.fps == n.fps) { g_modeList.push_back(m); break; }
    if (g_modeList.empty()) g_modeState = 2;
  }
  int w, h, f;
  ReadSizeFields(&w, &h, &f);
  if (autoPick && g_modeState != 1 && ModeIndexFor(w, h, f) >= (int)g_modeList.size()) {
    int p = PickDefaultMode(two);
    if (p >= 0) {
      SetText(ID_W, std::to_wstring(g_modeList[p].w));
      SetText(ID_H, std::to_wstring(g_modeList[p].h));
      SetText(ID_FPS, std::to_wstring(g_modeList[p].fps));
    }
  }
  FillModes();
  UpdateCamHint();
  g_loading = prevLoading;
}

void RefreshCameras() {
  ScanDevices();
  g_modeCache.clear();
  bool prevLoading = g_loading;
  g_loading = true;
  int sl = GetSel(ID_CAM), sr = GetSel(ID_CAMR), el = GetSel(ID_EYEL), er = GetSel(ID_EYER);
  FillCams(ID_CAM, false);
  FillCams(ID_CAMR, false);
  FillCams(ID_EYEL, true);
  FillCams(ID_EYER, true);
  FillPorts();
  int n = (int)g_cams.size();
  SetSel(ID_CAM, std::min(std::max(sl, 0), std::max(0, n - 1)));
  SetSel(ID_CAMR, std::min(std::max(sr, 0), std::max(0, n - 1)));
  SetSel(ID_EYEL, std::min(std::max(el, 0), n));
  SetSel(ID_EYER, std::min(std::max(er, 0), n));
  g_loading = prevLoading;
  RebuildModes(false);
}

// ------------------------------------------------------------------ configuration <-> champs
void UpdateEyeEnable() {
  bool on = IsChecked(ID_EYE);
  for (int id : {ID_EYEL, ID_EYER, ID_EYEFPS, ID_EYEBACK}) EnableWindow(H(id), on);
}

void ConfigToFields() {
  g_loading = true;
  int lastCam = std::max(0, (int)g_cams.size() - 1);
  int camR = CfgInt("camR", -1);
  bool two = camR >= 0;
  g_prevTwo = two;
  SetSel(ID_CAMTYPE, two ? 1 : 0);
  SetSel(ID_CAM, std::min(std::max(CfgInt("cam", 0), 0), lastCam));
  SetSel(ID_CAMR, std::min(std::max(two ? camR : 1, 0), lastCam));
  int w = CfgInt("width", 1600), h = CfgInt("height", 600), f = CfgInt("fps", 120);
  if (two) w /= 2;   // deux caméras : le fichier garde la largeur de l'image assemblée, la fenêtre montre celle d'UNE caméra
  SetText(ID_W, std::to_wstring(w));
  SetText(ID_H, std::to_wstring(h));
  SetText(ID_FPS, std::to_wstring(f));
  UpdateCamType();
  RebuildModes(g_cfg.find("width") == g_cfg.end());   // première utilisation : on propose un mode que la caméra a vraiment
  SetSel(ID_BACKEND, CfgInt("backend", 0) == 1 ? 1 : 0);
  SetChecked(ID_MFDIRECT, CfgInt("mfdirect", 0) != 0);

  std::string pn = CfgStr("portname", "");
  int pi = 0;
  for (size_t i = 0; i < g_ports.size(); i++) if (g_ports[i] == pn) pi = (int)i + 1;
  SetSel(ID_PORT, pi);

  std::string cd = CfgStr("calibdir", W2A(ExeDir() + L"\\calibration"));
  SetText(ID_CALDIR, cd == "-" ? L"" : A2W(cd));
  SetText(ID_BASE, A2W(CfgStr("baseline", "0")));

  SetText(ID_UDP, std::to_wstring(CfgInt("udp", 9876)));
  SetChecked(ID_SHARE, CfgInt("share", 1) != 0);
  SetChecked(ID_BLE, CfgInt("app_ble", 1) != 0);
  SetChecked(ID_AUTOTRACK, CfgInt("autotrack", 1) != 0);

  SetChecked(ID_EYE, CfgInt("eye", 0) != 0);
  SetSel(ID_EYEL, std::min(std::max(CfgInt("eyecamL", 1) + 1, 0), (int)g_cams.size()));
  SetSel(ID_EYER, std::min(std::max(CfgInt("eyecamR", 2) + 1, 0), (int)g_cams.size()));
  SetText(ID_EYEFPS, std::to_wstring(CfgInt("eyefps", 90)));
  SetSel(ID_EYEBACK, CfgInt("eyebackend", 0) == 1 ? 1 : 0);
  UpdateEyeEnable();

  SetChecked(ID_DIAG, CfgInt("windows", 0) != 0);
  SetText(ID_THREADS, std::to_wstring(CfgInt("app_cvthreads", 2)));
  SetChecked(ID_KEEP, CfgInt("app_keep", 0) != 0);
  g_loading = false;
}

bool ReadInt(int id, int lo, int hi, const wchar_t* frName, const wchar_t* enName, int* out) {
  std::wstring t = GetText(id);
  wchar_t* end = nullptr;
  long v = wcstol(t.c_str(), &end, 10);
  if (t.empty() || (end && *end != L'\0') || v < lo || v > hi) {
    std::wstring msg = std::wstring(Tr(frName, enName)) + Tr(L" : entre ", L": enter ") + std::to_wstring(lo) + Tr(L" et ", L" and ") + std::to_wstring(hi) + L".";
    MessageBoxW(g_wnd, msg.c_str(), Tr(L"Valeur invalide", L"Invalid value"), MB_OK | MB_ICONWARNING);
    SetFocus(H(id));
    return false;
  }
  *out = (int)v;
  return true;
}

// Lit les champs et écrit ar6dof_config.txt (les clés inconnues sont conservées).
bool SaveFields() {
  int w, h, f, udp, efps, thr;
  if (!ReadInt(ID_W, 320, 8000, L"Largeur", L"Width", &w)) return false;
  if (!ReadInt(ID_H, 240, 4000, L"Hauteur", L"Height", &h)) return false;
  if (!ReadInt(ID_FPS, 5, 240, L"Cadence", L"Frame rate", &f)) return false;
  if (!ReadInt(ID_UDP, 0, 65535, L"Port UDP", L"UDP port", &udp)) return false;
  if (!ReadInt(ID_EYEFPS, 10, 240, L"Cadence des yeux", L"Eye frame rate", &efps)) return false;
  if (!ReadInt(ID_THREADS, 1, 16, L"Threads de calcul", L"Compute threads", &thr)) return false;

  bool two = IsTwoCams();
  if (two && GetSel(ID_CAM) == GetSel(ID_CAMR)) {
    MessageBoxW(g_wnd, Tr(L"Choisis deux caméras différentes (gauche et droite).", L"Pick two different cameras (left and right)."),
                Tr(L"Caméra", L"Camera"), MB_OK | MB_ICONWARNING);
    SetFocus(H(ID_CAMR));
    return false;
  }
  LoadCfg();   // repart du fichier : ce que le programme y a écrit lui-même est conservé
  g_cfg["cam"] = std::to_string(std::max(0, GetSel(ID_CAM)));
  g_cfg["camR"] = two ? std::to_string(std::max(0, GetSel(ID_CAMR))) : std::string("-1");
  int ps = GetSel(ID_PORT);
  g_cfg["portname"] = (ps > 0 && (size_t)ps - 1 < g_ports.size()) ? g_ports[ps - 1] : "";
  g_cfg["width"] = std::to_string(two ? w * 2 : w);   // toujours la largeur TOTALE de l'image gauche|droite
  g_cfg["height"] = std::to_string(h);
  g_cfg["fps"] = std::to_string(f);
  g_cfg["backend"] = std::to_string(GetSel(ID_BACKEND) == 1 ? 1 : 0);
  g_cfg["mfdirect"] = IsChecked(ID_MFDIRECT) ? "1" : "0";
  std::string cd = W2A(GetText(ID_CALDIR));
  g_cfg["calibdir"] = cd.empty() ? "-" : cd;
  std::string base = W2A(GetText(ID_BASE));
  g_cfg["baseline"] = base.empty() ? "0" : base;
  g_cfg["udp"] = std::to_string(udp);
  g_cfg["share"] = IsChecked(ID_SHARE) ? "1" : "0";
  g_cfg["autotrack"] = IsChecked(ID_AUTOTRACK) ? "1" : "0";
  g_cfg["eye"] = IsChecked(ID_EYE) ? "1" : "0";
  g_cfg["eyecamL"] = std::to_string(GetSel(ID_EYEL) - 1);
  g_cfg["eyecamR"] = std::to_string(GetSel(ID_EYER) - 1);
  g_cfg["eyefps"] = std::to_string(efps);
  g_cfg["eyebackend"] = std::to_string(GetSel(ID_EYEBACK) == 1 ? 1 : 0);
  g_cfg["windows"] = IsChecked(ID_DIAG) ? "1" : "0";
  g_cfg["app_ble"] = IsChecked(ID_BLE) ? "1" : "0";
  g_cfg["app_cvthreads"] = std::to_string(thr);
  g_cfg["app_keep"] = IsChecked(ID_KEEP) ? "1" : "0";
  g_cfg["app_lang"] = g_en ? "en" : "fr";
  if (!SaveCfg()) {
    MessageBoxW(g_wnd, Tr(L"Impossible d'écrire ar6dof_config.txt (dossier protégé ?).", L"Cannot write ar6dof_config.txt (protected folder?)."),
                Tr(L"Enregistrement", L"Saving"), MB_OK | MB_ICONERROR);
    return false;
  }
  return true;
}

void SaveLanguageOnly() {
  LoadCfg();
  g_cfg["app_lang"] = g_en ? "en" : "fr";
  SaveCfg();
}

// ------------------------------------------------------------------ thème sombre / clair (suit le réglage « mode des applications » de Windows)
bool g_dark = false;
HBRUSH g_brBg = nullptr, g_brEdit = nullptr;
const COLORREF kDarkBg = RGB(32, 32, 32), kDarkEdit = RGB(48, 48, 48), kDarkText = RGB(236, 236, 236), kDarkDisabled = RGB(130, 130, 130);

bool SystemWantsDark() {
  HIGHCONTRASTW hc;
  memset(&hc, 0, sizeof(hc));
  hc.cbSize = sizeof(hc);
  if (SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(hc), &hc, 0) && (hc.dwFlags & HCF_HIGHCONTRASTON)) return false;   // contraste élevé : couleurs du système
  DWORD v = 1, sz = sizeof(v);
  if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD,
                   nullptr, &v, &sz) != ERROR_SUCCESS)
    return false;
  return v == 0;
}

typedef HRESULT(WINAPI* SetWindowThemeFn)(HWND, LPCWSTR, LPCWSTR);
typedef HRESULT(WINAPI* DwmSetWindowAttributeFn)(HWND, DWORD, LPCVOID, DWORD);

// uxtheme.dll / dwmapi.dll sont chargées à la demande : l'application reste lancée sur n'importe quelle version de Windows.
void SetCtlTheme(HWND c, LPCWSTR app, LPCWSTR list) {
  static SetWindowThemeFn fn = [] {
    HMODULE m = LoadLibraryW(L"uxtheme.dll");
    return m ? (SetWindowThemeFn)GetProcAddress(m, "SetWindowTheme") : (SetWindowThemeFn) nullptr;
  }();
  if (fn) fn(c, app, list);
}

BOOL CALLBACK ThemeChild(HWND c, LPARAM) {
  wchar_t cls[32] = L"";
  GetClassNameW(c, cls, 32);
  if (_wcsicmp(cls, L"Button") == 0) {
    DWORD type = (DWORD)GetWindowLongPtrW(c, GWL_STYLE) & 0xF;
    bool push = (type == BS_PUSHBUTTON || type == BS_DEFPUSHBUTTON);
    if (!g_dark) SetCtlTheme(c, nullptr, nullptr);
    else if (push) SetCtlTheme(c, L"DarkMode_Explorer", nullptr);
    else SetCtlTheme(c, L"", L"");   // cases à cocher et cadres : sans style visuel, pour que leur texte prenne la couleur demandée
  } else if (_wcsicmp(cls, L"ComboBox") == 0 || _wcsicmp(cls, L"Edit") == 0) {
    if (g_dark) SetCtlTheme(c, L"DarkMode_CFD", nullptr);
    else SetCtlTheme(c, nullptr, nullptr);
  }
  return TRUE;
}

void ApplyTheme() {
  g_dark = SystemWantsDark();
  if (!g_brBg) g_brBg = CreateSolidBrush(kDarkBg);
  if (!g_brEdit) g_brEdit = CreateSolidBrush(kDarkEdit);
  static DwmSetWindowAttributeFn dwm = [] {
    HMODULE m = LoadLibraryW(L"dwmapi.dll");
    return m ? (DwmSetWindowAttributeFn)GetProcAddress(m, "DwmSetWindowAttribute") : (DwmSetWindowAttributeFn) nullptr;
  }();
  BOOL on = g_dark ? TRUE : FALSE;
  if (dwm && FAILED(dwm(g_wnd, 20, &on, sizeof(on)))) dwm(g_wnd, 19, &on, sizeof(on));   // barre de titre sombre (20 : Windows 10 20H1+ et 11 ; 19 : versions précédentes)
  EnumChildWindows(g_wnd, ThemeChild, 0);
  RedrawWindow(g_wnd, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN);
}

// ------------------------------------------------------------------ ar6dof_probe : état, lancement, arrêt, commandes
bool ProbeRunning() {
  HANDLE s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (s == INVALID_HANDLE_VALUE) return false;
  PROCESSENTRY32W e;
  e.dwSize = sizeof(e);
  bool found = false;
  if (Process32FirstW(s, &e)) {
    do { if (_wcsicmp(e.szExeFile, L"ar6dof_probe.exe") == 0) { found = true; break; } } while (Process32NextW(s, &e));
  }
  CloseHandle(s);
  return found;
}

void KillProbe() {
  HANDLE s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (s == INVALID_HANDLE_VALUE) return;
  PROCESSENTRY32W e;
  e.dwSize = sizeof(e);
  if (Process32FirstW(s, &e)) {
    do {
      if (_wcsicmp(e.szExeFile, L"ar6dof_probe.exe") == 0) {
        HANDLE p = OpenProcess(PROCESS_TERMINATE, FALSE, e.th32ProcessID);
        if (p) { TerminateProcess(p, 0); CloseHandle(p); }
      }
    } while (Process32NextW(s, &e));
  }
  CloseHandle(s);
}

void SendCmd(char c) {
  static bool init = false;
  if (!init) { WSADATA w; WSAStartup(MAKEWORD(2, 2), &w); init = true; }
  SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
  if (s == INVALID_SOCKET) return;
  sockaddr_in a;
  memset(&a, 0, sizeof(a));
  a.sin_family = AF_INET;
  a.sin_port = htons(9877);   // canal de commande d'ar6dof_probe (le même que celui d'Unity)
  inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
  sendto(s, &c, 1, 0, reinterpret_cast<sockaddr*>(&a), sizeof(a));
  closesocket(s);
}

void StartProbe() {
  if (!SaveFields()) return;
  std::wstring exe = ExeDir() + L"\\ar6dof_probe.exe";
  if (!FileExists(exe)) {
    MessageBoxW(g_wnd, Tr(L"ar6dof_probe.exe est introuvable : il doit être dans le même dossier que cette application.",
                          L"ar6dof_probe.exe not found: it must be in the same folder as this application."),
                Tr(L"Démarrer", L"Start"), MB_OK | MB_ICONERROR);
    return;
  }
  if (ProbeRunning()) return;
  std::wstring cmd = L"\"" + exe + L"\" --auto --cv-threads " + std::to_wstring(CfgInt("app_cvthreads", 2));
  if (!IsChecked(ID_DIAG)) cmd += L" --no-windows";
  if (!IsChecked(ID_BLE)) cmd += L" --no-ble";
  if (!IsChecked(ID_KEEP)) cmd += L" --parent " + std::to_wstring(GetCurrentProcessId());   // s'arrête tout seul quand cette fenêtre se ferme
  std::vector<wchar_t> buf(cmd.begin(), cmd.end());
  buf.push_back(L'\0');
  STARTUPINFOW si;
  memset(&si, 0, sizeof(si));
  si.cb = sizeof(si);
  PROCESS_INFORMATION pi;
  memset(&pi, 0, sizeof(pi));
  std::wstring dir = ExeDir();
  if (!CreateProcessW(exe.c_str(), buf.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, dir.c_str(), &si, &pi)) {
    MessageBoxW(g_wnd, Tr(L"Le lancement d'ar6dof_probe.exe a échoué.", L"Launching ar6dof_probe.exe failed."), Tr(L"Démarrer", L"Start"), MB_OK | MB_ICONERROR);
    return;
  }
  if (g_ourProc) CloseHandle(g_ourProc);
  g_ourProc = pi.hProcess;
  CloseHandle(pi.hThread);
  g_lastLogSize = ~0ull;
}

void StopProbe() {
  SendCmd('Q');
  g_stopDeadline = GetTickCount() + 4000;   // arrêt propre d'abord ; sinon on force
}

// ------------------------------------------------------------------ journal
std::string ReadLogTail(ULONGLONG* sizeOut) {
  HANDLE f = CreateFileW(LogPath().c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
  if (f == INVALID_HANDLE_VALUE) { *sizeOut = 0; return ""; }
  LARGE_INTEGER sz;
  GetFileSizeEx(f, &sz);
  *sizeOut = (ULONGLONG)sz.QuadPart;
  const LONGLONG want = 48 * 1024;
  LONGLONG start = sz.QuadPart > want ? sz.QuadPart - want : 0;
  LARGE_INTEGER pos;
  pos.QuadPart = start;
  SetFilePointerEx(f, pos, nullptr, FILE_BEGIN);
  std::string data((size_t)(sz.QuadPart - start), '\0');
  DWORD got = 0;
  if (!data.empty()) ReadFile(f, &data[0], (DWORD)data.size(), &got, nullptr);
  data.resize(got);
  CloseHandle(f);
  if (start > 0) { size_t nl = data.find('\n'); if (nl != std::string::npos) data.erase(0, nl + 1); }   // 1re ligne coupée : on la saute
  return data;
}

bool IsStatsLine(const std::string& l) { return l.rfind("camera ", 0) == 0 && l.find("| suivi ") != std::string::npos; }

void RefreshLog() {
  ULONGLONG size = 0;
  std::string data = ReadLogTail(&size);
  if (size == g_lastLogSize) return;
  g_lastLogSize = size;
  std::vector<std::string> lines;
  size_t p = 0;
  while (p < data.size()) {
    size_t e = data.find('\n', p);
    if (e == std::string::npos) e = data.size();
    std::string l = data.substr(p, e - p);
    if (!l.empty() && l.back() == '\r') l.pop_back();
    p = e + 1;
    if (l.empty()) continue;
    if (IsStatsLine(l)) { g_lastStats = l; continue; }   // une ligne par seconde : affichée en bas, pas dans le journal
    lines.push_back(l);
  }
  const size_t keep = 80;
  if (lines.size() > keep) lines.erase(lines.begin(), lines.end() - keep);
  std::string text;
  for (const auto& l : lines) { text += l; text += "\r\n"; }
  if (text == g_lastLogText) return;
  g_lastLogText = text;
  HWND log = H(ID_LOG);
  SetWindowTextW(log, LogToW(text).c_str());
  SendMessageW(log, EM_LINESCROLL, 0, 100000);   // jusqu'en bas
}

// ------------------------------------------------------------------ état de la fenêtre
void UpdateState() {
  bool running = ProbeRunning();
  if (running != g_running) g_running = running;
  if (!running) g_stopDeadline = 0;
  SetTextIfChanged(H(ID_START), running ? Tr(L"■  Arrêter le suivi", L"■  Stop tracking") : Tr(L"▶  Démarrer le suivi", L"▶  Start tracking"));
  for (int id : {ID_A_RESTART, ID_A_FLOOR, ID_A_YAW, ID_A_SWAP, ID_A_CHECK, ID_M_STEP1, ID_M_STEP2, ID_M_RESET}) EnableWindow(H(id), running);
  if (g_msgUntil == 0 || (LONG)(GetTickCount() - g_msgUntil) > 0) {
    g_msgUntil = 0;
    SetTextIfChanged(H(ID_STATUS), running ? Tr(L"En marche", L"Running") : Tr(L"Arrêté", L"Stopped"));
  }
  SetTextIfChanged(H(ID_STATS), running && !g_lastStats.empty() ? LogToW(g_lastStats) : std::wstring());
  if (g_stopDeadline && running && (LONG)(GetTickCount() - g_stopDeadline) > 0) { KillProbe(); g_stopDeadline = 0; }
}

void ApplyLanguage() {
  for (const auto& l : g_loc) SetTextIfChanged(l.h, Tr(l.fr, l.en));
  SetWindowTextW(g_wnd, Tr(L"AR6DoF — Centre de contrôle", L"AR6DoF — Control center"));
  g_loading = true;
  FillCamTypes();
  FillModes();
  FillBackends(ID_BACKEND);
  FillBackends(ID_EYEBACK);
  FillCams(ID_EYEL, true);
  FillCams(ID_EYER, true);
  FillPorts();
  g_loading = false;
  UpdateCamType();
  UpdateCamHint();
  UpdateState();
}

void BrowseCalibDir() {
  BROWSEINFOW bi;
  memset(&bi, 0, sizeof(bi));
  bi.hwndOwner = g_wnd;
  bi.lpszTitle = Tr(L"Dossier de calibration RTAB-Map", L"RTAB-Map calibration folder");
  bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
  PIDLIST_ABSOLUTE pidl = SHBrowseForFolderW(&bi);
  if (!pidl) return;
  wchar_t path[MAX_PATH];
  if (SHGetPathFromIDListW(pidl, path)) SetText(ID_CALDIR, path);
  CoTaskMemFree(pidl);
}

void OnModeChanged() {
  if (g_loading) return;
  int i = GetSel(ID_MODE);
  if (i < 0 || i >= (int)g_modeList.size()) return;
  g_loading = true;
  SetText(ID_W, std::to_wstring(g_modeList[i].w));
  SetText(ID_H, std::to_wstring(g_modeList[i].h));
  SetText(ID_FPS, std::to_wstring(g_modeList[i].fps));
  g_loading = false;
  UpdateCamHint();
}

void OnSizeEdited() {
  if (g_loading) return;
  int w, h, f;
  ReadSizeFields(&w, &h, &f);
  g_loading = true;
  SetSel(ID_MODE, ModeIndexFor(w, h, f));
  g_loading = false;
  UpdateCamHint();
}

// Passage module double <-> deux caméras : la largeur saisie change de sens (image entière / une seule caméra).
void OnCamTypeChanged() {
  if (g_loading) return;
  bool two = IsTwoCams();
  if (two != g_prevTwo) {
    int w = _wtoi(GetText(ID_W).c_str());
    if (w > 0) {
      g_loading = true;
      SetText(ID_W, std::to_wstring(two ? w / 2 : w * 2));
      g_loading = false;
    }
    g_prevTwo = two;
  }
  UpdateCamType();
  RebuildModes(true);
}

// ------------------------------------------------------------------ construction de la fenêtre
void CreateControls() {
  // colonne de gauche : x 12..422 ; colonne de droite : x 438..848
  Group(L"Caméra stéréo", L"Stereo camera", 12, 6, 410, 222);
  Label(L"Type", L"Type", 24, 28, 90);
  Combo(120, 24, 290, ID_CAMTYPE);
  g_lblCam = Label(L"Caméra", L"Camera", 24, 56, 94);
  Combo(120, 52, 214, ID_CAM);
  Button(L"Actualiser", L"Refresh", 340, 51, 70, 26, ID_CAM_REFRESH);
  Label(L"Caméra droite", L"Right camera", 24, 84, 94);
  Combo(120, 80, 290, ID_CAMR);
  Label(L"Mode", L"Mode", 24, 112, 90);
  Combo(120, 108, 290, ID_MODE);
  Label(L"Taille / cadence", L"Size / rate", 24, 140, 96);
  Edit(120, 136, 62, ID_W, true);
  Label(L"×", L"×", 188, 140, 14);
  Edit(204, 136, 62, ID_H, true);
  Label(L"à", L"at", 272, 140, 18);
  Edit(292, 136, 48, ID_FPS, true);
  Label(L"i/s", L"fps", 346, 140, 40);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, 24, 166, 386, 32, ID_CAMHINT);
  Check(L"Lecteur direct Media Foundation (moins de latence et de CPU)", L"Direct Media Foundation reader (lower latency and CPU)", 24, 198, 386, ID_MFDIRECT);

  Group(L"Calibration de la caméra", L"Camera calibration", 12, 234, 410, 84);
  Label(L"Dossier", L"Folder", 24, 258, 90);
  Edit(120, 254, 218, ID_CALDIR);
  Button(L"Parcourir…", L"Browse…", 344, 253, 66, 26, ID_CALBROWSE);
  Label(L"Base réelle (mm)", L"Real baseline (mm)", 24, 288, 96);
  Edit(120, 284, 60, ID_BASE);
  Label(L"0 = celle de la calibration", L"0 = keep the calibration’s", 190, 288, 220);

  Group(L"Avancé", L"Advanced", 12, 324, 410, 126);
  Check(L"Fenêtres de diagnostic OpenCV (plus de CPU)", L"OpenCV diagnostic windows (more CPU)", 24, 344, 386, ID_DIAG);
  Label(L"Threads de calcul", L"Compute threads", 24, 372, 130);
  Edit(160, 368, 44, ID_THREADS, true);
  Label(L"Pilote caméra", L"Camera driver", 24, 400, 96);
  Combo(120, 396, 290, ID_BACKEND);
  Check(L"Laisser tourner quand je ferme cette fenêtre", L"Keep running when I close this window", 24, 424, 386, ID_KEEP);

  Group(L"Sorties vers Unity", L"Output to Unity", 438, 6, 410, 130);
  Label(L"Port UDP", L"UDP port", 450, 30, 100);
  Edit(556, 26, 64, ID_UDP, true);
  Label(L"0 = désactivé", L"0 = off", 628, 30, 200);
  Check(L"Partager l'image de la caméra (passthrough)", L"Share the camera image (passthrough)", 450, 52, 386, ID_SHARE);
  Check(L"Télécommande Bluetooth", L"Bluetooth remote", 450, 76, 386, ID_BLE);
  Check(L"Démarrer le suivi dès la première image", L"Start tracking on the first frame", 450, 100, 386, ID_AUTOTRACK);

  Group(L"Suivi des yeux (eye tracking)", L"Eye tracking", 438, 142, 410, 138);
  Check(L"Activer avec 2 caméras d'œil", L"Enable with 2 eye cameras", 450, 162, 386, ID_EYE);
  Label(L"Œil gauche", L"Left eye", 450, 190, 100);
  Combo(556, 186, 280, ID_EYEL);
  Label(L"Œil droit", L"Right eye", 450, 218, 100);
  Combo(556, 214, 280, ID_EYER);
  Label(L"Cadence", L"Rate", 450, 248, 100);
  Edit(556, 244, 48, ID_EYEFPS, true);
  Label(L"Pilote", L"Driver", 614, 248, 50);
  Combo(666, 244, 170, ID_EYEBACK);

  Group(L"Capteur de mouvement (IMU)", L"Motion sensor (IMU)", 438, 286, 410, 186);
  Label(L"Port série", L"Serial port", 450, 310, 70);
  Combo(526, 306, 230, ID_PORT);
  Button(L"Actualiser", L"Refresh", 762, 305, 74, 26, ID_PORT_REFRESH);
  Label(L"Montage (une seule fois) : 1. Pose le casque à plat, à l'endroit, sans bouger, puis clique sur « 1 ».\r\n2. Penche-le vers l'avant (pointe vers le haut), immobile, puis clique sur « 2 ».",
        L"Mounting (once): 1. Put the headset flat, the right way up, motionless, then click “1”.\r\n2. Tilt it forward (nose up), motionless, then click “2”.", 450, 340, 386, 54);
  Button(L"1 · Casque à plat", L"1 · Headset flat", 450, 402, 186, 30, ID_M_STEP1);
  Button(L"2 · Penché en avant", L"2 · Tilted forward", 650, 402, 186, 30, ID_M_STEP2);
  Button(L"Réinitialiser le montage", L"Reset the mounting", 450, 438, 386, 28, ID_M_RESET);

  Group(L"Pendant le suivi (remplacent les touches du clavier)", L"While tracking (replace the keyboard keys)", 12, 478, 836, 62);
  Button(L"Redémarrer le suivi", L"Restart tracking", 24, 500, 156, 30, ID_A_RESTART);
  Button(L"Verrouiller le sol", L"Lock the floor", 188, 500, 156, 30, ID_A_FLOOR);
  Button(L"Cap à zéro", L"Zero the heading", 352, 500, 156, 30, ID_A_YAW);
  Button(L"Inverser gauche / droite", L"Swap left / right", 516, 500, 156, 30, ID_A_SWAP);
  Button(L"Vérifier la calibration", L"Check the calibration", 680, 500, 156, 30, ID_A_CHECK);

  Mk(L"EDIT", nullptr, nullptr, ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL, 12, 546, 836, 44, ID_LOG, WS_EX_CLIENTEDGE);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX | SS_ENDELLIPSIS, 14, 594, 832, 18, ID_STATS);

  Button(L"Enregistrer", L"Save", 12, 618, 120, 32, ID_SAVE);
  Button(L"▶  Démarrer le suivi", L"▶  Start tracking", 140, 618, 190, 32, ID_START);
  Button(L"Ouvrir le dossier", L"Open the folder", 338, 618, 130, 32, ID_OPENDIR);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX | SS_ENDELLIPSIS, 484, 625, 208, 20, ID_STATUS);
  Combo(704, 620, 144, ID_LANG);
}

LRESULT CALLBACK WndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
  switch (m) {
    case WM_ERASEBKGND:
      if (g_dark) {
        RECT rc;
        GetClientRect(h, &rc);
        FillRect((HDC)w, &rc, g_brBg);
        return 1;
      }
      break;
    case WM_CTLCOLORSTATIC:
    case WM_CTLCOLOREDIT:
    case WM_CTLCOLORLISTBOX:
    case WM_CTLCOLORBTN:
      if (g_dark) {
        HDC dc = (HDC)w;
        HWND ctl = (HWND)l;
        wchar_t cls[16] = L"";
        GetClassNameW(ctl, cls, 16);
        bool field = (m == WM_CTLCOLOREDIT || m == WM_CTLCOLORLISTBOX || _wcsicmp(cls, L"Edit") == 0);   // champ de saisie, liste, journal (EDIT en lecture seule)
        SetTextColor(dc, IsWindowEnabled(ctl) ? kDarkText : kDarkDisabled);
        SetBkColor(dc, field ? kDarkEdit : kDarkBg);
        return (LRESULT)(field ? g_brEdit : g_brBg);
      }
      break;
    case WM_SETTINGCHANGE:
      if (l && lstrcmpW((LPCWSTR)l, L"ImmersiveColorSet") == 0 && SystemWantsDark() != g_dark) ApplyTheme();   // l'utilisateur change de mode pendant que l'application tourne
      break;
    case WM_COMMAND: {
      int id = LOWORD(w), code = HIWORD(w);
      switch (id) {
        case ID_LANG:
          if (code == CBN_SELCHANGE) { g_en = GetSel(ID_LANG) == 1; ApplyLanguage(); SaveLanguageOnly(); }
          break;
        case ID_MODE:
          if (code == CBN_SELCHANGE) OnModeChanged();
          break;
        case ID_CAMTYPE:
          if (code == CBN_SELCHANGE) OnCamTypeChanged();
          break;
        case ID_CAM: case ID_CAMR:
          if (code == CBN_SELCHANGE && !g_loading) RebuildModes(true);
          break;
        case ID_CAM_REFRESH:
          if (code == BN_CLICKED) RefreshCameras();
          break;
        case ID_W: case ID_H: case ID_FPS:
          if (code == EN_CHANGE) OnSizeEdited();
          break;
        case ID_EYE:
          if (code == BN_CLICKED) UpdateEyeEnable();
          break;
        case ID_PORT_REFRESH:
          g_ports = ListComPorts();
          g_loading = true;
          FillPorts();
          g_loading = false;
          break;
        case ID_CALBROWSE: BrowseCalibDir(); break;
        case ID_SAVE:
          if (SaveFields()) {
            SetWindowTextW(H(ID_STATUS), Tr(L"Enregistré", L"Saved"));
            g_msgUntil = GetTickCount() + 2500;
            if (g_msgUntil == 0) g_msgUntil = 1;
          }
          break;
        case ID_START:
          if (g_running) StopProbe(); else StartProbe();
          UpdateState();
          break;
        case ID_OPENDIR: ShellExecuteW(h, L"open", ExeDir().c_str(), nullptr, nullptr, SW_SHOWNORMAL); break;
        case ID_A_RESTART: SendCmd('T'); break;
        case ID_A_FLOOR: SendCmd('V'); break;
        case ID_A_YAW: SendCmd('Z'); break;
        case ID_A_SWAP: SendCmd('x'); break;
        case ID_A_CHECK: SendCmd('c'); break;
        case ID_M_STEP1: SendCmd('g'); break;
        case ID_M_STEP2: SendCmd('f'); break;
        case ID_M_RESET: SendCmd('0'); break;
      }
      return 0;
    }
    case WM_TIMER:
      RefreshLog();
      UpdateState();
      return 0;
    case WM_CLOSE:
      if (g_running && !IsChecked(ID_KEEP) && g_ourProc) SendCmd('Q');   // arrêt propre du programme que cette fenêtre a lancé
      DestroyWindow(h);
      return 0;
    case WM_DESTROY:
      KillTimer(h, 1);
      PostQuitMessage(0);
      return 0;
  }
  return DefWindowProcW(h, m, w, l);
}

}  // namespace

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, PWSTR, int show) {
  SetProcessDPIAware();
  OleInitialize(nullptr);   // boîte de dialogue de dossier (appartement monothread)
  INITCOMMONCONTROLSEX icc;
  icc.dwSize = sizeof(icc);
  icc.dwICC = ICC_STANDARD_CLASSES;
  InitCommonControlsEx(&icc);

  HDC dc = GetDC(nullptr);
  g_dpi = GetDeviceCaps(dc, LOGPIXELSY);
  ReleaseDC(nullptr, dc);

  NONCLIENTMETRICSW ncm;
  memset(&ncm, 0, sizeof(ncm));
  ncm.cbSize = sizeof(ncm);
  SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0);
  g_font = CreateFontIndirectW(&ncm.lfMessageFont);

  LoadCfg();
  std::string lang = CfgStr("app_lang", "");
  if (lang.empty()) g_en = (PRIMARYLANGID(GetUserDefaultUILanguage()) != LANG_FRENCH);
  else g_en = (lang == "en");

  WNDCLASSW wc;
  memset(&wc, 0, sizeof(wc));
  wc.lpfnWndProc = WndProc;
  wc.hInstance = inst;
  wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
  wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
  wc.lpszClassName = L"AR6DoFApp";
  wc.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
  RegisterClassW(&wc);

  const DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
  RECT r = {0, 0, S(860), S(660)};
  AdjustWindowRect(&r, style, FALSE);
  g_wnd = CreateWindowExW(0, wc.lpszClassName, Tr(L"AR6DoF — Centre de contrôle", L"AR6DoF — Control center"), style, CW_USEDEFAULT, CW_USEDEFAULT,
                          r.right - r.left, r.bottom - r.top, nullptr, nullptr, inst, nullptr);
  if (!g_wnd) return 1;

  CreateControls();
  ApplyTheme();
  // liste de langue
  AddItem(ID_LANG, L"Français");
  AddItem(ID_LANG, L"English");
  SetSel(ID_LANG, g_en ? 1 : 0);

  ScanDevices();
  g_loading = true;
  FillCamTypes();
  FillBackends(ID_BACKEND);
  FillBackends(ID_EYEBACK);
  FillCams(ID_CAM, false);
  FillCams(ID_CAMR, false);
  FillCams(ID_EYEL, true);
  FillCams(ID_EYER, true);
  FillPorts();
  g_loading = false;
  ConfigToFields();
  SendMessageW(H(ID_THREADS), EM_SETLIMITTEXT, 2, 0);
  UpdateState();
  RefreshLog();
  SetTimer(g_wnd, 1, 700, nullptr);

  ShowWindow(g_wnd, show);
  UpdateWindow(g_wnd);

  MSG msg;
  while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
    if (!IsDialogMessageW(g_wnd, &msg)) {
      TranslateMessage(&msg);
      DispatchMessageW(&msg);
    }
  }
  if (g_ourProc) CloseHandle(g_ourProc);
  OleUninitialize();
  return 0;
}
