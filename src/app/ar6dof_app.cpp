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

#include "calib_view.h"
#include "devices.h"
#include "frame_share_core.h"

#pragma comment(linker, "\"/manifestdependency:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")

namespace {

// ------------------------------------------------------------------ état global
bool g_en = false;                 // langue : false = français, true = English
HWND g_wnd = nullptr;
HFONT g_font = nullptr;
HFONT g_fontTitle = nullptr;       // titre d'une carte
HFONT g_fontBig = nullptr;         // titre de la page
HFONT g_fontIcon = nullptr;        // icônes de la navigation (Segoe Fluent Icons / Segoe MDL2 Assets)
int g_page = 0;                    // page affichée
int g_buildPage = -1;              // page dont on crée les contrôles (-1 = visibles sur toutes les pages)
struct Card { int page, x, y, w, h; const wchar_t* fr; const wchar_t* en; int steps; };   // steps : étapes de la page Calibration où la carte est visible (0 = toujours)
std::vector<Card> g_cards;                     // cartes arrondies de chaque page (dessinées par WM_PAINT)
std::vector<std::pair<HWND, int>> g_pageCtl;   // contrôle -> page
std::vector<std::pair<HWND, int>> g_stepCtl;   // contrôle de la page Calibration -> étapes où il est visible (masque de bits)
int g_buildSteps = 0;              // masque appliqué aux contrôles et cartes en cours de création (0 = toutes les étapes)
int g_calStep = 0;                 // étape affichée de la page Calibration (calview::Step)
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
std::vector<std::string> g_ports;        // ids « COM5 » (ce qui est enregistre)
std::vector<std::string> g_portLabels;   // noms affiches (Gestionnaire de peripheriques)

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
  ID_LANG = 100, ID_CAMTYPE, ID_CAM, ID_CAM_REFRESH, ID_CAMR_REFRESH, ID_EYEL_REFRESH, ID_EYER_REFRESH, ID_MOUTH_REFRESH, ID_CAMR, ID_CAMHINT, ID_MODE, ID_W, ID_H, ID_FPS, ID_BACKEND, ID_MFDIRECT,
  ID_PORT, ID_PORT_REFRESH, ID_CALDIR, ID_CALBROWSE, ID_BASE, ID_IMUSTAT, ID_MOUNTMSG,
  ID_UDP, ID_SHARE, ID_BLE, ID_AUTOTRACK,
  ID_EYE, ID_EYEL, ID_EYER, ID_EYEFPS, ID_EYEBACK, ID_EYE_CAL, ID_EYE_RECENTER, ID_EYE_CLEAR, ID_EYE_FOV, ID_EYESTAT,
  ID_MOUTH, ID_MOUTHCAM, ID_MOUTHFPS, ID_MOUTHBACK, ID_MC_NEUTRAL, ID_MC_OPEN, ID_MC_WIDE, ID_MC_NARROW, ID_MC_CLEAR, ID_MOUTHSTAT,
  ID_PV_CAM, ID_PV_EYE, ID_PV_MOUTH, ID_PVW_CAM, ID_PVW_EYE, ID_PVW_MOUTH, ID_PVW_IMU, ID_PVW_TRK,
  ID_THREADS, ID_KEEP,
  ID_A_RESTART, ID_A_FLOOR, ID_A_YAW, ID_A_SWAP, ID_A_CHECK,
  ID_M_STEP1, ID_M_STEP2, ID_M_RESET, ID_M_FLIP,
  ID_LOG, ID_STATS, ID_SAVE, ID_START, ID_STATUS,
  ID_CAL_PRESET, ID_CAL_TOTAL, ID_CAL_HINT, ID_CAL_SQ, ID_CAL_EXTXT, ID_CAL_INFO, ID_CZ_AUTO, ID_CZ_VAL, ID_CZ_SET, ID_CZ_RESET, ID_CAL_START, ID_CAL_IMPORT, ID_CAL_STATUS,
  ID_CAL_MSG, ID_CAL_TIPT, ID_CAL_TIPB, ID_CAL_CALC, ID_CAL_SWAP, ID_CAL_CANCEL, ID_CAL_COMP, ID_CAL_RES, ID_CAL_WARN, ID_CAL_RNOTE, ID_CAL_APPLY, ID_CAL_TOVERIFY, ID_CAL_REDO,
  ID_CAL_VTXT, ID_CAL_VDIST, ID_CAL_VMEASURE, ID_CAL_VFIX, ID_CAL_VBACK,
  ID_CAL_GFX_BAR, ID_CAL_GFX_PV, ID_CAL_GFX_TIP, ID_CAL_GFX_GOALS, ID_CAL_GFX_EX, ID_CAL_GFX_PV2, ID_CAL_GFX_VB,
  ID_NAV0 = 300   // 9 pages : ID_NAV0 .. ID_NAV0 + 8
};
const int kNavCount = 9;
enum { PG_CAM = 0, PG_CALIB, PG_SENSORS, PG_UNITY, PG_EYES, PG_MOUTH, PG_TRACK, PG_LOG, PG_ADV };

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
  g_pageCtl.push_back({c, g_buildPage});
  if (g_buildSteps) g_stepCtl.push_back({c, g_buildSteps});
  return c;
}

// Carte arrondie (comme dans les Paramètres de Windows 11) : dessinée par WM_PAINT, avec son titre ; les contrôles se posent dessus.
void CardBox(const wchar_t* fr, const wchar_t* en, int x, int y, int w, int h) { g_cards.push_back({g_buildPage, x, y, w, h, fr, en, g_buildSteps}); }
bool CardShown(const Card& c) { return c.page == g_page && (c.steps == 0 || (c.steps & (1 << g_calStep)) != 0); }
HWND Label(const wchar_t* fr, const wchar_t* en, int x, int y, int w, int h = 20) { return Mk(L"STATIC", fr, en, SS_LEFT | SS_NOPREFIX, x, y, w, h, -1); }
// Case à cocher dessinée par nous (BS_OWNERDRAW) : carré arrondi qui prend la couleur d'accentuation de Windows ; l'état est gardé dans une propriété de la fenêtre.
HWND Check(const wchar_t* fr, const wchar_t* en, int x, int y, int w, int id) {
  HWND c = Mk(L"BUTTON", fr, en, BS_OWNERDRAW | WS_TABSTOP, x, y, w, 24, id);
  SetPropW(c, L"ischk", (HANDLE)(INT_PTR)1);
  SetPropW(c, L"chk", (HANDLE)(INT_PTR)0);
  return c;
}
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
bool IsChecked(int id) { return (INT_PTR)GetPropW(H(id), L"chk") != 0; }
void SetChecked(int id, bool v) {
  SetPropW(H(id), L"chk", (HANDLE)(INT_PTR)(v ? 1 : 0));
  InvalidateRect(H(id), nullptr, FALSE);
}
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

// Les deux modes qui valent la peine d'être signalés (on ne connaît pas la caméra, on le devine d'après la liste) :
// la plus haute résolution (image nette, suivi précis) et la plus haute cadence (passthrough fluide, faible latence, mais exposition courte : beaucoup de lumière).
// -1 si la liste n'a qu'un mode (rien à comparer).
void BestModes(int* byRes, int* byFps) {
  *byRes = *byFps = -1;
  if (g_modeList.size() < 2) return;
  for (size_t i = 0; i < g_modeList.size(); i++) {
    const ModeEntry& m = g_modeList[i];
    if (*byRes < 0 || (long long)m.w * m.h > (long long)g_modeList[*byRes].w * g_modeList[*byRes].h ||
        ((long long)m.w * m.h == (long long)g_modeList[*byRes].w * g_modeList[*byRes].h && m.fps > g_modeList[*byRes].fps)) *byRes = (int)i;
    if (*byFps < 0 || m.fps > g_modeList[*byFps].fps ||
        (m.fps == g_modeList[*byFps].fps && (long long)m.w * m.h > (long long)g_modeList[*byFps].w * g_modeList[*byFps].h)) *byFps = (int)i;
  }
}

void FillModes() {
  ClearCombo(ID_MODE);
  int byRes, byFps;
  BestModes(&byRes, &byFps);
  for (size_t i = 0; i < g_modeList.size(); i++) {
    const ModeEntry& m = g_modeList[i];
    std::wstring t = std::to_wstring(m.w) + L" × " + std::to_wstring(m.h) + Tr(L" à ", L" at ") + std::to_wstring(m.fps) + Tr(L" i/s", L" fps");
    if ((int)i == byRes && (int)i == byFps) t += Tr(L"   ← recommandé", L"   ← recommended");
    else if ((int)i == byRes) t += Tr(L"   ← recommandé : suivi précis", L"   ← recommended: accurate tracking");
    else if ((int)i == byFps) t += Tr(L"   ← recommandé : passthrough fluide", L"   ← recommended: smooth passthrough");
    AddItem(ID_MODE, t);
  }
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
  for (size_t i = 0; i < g_ports.size(); i++) AddItem(ID_PORT, A2W(i < g_portLabels.size() ? g_portLabels[i] : g_ports[i], CP_UTF8));
  int idx = 0;
  for (size_t i = 0; i < g_ports.size(); i++)
    if (A2W(g_ports[i]) == cur) idx = (int)i + 1;
  SetSel(ID_PORT, idx);
}

void ScanPorts() {
  g_ports.clear();
  g_portLabels.clear();
  for (const auto& p : ListComPortInfo()) { g_ports.push_back(p.id); g_portLabels.push_back(p.label); }
}

void ScanDevices() {
  // Media Foundation veut son propre thread : la fenêtre, elle, est en appartement monothread (boîtes de dialogue)
  std::vector<std::string> cams;
  std::thread t([&cams] { cams = ListCameras(); });
  t.join();
  g_cams = cams;
  ScanPorts();
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
  if (g_modeState == 0 && !g_modeNonMjpg && known && f >= 100)
    t += std::wstring(L"  ") + Tr(L"Cadence élevée : prévoir beaucoup de lumière (sinon le suivi décroche).",
                              L"High frame rate: needs plenty of light (otherwise tracking drops).");
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
  int sl = GetSel(ID_CAM), sr = GetSel(ID_CAMR), el = GetSel(ID_EYEL), er = GetSel(ID_EYER), sm = GetSel(ID_MOUTHCAM);
  FillCams(ID_CAM, false);
  FillCams(ID_CAMR, false);
  FillCams(ID_EYEL, true);
  FillCams(ID_EYER, true);
  FillCams(ID_MOUTHCAM, true);
  FillPorts();
  int n = (int)g_cams.size();
  SetSel(ID_CAM, std::min(std::max(sl, 0), std::max(0, n - 1)));
  SetSel(ID_CAMR, std::min(std::max(sr, 0), std::max(0, n - 1)));
  SetSel(ID_EYEL, std::min(std::max(el, 0), n));
  SetSel(ID_EYER, std::min(std::max(er, 0), n));
  SetSel(ID_MOUTHCAM, std::min(std::max(sm, 0), n));
  g_loading = prevLoading;
  RebuildModes(false);
}

// ------------------------------------------------------------------ configuration <-> champs
void UpdateEyeEnable() {
  bool on = IsChecked(ID_EYE);
  for (int id : {ID_EYEL, ID_EYER, ID_EYEFPS, ID_EYEBACK}) EnableWindow(H(id), on);
}

void UpdateMouthEnable() {
  bool on = IsChecked(ID_MOUTH);
  for (int id : {ID_MOUTHCAM, ID_MOUTHFPS, ID_MOUTHBACK}) EnableWindow(H(id), on);
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
  SetChecked(ID_MFDIRECT, CfgInt("mfdirect", 1) != 0);   // lecteur direct par défaut : repli automatique sur OpenCV si la caméra ne le supporte pas

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
  SetText(ID_EYE_FOV, std::to_wstring(CfgInt("app_calfov", 60)));

  SetChecked(ID_MOUTH, CfgInt("mouth", 0) != 0);
  SetSel(ID_MOUTHCAM, std::min(std::max(CfgInt("mouthcam", -1) + 1, 0), (int)g_cams.size()));
  SetText(ID_MOUTHFPS, std::to_wstring(CfgInt("mouthfps", 60)));
  SetSel(ID_MOUTHBACK, CfgInt("mouthbackend", 0) == 1 ? 1 : 0);
  UpdateMouthEnable();

  SetText(ID_THREADS, std::to_wstring(CfgInt("app_cvthreads", 2)));
  SetChecked(ID_KEEP, CfgInt("app_keep", 0) != 0);
  {   // page Calibration
    int np = 0;
    calview::Presets(&np);
    SetSel(ID_CAL_PRESET, std::min(std::max(CfgInt("cal_preset", 0), 0), np - 1));
    SetText(ID_CAL_TOTAL, A2W(CfgStr("cal_total", "250")));
    SetText(ID_CAL_HINT, A2W(CfgStr("cal_hint", "0")));
    SetText(ID_CAL_VDIST, A2W(CfgStr("cal_verify", "500")));
  }
  g_loading = false;
}

bool g_quietSave = false;   // enregistrement automatique (démarrage du mode services) : pas de boîte de dialogue

bool ReadInt(int id, int lo, int hi, const wchar_t* frName, const wchar_t* enName, int* out) {
  std::wstring t = GetText(id);
  wchar_t* end = nullptr;
  long v = wcstol(t.c_str(), &end, 10);
  if (t.empty() || (end && *end != L'\0') || v < lo || v > hi) {
    std::wstring msg = std::wstring(Tr(frName, enName)) + Tr(L" : entre ", L": enter ") + std::to_wstring(lo) + Tr(L" et ", L" and ") + std::to_wstring(hi) + L".";
    if (!g_quietSave) {
      MessageBoxW(g_wnd, msg.c_str(), Tr(L"Valeur invalide", L"Invalid value"), MB_OK | MB_ICONWARNING);
      SetFocus(H(id));
    }
    return false;
  }
  *out = (int)v;
  return true;
}

bool ParseMm(int id, double lo, double hi, double* out) {   // millimètres saisis (page Calibration) (virgule ou point)
  std::wstring t = GetText(id);
  for (auto& ch : t) if (ch == L',') ch = L'.';
  wchar_t* end = nullptr;
  const double v = wcstod(t.c_str(), &end);
  if (t.empty() || (end && *end != L'\0') || !(v >= lo && v <= hi)) return false;
  *out = v;
  return true;
}

// Lit les champs et écrit ar6dof_config.txt (les clés inconnues sont conservées).
bool SaveFields() {
  int w, h, f, udp, efps, mfps, fov, thr;
  if (!ReadInt(ID_W, 320, 8000, L"Largeur", L"Width", &w)) return false;
  if (!ReadInt(ID_H, 240, 4000, L"Hauteur", L"Height", &h)) return false;
  if (!ReadInt(ID_FPS, 5, 240, L"Cadence", L"Frame rate", &f)) return false;
  if (!ReadInt(ID_UDP, 0, 65535, L"Port UDP", L"UDP port", &udp)) return false;
  if (!ReadInt(ID_EYEFPS, 10, 240, L"Cadence des yeux", L"Eye frame rate", &efps)) return false;
  if (!ReadInt(ID_MOUTHFPS, 10, 120, L"Cadence de la bouche", L"Mouth frame rate", &mfps)) return false;
  if (!ReadInt(ID_EYE_FOV, 10, 150, L"Champ de vision de l'écran", L"Screen field of view", &fov)) return false;
  if (!ReadInt(ID_THREADS, 1, 16, L"Threads de calcul", L"Compute threads", &thr)) return false;

  bool two = IsTwoCams();
  if (two && GetSel(ID_CAM) == GetSel(ID_CAMR)) {
    if (!g_quietSave) {
      MessageBoxW(g_wnd, Tr(L"Choisis deux caméras différentes (gauche et droite).", L"Pick two different cameras (left and right)."),
                  Tr(L"Caméra", L"Camera"), MB_OK | MB_ICONWARNING);
      SetFocus(H(ID_CAMR));
    }
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
  g_cfg.erase("windows");   // ancienne option : les fenêtres OpenCV n'existent plus
  g_cfg["mouth"] = IsChecked(ID_MOUTH) ? "1" : "0";
  g_cfg["mouthcam"] = std::to_string(GetSel(ID_MOUTHCAM) - 1);
  g_cfg["mouthfps"] = std::to_string(mfps);
  g_cfg["mouthbackend"] = std::to_string(GetSel(ID_MOUTHBACK) == 1 ? 1 : 0);
  g_cfg["app_calfov"] = std::to_string(fov);
  g_cfg["app_ble"] = IsChecked(ID_BLE) ? "1" : "0";
  g_cfg["app_cvthreads"] = std::to_string(thr);
  g_cfg["app_keep"] = IsChecked(ID_KEEP) ? "1" : "0";
  {   // page Calibration : grille, largeur mesurée, écart des objectifs, distance de la règle (valeurs invalides ignorées : la page ne bloque jamais l'enregistrement)
    double v = 0;
    g_cfg["cal_preset"] = std::to_string(std::max(0, GetSel(ID_CAL_PRESET)));
    if (ParseMm(ID_CAL_TOTAL, 20, 2000, &v)) g_cfg["cal_total"] = W2A(calview::Num(v, 1));
    if (ParseMm(ID_CAL_HINT, 0, 400, &v)) g_cfg["cal_hint"] = W2A(calview::Num(v, 1));
    if (ParseMm(ID_CAL_VDIST, 100, 3000, &v)) g_cfg["cal_verify"] = W2A(calview::Num(v, 0));
  }
  g_cfg["app_lang"] = g_en ? "en" : "fr";
  if (!SaveCfg()) {
    if (!g_quietSave)
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

// ------------------------------------------------------------------ apparence Windows 11 : thème sombre / clair + couleur d'accentuation du système
bool g_dark = false;
struct Pal { COLORREF bg, card, border, text, dim, field, sel; };
Pal g_pal = {RGB(243, 243, 243), RGB(255, 255, 255), RGB(229, 229, 229), RGB(26, 26, 26), RGB(96, 96, 96), RGB(255, 255, 255), RGB(233, 233, 233)};
COLORREF g_accent = RGB(0, 120, 212);
HBRUSH g_brBg = nullptr, g_brCard = nullptr, g_brField = nullptr;

bool SystemWantsDark() {
  HIGHCONTRASTW hc;
  memset(&hc, 0, sizeof(hc));
  hc.cbSize = sizeof(hc);
  if (SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(hc), &hc, 0) && (hc.dwFlags & HCF_HIGHCONTRASTON)) return false;   // contraste élevé : on ne dessine pas nos couleurs
  DWORD v = 1, sz = sizeof(v);
  if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD,
                   nullptr, &v, &sz) != ERROR_SUCCESS)
    return false;
  return v == 0;
}

typedef HRESULT(WINAPI* SetWindowThemeFn)(HWND, LPCWSTR, LPCWSTR);
typedef HRESULT(WINAPI* DwmSetWindowAttributeFn)(HWND, DWORD, LPCVOID, DWORD);
typedef HRESULT(WINAPI* DwmGetColorizationColorFn)(DWORD*, BOOL*);

// uxtheme.dll / dwmapi.dll sont chargées à la demande : l'application reste lancée sur n'importe quelle version de Windows.
void SetCtlTheme(HWND c, LPCWSTR app, LPCWSTR list) {
  static SetWindowThemeFn fn = [] {
    HMODULE m = LoadLibraryW(L"uxtheme.dll");
    return m ? (SetWindowThemeFn)GetProcAddress(m, "SetWindowTheme") : (SetWindowThemeFn) nullptr;
  }();
  if (fn) fn(c, app, list);
}

COLORREF SystemAccent() {
  static DwmGetColorizationColorFn fn = [] {
    HMODULE m = LoadLibraryW(L"dwmapi.dll");
    return m ? (DwmGetColorizationColorFn)GetProcAddress(m, "DwmGetColorizationColor") : (DwmGetColorizationColorFn) nullptr;
  }();
  DWORD argb = 0;
  BOOL opaque = FALSE;
  if (fn && SUCCEEDED(fn(&argb, &opaque))) return RGB((argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
  return RGB(0, 120, 212);
}

double Luma(COLORREF c) { return (0.299 * GetRValue(c) + 0.587 * GetGValue(c) + 0.114 * GetBValue(c)) / 255.0; }

BOOL CALLBACK ThemeChild(HWND c, LPARAM) {
  wchar_t cls[32] = L"";
  GetClassNameW(c, cls, 32);
  if (_wcsicmp(cls, L"Button") == 0) {
    DWORD type = (DWORD)GetWindowLongPtrW(c, GWL_STYLE) & 0xF;
    if (type == BS_OWNERDRAW) return TRUE;   // dessiné par nous
    if (!g_dark) SetCtlTheme(c, nullptr, nullptr);
    else SetCtlTheme(c, L"DarkMode_Explorer", nullptr);
  } else if (_wcsicmp(cls, L"ComboBox") == 0 || _wcsicmp(cls, L"Edit") == 0) {
    if (g_dark) SetCtlTheme(c, L"DarkMode_CFD", nullptr);
    else SetCtlTheme(c, nullptr, nullptr);
  }
  return TRUE;
}

void ApplyTheme() {
  g_dark = SystemWantsDark();
  g_accent = SystemAccent();
  if (g_dark) g_pal = {RGB(32, 32, 32), RGB(43, 43, 43), RGB(58, 58, 58), RGB(255, 255, 255), RGB(172, 172, 172), RGB(51, 51, 51), RGB(55, 55, 55)};
  else g_pal = {RGB(243, 243, 243), RGB(255, 255, 255), RGB(229, 229, 229), RGB(26, 26, 26), RGB(96, 96, 96), RGB(255, 255, 255), RGB(233, 233, 233)};
  HBRUSH ob = g_brBg, oc = g_brCard, of = g_brField;
  g_brBg = CreateSolidBrush(g_pal.bg);
  g_brCard = CreateSolidBrush(g_pal.card);
  g_brField = CreateSolidBrush(g_pal.field);
  if (ob) DeleteObject(ob);
  if (oc) DeleteObject(oc);
  if (of) DeleteObject(of);
  static DwmSetWindowAttributeFn dwm = [] {
    HMODULE m = LoadLibraryW(L"dwmapi.dll");
    return m ? (DwmSetWindowAttributeFn)GetProcAddress(m, "DwmSetWindowAttribute") : (DwmSetWindowAttributeFn) nullptr;
  }();
  if (dwm) {
    BOOL on = g_dark ? TRUE : FALSE;
    if (FAILED(dwm(g_wnd, 20, &on, sizeof(on)))) dwm(g_wnd, 19, &on, sizeof(on));   // barre de titre sombre (20 : Windows 10 20H1+ et 11 ; 19 : versions précédentes)
    COLORREF cap = g_pal.bg, txt = g_pal.text;
    dwm(g_wnd, 35, &cap, sizeof(cap));   // Windows 11 : barre de titre de la couleur du fond (ignoré avant)
    dwm(g_wnd, 36, &txt, sizeof(txt));
  }
  EnumChildWindows(g_wnd, ThemeChild, 0);
  RedrawWindow(g_wnd, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN);
}

RECT CardRect(const Card& c) { RECT r = {S(c.x), S(c.y), S(c.x + c.w), S(c.y + c.h)}; return r; }

// Le contrôle est-il posé sur une carte de la page affichée ? (son fond est alors celui de la carte)
bool OnCard(HWND ctl) {
  RECT r;
  GetWindowRect(ctl, &r);
  POINT p = {(r.left + r.right) / 2, (r.top + r.bottom) / 2};
  ScreenToClient(g_wnd, &p);
  for (const auto& c : g_cards) {
    RECT cr = CardRect(c);
    if (CardShown(c) && PtInRect(&cr, p)) return true;
  }
  return false;
}

void FillRound(HDC dc, RECT r, int rad, COLORREF fill, COLORREF line, bool outline) {
  HBRUSH b = CreateSolidBrush(fill);
  HPEN p = outline ? CreatePen(PS_SOLID, 1, line) : (HPEN)GetStockObject(NULL_PEN);
  HGDIOBJ ob = SelectObject(dc, b), op = SelectObject(dc, p);
  if (!outline) { r.right++; r.bottom++; }
  RoundRect(dc, r.left, r.top, r.right, r.bottom, rad * 2, rad * 2);
  SelectObject(dc, ob);
  SelectObject(dc, op);
  DeleteObject(b);
  if (outline) DeleteObject(p);
}

const wchar_t* kNavFr[kNavCount] = {L"Caméra", L"Calibration", L"Capteurs", L"Unity", L"Yeux", L"Bouche", L"Suivi", L"Journal", L"Avancé"};
const wchar_t* kNavEn[kNavCount] = {L"Camera", L"Calibration", L"Sensors", L"Unity", L"Eyes", L"Mouth", L"Tracking", L"Log", L"Advanced"};
const wchar_t* kNavIcon[kNavCount] = {L"\xE722", L"\xE80A", L"\xE9D9", L"\xE71B", L"\xE7B3", L"\xE76E", L"\xE768", L"\xE8A5", L"\xE713"};   // Segoe Fluent Icons : appareil photo, capteur, lien, œil, lecture, réglages

// Fond de la fenêtre : titre de l'application, titre de la page et cartes de la page affichée.
void PaintChrome(HDC dc) {
  SetBkMode(dc, TRANSPARENT);
  SelectObject(dc, g_fontBig);
  SetTextColor(dc, g_pal.text);
  RECT r = {S(24), S(18), S(190), S(52)};
  DrawTextW(dc, L"AR6DoF", -1, &r, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX);
  r = {S(224), S(14), S(916), S(52)};
  DrawTextW(dc, Tr(kNavFr[g_page], kNavEn[g_page]), -1, &r, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX);
  for (const auto& c : g_cards) {
    if (!CardShown(c)) continue;
    FillRound(dc, CardRect(c), S(8), g_pal.card, g_pal.border, true);
    SelectObject(dc, g_fontTitle);
    SetTextColor(dc, g_pal.text);
    RECT tr = {S(c.x + 20), S(c.y + 10), S(c.x + c.w - 20), S(c.y + 38)};
    DrawTextW(dc, Tr(c.fr, c.en), -1, &tr, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX);
  }
}

// Dessin des contrôles BS_OWNERDRAW : navigation, cases à cocher, bouton principal.
void DrawOwner(const DRAWITEMSTRUCT* d) {
  HDC dc = d->hDC;
  RECT r = d->rcItem;
  int id = (int)d->CtlID;
  bool disabled = (d->itemState & ODS_DISABLED) != 0;
  bool pressed = (d->itemState & ODS_SELECTED) != 0;
  wchar_t text[128] = L"";
  GetWindowTextW(d->hwndItem, text, 128);
  SetBkMode(dc, TRANSPARENT);
  if (id >= ID_NAV0 && id < ID_NAV0 + kNavCount) {
    FillRect(dc, &r, g_brBg);
    bool sel = (id - ID_NAV0) == g_page;
    RECT pill = {r.left + S(4), r.top + S(2), r.right - S(4), r.bottom - S(2)};
    if (sel || pressed) FillRound(dc, pill, S(5), g_pal.sel, g_pal.sel, false);
    if (sel) {
      HBRUSH ab = CreateSolidBrush(g_accent);
      RECT bar = {pill.left + S(1), (r.top + r.bottom) / 2 - S(9), pill.left + S(4), (r.top + r.bottom) / 2 + S(9)};
      FillRect(dc, &bar, ab);
      DeleteObject(ab);
    }
    SetTextColor(dc, g_pal.text);
    if (g_fontIcon) {
      SelectObject(dc, g_fontIcon);
      RECT ir = {pill.left + S(12), r.top, pill.left + S(40), r.bottom};
      DrawTextW(dc, kNavIcon[id - ID_NAV0], 1, &ir, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
    }
    SelectObject(dc, g_font);
    RECT tr = {pill.left + S(48), r.top, pill.right, r.bottom};
    DrawTextW(dc, text, -1, &tr, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX);
    return;
  }
  if (id == ID_START) {
    FillRect(dc, &r, OnCard(d->hwndItem) ? g_brCard : g_brBg);   // le bouton est dans une carte (page Suivi)
    COLORREF fill = disabled ? g_pal.border : g_accent;
    if (pressed) fill = RGB(GetRValue(fill) * 8 / 10, GetGValue(fill) * 8 / 10, GetBValue(fill) * 8 / 10);
    FillRound(dc, r, S(5), fill, fill, false);
    SetTextColor(dc, disabled ? g_pal.dim : (Luma(fill) > 0.6 ? RGB(0, 0, 0) : RGB(255, 255, 255)));
    SelectObject(dc, g_font);
    DrawTextW(dc, text, -1, &r, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
    return;
  }
  // case à cocher
  bool onCard = OnCard(d->hwndItem);
  FillRect(dc, &r, onCard ? g_brCard : g_brBg);
  int box = S(18);
  int cy = (r.top + r.bottom) / 2;
  RECT br = {r.left + S(2), cy - box / 2, r.left + S(2) + box, cy - box / 2 + box};
  bool on = IsChecked(id);
  if (on) {
    COLORREF f = disabled ? g_pal.border : g_accent;
    FillRound(dc, br, S(4), f, f, false);
    HPEN pen = CreatePen(PS_SOLID, std::max(2, S(2)), Luma(f) > 0.6 ? RGB(0, 0, 0) : RGB(255, 255, 255));
    HGDIOBJ op = SelectObject(dc, pen);
    MoveToEx(dc, br.left + box * 22 / 100, br.top + box * 52 / 100, nullptr);
    LineTo(dc, br.left + box * 43 / 100, br.top + box * 72 / 100);
    LineTo(dc, br.left + box * 78 / 100, br.top + box * 30 / 100);
    SelectObject(dc, op);
    DeleteObject(pen);
  } else {
    FillRound(dc, br, S(4), onCard ? g_pal.card : g_pal.bg, disabled ? g_pal.border : g_pal.dim, true);
  }
  SetTextColor(dc, disabled ? g_pal.dim : g_pal.text);
  SelectObject(dc, g_font);
  RECT tr = {br.right + S(10), r.top, r.right, r.bottom};
  DrawTextW(dc, text, -1, &tr, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX);
  if (d->itemState & ODS_FOCUS) {
    RECT fr = {r.left, r.top + S(1), r.right, r.bottom - S(1)};
    DrawFocusRect(dc, &fr);
  }
}

bool FontExists(const wchar_t* face) {
  HDC dc = GetDC(nullptr);
  LOGFONTW lf;
  memset(&lf, 0, sizeof(lf));
  lstrcpynW(lf.lfFaceName, face, 32);
  lf.lfCharSet = DEFAULT_CHARSET;
  bool found = false;
  EnumFontFamiliesExW(dc, &lf, [](const LOGFONTW*, const TEXTMETRICW*, DWORD, LPARAM p) -> int { *(bool*)p = true; return 0; }, (LPARAM)&found, 0);
  ReleaseDC(nullptr, dc);
  return found;
}

void CalApplyStep();   // page Calibration : montre les contrôles de l'étape en cours (défini plus bas)

void ShowPage(int p) {
  g_page = std::min(std::max(p, 0), kNavCount - 1);
  for (const auto& pc : g_pageCtl) ShowWindow(pc.first, (pc.second < 0 || pc.second == g_page) ? SW_SHOW : SW_HIDE);
  if (g_page == PG_CALIB) CalApplyStep();
  RedrawWindow(g_wnd, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
}

void OnPageChanged();   // défini plus bas (aperçus, mode services)

// ------------------------------------------------------------------ ar6dof_probe : deux modes
//  - « suivi » : lancé par le bouton Démarrer (ou par Unity) ; tout tourne (caméra stéréo, tracking 6DoF, passthrough, Bluetooth...)
//  - « services » : lancé tout seul quand on a besoin d'un aperçu ou d'une calibration SANS suivi (IMU, yeux, bouche) ;
//    pas de tracking, caméras ouvertes seulement pendant un aperçu ; s'arrête après 20 s d'inactivité.
struct ProbeProcs { bool full = false, svc = false; };
ProbeProcs g_procs;
DWORD g_svcPid = 0;
HANDLE g_svcProc = nullptr;
DWORD g_svcReadyAt = 0, g_svcIdleSince = 0, g_svcLastStart = 0, g_svcStopDeadline = 0;
bool g_startAfterSvc = false;                  // « Démarrer le suivi » demandé pendant que le mode services tourne : on attend sa fin
bool g_pvOn[3] = {false, false, false};        // aperçus demandés par l'utilisateur : caméra stéréo, yeux, bouche
std::string g_lastImu, g_lastMount, g_lastEye, g_lastMouth;   // derniers messages du journal (affichés dans leur page)

// Étalonnage des yeux sur l'écran (fenêtre plein écran)
struct CalState {
  bool active = false, begun = false, solved = false;
  HWND wnd = nullptr;
  DWORD start = 0, endAt = 0;   // start : instant du premier point
  int pointSent = -1;
  double fov = 60;
};
CalState g_cal;

// Page Calibration de la caméra : tout le calcul est fait par ar6dof_probe (calib_session.h) ; la page commande par UDP et lit les deux fichiers d'état.
struct CalPage {
  calview::Status st;          // calib_status.json
  calview::Info info;          // calib_info.json
  FILETIME stStamp = {0, 0}, infoStamp = {0, 0};
  int step = 0;                // calview::Step affiché
  bool wantVerify = false;
  bool starting = false;       // « Commencer » demandé : CALSTART est renvoyé tant que la sonde n'a pas répondu
  DWORD startReq = 0, lastStartSend = 0, lastPoll = 0, flashUntil = 0, lastCancel = 0, verifySentAt = 0;
  int lastAccept = -1, verifySentSeq = -1;
  std::wstring note;           // message sous le bouton « Commencer »
  bool uiDirty = true, lastRun = false;
};
CalPage g_cp;

void ScanProbes() {
  bool full = false, svc = false;
  HANDLE s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (s != INVALID_HANDLE_VALUE) {
    PROCESSENTRY32W e;
    e.dwSize = sizeof(e);
    if (Process32FirstW(s, &e)) {
      do {
        if (_wcsicmp(e.szExeFile, L"ar6dof_probe.exe") == 0) {
          if (g_svcPid && e.th32ProcessID == g_svcPid) svc = true;
          else full = true;
        }
      } while (Process32NextW(s, &e));
    }
    CloseHandle(s);
  }
  g_procs.full = full;
  g_procs.svc = svc;
  if (!svc && g_svcPid) {
    g_svcPid = 0;
    if (g_svcProc) { CloseHandle(g_svcProc); g_svcProc = nullptr; }
  }
}

void KillPid(DWORD pid) {
  HANDLE p = OpenProcess(PROCESS_TERMINATE, FALSE, pid);
  if (p) { TerminateProcess(p, 0); CloseHandle(p); }
}

void KillProbe() {
  HANDLE s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (s == INVALID_HANDLE_VALUE) return;
  PROCESSENTRY32W e;
  e.dwSize = sizeof(e);
  if (Process32FirstW(s, &e)) {
    do { if (_wcsicmp(e.szExeFile, L"ar6dof_probe.exe") == 0) KillPid(e.th32ProcessID); } while (Process32NextW(s, &e));
  }
  CloseHandle(s);
}

// Un datagramme UDP vers 127.0.0.1:port (9877 = ar6dof_probe, 9884 = eye tracking, 9887 = mouth tracking).
void SendUdp(int port, const std::string& text) {
  static bool init = false;
  if (!init) { WSADATA w; WSAStartup(MAKEWORD(2, 2), &w); init = true; }
  SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
  if (s == INVALID_SOCKET) return;
  sockaddr_in a;
  memset(&a, 0, sizeof(a));
  a.sin_family = AF_INET;
  a.sin_port = htons((u_short)port);
  inet_pton(AF_INET, "127.0.0.1", &a.sin_addr);
  sendto(s, text.data(), (int)text.size(), 0, reinterpret_cast<sockaddr*>(&a), sizeof(a));
  closesocket(s);
}

void SendCmd(char c) { SendUdp(9877, std::string(1, c)); }

// Commandes en attente : le mode services met ~2 s à ouvrir ses ports, on envoie quand il est prêt.
struct Pending { DWORD at; int port; std::string text; };
std::vector<Pending> g_pending;

void QueueUdp(int port, const std::string& text, DWORD delayMs) {
  if (delayMs == 0) { SendUdp(port, text); return; }
  g_pending.push_back({GetTickCount() + delayMs, port, text});
}

void FlushPending() {
  if (g_pending.empty()) return;
  DWORD now = GetTickCount();
  for (size_t i = 0; i < g_pending.size();) {
    if ((LONG)(now - g_pending[i].at) >= 0) {
      SendUdp(g_pending[i].port, g_pending[i].text);
      g_pending.erase(g_pending.begin() + i);
    } else {
      i++;
    }
  }
}

bool StartServices(bool interactive) {
  DWORD now = GetTickCount();
  if (!interactive && g_svcLastStart && (LONG)(now - g_svcLastStart) < 3000) return false;
  g_svcLastStart = now ? now : 1;
  std::wstring exe = ExeDir() + L"\\ar6dof_probe.exe";
  if (!FileExists(exe)) {
    if (interactive)
      MessageBoxW(g_wnd, Tr(L"ar6dof_probe.exe est introuvable : il doit être dans le même dossier que cette application.",
                            L"ar6dof_probe.exe not found: it must be in the same folder as this application."),
                  Tr(L"Démarrer", L"Start"), MB_OK | MB_ICONERROR);
    return false;
  }
  g_quietSave = !interactive;
  bool ok = SaveFields();   // le mode services lit ar6dof_config.txt : caméras des yeux et de la bouche, port de l'IMU...
  g_quietSave = false;
  if (!ok) return false;
  std::wstring cmd = L"\"" + exe + L"\" --services --auto --no-ble --cv-threads " + std::to_wstring(CfgInt("app_cvthreads", 2)) + L" --parent " +
                     std::to_wstring(GetCurrentProcessId());
  std::vector<wchar_t> buf(cmd.begin(), cmd.end());
  buf.push_back(L'\0');
  STARTUPINFOW si;
  memset(&si, 0, sizeof(si));
  si.cb = sizeof(si);
  PROCESS_INFORMATION pi;
  memset(&pi, 0, sizeof(pi));
  std::wstring dir = ExeDir();
  if (!CreateProcessW(exe.c_str(), buf.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, dir.c_str(), &si, &pi)) return false;
  if (g_svcProc) CloseHandle(g_svcProc);
  g_svcProc = pi.hProcess;
  g_svcPid = pi.dwProcessId;
  CloseHandle(pi.hThread);
  g_procs.svc = true;
  g_svcReadyAt = GetTickCount() + 2200;
  g_svcIdleSince = 0;
  g_lastLogSize = ~0ull;
  g_lastImu.clear(); g_lastMount.clear(); g_lastEye.clear(); g_lastMouth.clear();
  return true;
}

// Un ar6dof_probe est-il là pour répondre ? Sinon on lance le mode services. Retourne le délai (ms) à attendre avant d'envoyer une commande.
DWORD EnsureProbe() {
  ScanProbes();
  if (g_procs.full) return 0;
  if (g_procs.svc) {
    LONG left = (LONG)(g_svcReadyAt - GetTickCount());
    return left > 0 ? (DWORD)left : 0;
  }
  return StartServices(true) ? 2200 : 0;
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
  ScanProbes();
  if (g_procs.full) return;
  std::wstring cmd = L"\"" + exe + L"\" --auto --cv-threads " + std::to_wstring(CfgInt("app_cvthreads", 2));
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
  g_lastMount.clear(); g_lastEye.clear(); g_lastMouth.clear();
}

void StopProbe() {
  SendCmd('Q');
  g_stopDeadline = GetTickCount() + 4000;   // arrêt propre d'abord ; sinon on force
}

// « Démarrer le suivi » : si le mode services tourne (il garde la caméra, le port de l'IMU...), on l'arrête d'abord.
void StartTracking() {
  ScanProbes();
  if (g_procs.svc) {
    SendCmd('Q');
    g_startAfterSvc = true;
    g_svcStopDeadline = GetTickCount() + 3500;
    return;
  }
  StartProbe();
}

// ------------------------------------------------------------------ aperçus des caméras
// ar6dof_probe écrit les images dans la mémoire partagée « Local\ar6dof_preview » (800 x 300) seulement tant qu'on lui envoie « PV n »
// (n = 1 caméra stéréo, 2 yeux, 3 bouche) au moins une fois par seconde. Fermer la page ou l'application suffit à tout arrêter.
const int kPvW = 800, kPvH = 300;
HANDLE g_pvMap = nullptr;
const uint8_t* g_pvBase = nullptr;
std::vector<uint8_t> g_pvBuf;
uint64_t g_pvLastId = 0;
bool g_pvHave = false;
int g_pvSrc = 0, g_pvActive = 0;
DWORD g_pvLastReq = 0;

int WantedSource() {
  if (g_cal.active) return 2;
  if (g_page == PG_CAM && g_pvOn[0]) return 1;
  if (g_page == PG_EYES && g_pvOn[1]) return 2;
  if (g_page == PG_MOUTH && g_pvOn[2]) return 3;
  if (g_page == PG_SENSORS && GetSel(ID_PORT) > 0) return 4;   // graphique de l'IMU : automatique dans la page Capteurs
  if (g_page == PG_TRACK && g_procs.full) return 5;            // vue du suivi : automatique, seulement quand le suivi tourne
  if (g_page == PG_CALIB && g_procs.full && (g_cp.step == calview::kCapture || g_cp.step == calview::kVerify)) return 1;   // calibration : caméra seulement pendant la capture / la règle
  return 0;
}

HWND PvWindow(int src) {
  if (src == 1 && g_page == PG_CALIB) return H(g_cp.step == calview::kVerify ? ID_CAL_GFX_PV2 : ID_CAL_GFX_PV);
  return H(src == 1 ? ID_PVW_CAM : src == 2 ? ID_PVW_EYE : src == 3 ? ID_PVW_MOUTH : src == 5 ? ID_PVW_TRK : ID_PVW_IMU); }

bool OpenPreview() {
  if (g_pvBase) return true;
  g_pvMap = OpenFileMappingA(FILE_MAP_READ, FALSE, "Local\\ar6dof_preview");
  if (!g_pvMap) return false;
  g_pvBase = static_cast<const uint8_t*>(MapViewOfFile(g_pvMap, FILE_MAP_READ, 0, 0, 0));
  uint32_t head[5] = {0, 0, 0, 0, 0};
  if (g_pvBase) { memcpy(&head[0], g_pvBase, 4); memcpy(&head[2], g_pvBase + 8, 12); }
  if (!g_pvBase || head[0] != fshare::kMagic || head[2] != (uint32_t)kPvW || head[3] != (uint32_t)kPvH || head[4] != 4u) {
    if (g_pvBase) UnmapViewOfFile(g_pvBase);
    CloseHandle(g_pvMap);
    g_pvBase = nullptr;
    g_pvMap = nullptr;
    return false;
  }
  g_pvLastId = 0;
  return true;
}

void ClosePreview() {
  if (g_pvBase) UnmapViewOfFile(g_pvBase);
  if (g_pvMap) CloseHandle(g_pvMap);
  g_pvBase = nullptr;
  g_pvMap = nullptr;
}

void PollPreview(int want) {
  if (!OpenPreview()) return;
  g_pvBuf.resize((size_t)kPvW * kPvH * 4);
  uint64_t fid = 0, last = g_pvLastId;
  double tw = 0, tc = 0;
  if (!fshare::Read(g_pvBase, g_pvBuf.data(), g_pvBuf.size(), last, fid, tw, tc)) return;
  g_pvLastId = last;
  LARGE_INTEGER f, n;
  QueryPerformanceFrequency(&f);
  QueryPerformanceCounter(&n);
  if ((double)n.QuadPart / (double)f.QuadPart - tw > 1.5) return;   // image d'une session précédente
  g_pvHave = true;
  g_pvSrc = want;
  InvalidateRect(PvWindow(want), nullptr, FALSE);
}

// Appelée toutes les 66 ms : n'envoie et ne lit rien quand aucun aperçu n'est demandé.
void OnFastTick() {
  FlushPending();
  const int want = WantedSource();
  const DWORD now = GetTickCount();
  if (want != g_pvActive) {
    if (want == 0 && g_pvActive != 0) { SendUdp(9877, "PV 0"); ClosePreview(); }
    g_pvHave = false;
    for (int s = 1; s <= 5; s++) InvalidateRect(PvWindow(s), nullptr, FALSE);
    g_pvActive = want;
    g_pvLastReq = 0;
  }
  if (!want) return;
  if (g_pvLastReq == 0 || now - g_pvLastReq >= 400) {
    g_pvLastReq = now ? now : 1;
    SendUdp(9877, "PV " + std::to_string(want));
  }
  PollPreview(want);
}

LRESULT CALLBACK PvProc(HWND h, UINT m, WPARAM w, LPARAM l) {
  if (m == WM_ERASEBKGND) return 1;
  if (m == WM_PAINT) {
    PAINTSTRUCT ps;
    HDC dc = BeginPaint(h, &ps);
    RECT rc;
    GetClientRect(h, &rc);
    const int src = (int)GetWindowLongPtrW(h, GWLP_USERDATA);
    if (g_pvHave && g_pvSrc == src && WantedSource() == src && g_pvBuf.size() >= (size_t)kPvW * kPvH * 4) {
      BITMAPINFO bi;
      memset(&bi, 0, sizeof(bi));
      bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
      bi.bmiHeader.biWidth = kPvW;
      bi.bmiHeader.biHeight = -kPvH;   // lignes du haut vers le bas
      bi.bmiHeader.biPlanes = 1;
      bi.bmiHeader.biBitCount = 32;
      bi.bmiHeader.biCompression = BI_RGB;
      SetStretchBltMode(dc, HALFTONE);
      SetBrushOrgEx(dc, 0, 0, nullptr);
      StretchDIBits(dc, 0, 0, rc.right, rc.bottom, 0, 0, kPvW, kPvH, g_pvBuf.data(), &bi, DIB_RGB_COLORS, SRCCOPY);
    } else {
      HBRUSH b = CreateSolidBrush(RGB(22, 22, 22));
      FillRect(dc, &rc, b);
      DeleteObject(b);
      SetBkMode(dc, TRANSPARENT);
      SetTextColor(dc, RGB(160, 160, 160));
      SelectObject(dc, g_font);
      const wchar_t* msg = WantedSource() == src ? (src == 4 ? Tr(L"En attente de l'IMU…", L"Waiting for the IMU…") : Tr(L"En attente de la caméra…", L"Waiting for the camera…"))
                                                 : src == 5 ? Tr(L"Démarre le suivi pour voir la trajectoire", L"Start tracking to see the trajectory") : Tr(L"Aperçu désactivé", L"Preview off");
      DrawTextW(dc, msg, -1, &rc, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
    }
    EndPaint(h, &ps);
    return 0;
  }
  return DefWindowProcW(h, m, w, l);
}

void TogglePreview(int idx) {
  g_pvOn[idx] = !g_pvOn[idx];
  if (g_pvOn[idx]) EnsureProbe();
  OnFastTick();
}

// ------------------------------------------------------------------ étalonnage des yeux sur l'écran (9 points, fenêtre plein écran)
const double kPi = 3.14159265358979323846;
const double kCalPts[9][2] = {{0.5, 0.5}, {0.1, 0.1}, {0.9, 0.1}, {0.1, 0.9}, {0.9, 0.9}, {0.5, 0.1}, {0.5, 0.9}, {0.1, 0.5}, {0.9, 0.5}};
const DWORD kCalPrep = 3500, kCalSettle = 900, kCalCollect = 800, kCalPer = 2000;

void EndEyeCal(bool cancel) {
  if (cancel && g_cal.active && !g_cal.solved) SendUdp(9884, "CAL_CANCEL\n");
  g_cal.active = false;
  HWND w = g_cal.wnd;
  g_cal.wnd = nullptr;
  if (w) DestroyWindow(w);
}

LRESULT CALLBACK CalProc(HWND h, UINT m, WPARAM w, LPARAM l) {
  switch (m) {
    case WM_ERASEBKGND:
      return 1;
    case WM_SETCURSOR:
      SetCursor(LoadCursorW(nullptr, IDC_ARROW));
      return TRUE;
    case WM_KEYDOWN:
      if (w == VK_ESCAPE) EndEyeCal(true);
      return 0;
    case WM_RBUTTONDOWN:
      EndEyeCal(true);
      return 0;
    case WM_TIMER: {
      if (!g_cal.active) return 0;
      const DWORD now = GetTickCount();
      const LONG el = (LONG)(now - g_cal.start);
      if (!g_cal.begun && el >= -500) { SendUdp(9884, "CAL_BEGIN\n"); g_cal.begun = true; }
      const int idx = el >= 0 ? (int)(el / (LONG)kCalPer) : -1;
      if (g_cal.begun && idx >= 0 && idx < 9 && idx > g_cal.pointSent) {
        g_cal.pointSent = idx;
        RECT rc;
        GetClientRect(h, &rc);
        const double tx = std::tan(g_cal.fov * kPi / 360.0), ty = tx * (double)rc.bottom / std::max(1L, rc.right);
        const double u = (kCalPts[idx][0] - 0.5) * 2.0, v = (0.5 - kCalPts[idx][1]) * 2.0;   // yaw > 0 : à droite, pitch > 0 : en haut
        char b[96];
        std::snprintf(b, sizeof(b), "CAL_POINT %.3f %.3f %u %u\n", std::atan(u * tx) * 180.0 / kPi, std::atan(v * ty) * 180.0 / kPi, (unsigned)kCalSettle,
                      (unsigned)kCalCollect);
        SendUdp(9884, b);
      }
      if (!g_cal.solved && el >= (LONG)(9 * kCalPer + 300)) {
        SendUdp(9884, "CAL_SOLVE\n");
        g_cal.solved = true;
        g_cal.endAt = now + 1500;
      }
      if (g_cal.solved && (LONG)(now - g_cal.endAt) >= 0) { EndEyeCal(false); return 0; }
      InvalidateRect(h, nullptr, FALSE);
      return 0;
    }
    case WM_PAINT: {
      PAINTSTRUCT ps;
      HDC dc = BeginPaint(h, &ps);
      RECT rc;
      GetClientRect(h, &rc);
      HDC mem = CreateCompatibleDC(dc);
      HBITMAP bmp = CreateCompatibleBitmap(dc, rc.right, rc.bottom);
      HGDIOBJ old = SelectObject(mem, bmp);
      FillRect(mem, &rc, (HBRUSH)GetStockObject(BLACK_BRUSH));
      SetBkMode(mem, TRANSPARENT);
      SelectObject(mem, g_fontTitle);
      const LONG el = (LONG)(GetTickCount() - g_cal.start);
      std::wstring msg;
      if (el < 0) {
        msg = Tr(L"Ouverture des caméras… tiens la tête droite, face à l'écran", L"Opening the cameras… keep your head straight, facing the screen");
      } else if (g_cal.solved) {
        msg = Tr(L"Calcul de l'étalonnage…", L"Computing the calibration…");
      } else {
        const int idx = std::min(8, (int)(el / (LONG)kCalPer));
        const LONG ph = el % (LONG)kCalPer;
        const double px = kCalPts[idx][0] * rc.right, py = kCalPts[idx][1] * rc.bottom;
        const double t = std::min(1.0, (double)ph / (double)(kCalSettle + kCalCollect));
        const int R = S(30), r = (int)(S(22) * (1.0 - t) + S(5) * t);
        HBRUSH none = (HBRUSH)GetStockObject(NULL_BRUSH);
        HPEN ring = CreatePen(PS_SOLID, std::max(2, S(3)), g_accent);
        HGDIOBJ op = SelectObject(mem, ring), ob = SelectObject(mem, none);
        Ellipse(mem, (int)px - R, (int)py - R, (int)px + R, (int)py + R);
        SelectObject(mem, op);
        SelectObject(mem, ob);
        DeleteObject(ring);
        HBRUSH dot = CreateSolidBrush(RGB(255, 255, 255));
        HGDIOBJ ob2 = SelectObject(mem, dot);
        HGDIOBJ op2 = SelectObject(mem, GetStockObject(NULL_PEN));
        Ellipse(mem, (int)px - r, (int)py - r, (int)px + r, (int)py + r);
        SelectObject(mem, ob2);
        SelectObject(mem, op2);
        DeleteObject(dot);
        msg = std::wstring(Tr(L"Regarde le point sans bouger la tête", L"Look at the dot without moving your head")) + L"  (" + std::to_wstring(idx + 1) + L" / 9)";
      }
      SetTextColor(mem, RGB(210, 210, 210));
      RECT tr = {0, rc.bottom - S(90), rc.right, rc.bottom - S(50)};
      DrawTextW(mem, msg.c_str(), -1, &tr, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
      SelectObject(mem, g_font);
      SetTextColor(mem, RGB(130, 130, 130));
      RECT hr = {0, rc.bottom - S(50), rc.right, rc.bottom - S(20)};
      DrawTextW(mem, Tr(L"Échap ou clic droit pour annuler", L"Esc or right-click to cancel"), -1, &hr, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
      BitBlt(dc, 0, 0, rc.right, rc.bottom, mem, 0, 0, SRCCOPY);
      SelectObject(mem, old);
      DeleteObject(bmp);
      DeleteDC(mem);
      EndPaint(h, &ps);
      return 0;
    }
    case WM_DESTROY:
      KillTimer(h, 7);
      if (g_cal.active) { g_cal.active = false; g_cal.wnd = nullptr; }
      return 0;
  }
  return DefWindowProcW(h, m, w, l);
}

void StartEyeCal() {
  if (g_cal.active) return;
  int fov = 60;
  if (!ReadInt(ID_EYE_FOV, 10, 150, L"Champ de vision de l'écran", L"Screen field of view", &fov)) return;
  if (GetSel(ID_EYEL) <= 0 && GetSel(ID_EYER) <= 0) {
    MessageBoxW(g_wnd, Tr(L"Coche « Activer », choisis au moins une caméra d'œil, puis clique sur Enregistrer.",
                          L"Tick “Enable”, pick at least one eye camera, then click Save."),
                Tr(L"Étalonnage des yeux", L"Eye calibration"), MB_OK | MB_ICONINFORMATION);
    return;
  }
  const DWORD delay = EnsureProbe();
  QueueUdp(9884, "EYE 1\n", delay);
  g_cal = CalState();
  g_cal.fov = fov;
  g_cal.active = true;
  g_cal.start = GetTickCount() + delay + kCalPrep;
  MONITORINFO mi;
  memset(&mi, 0, sizeof(mi));
  mi.cbSize = sizeof(mi);
  GetMonitorInfoW(MonitorFromWindow(g_wnd, MONITOR_DEFAULTTONEAREST), &mi);
  g_cal.wnd = CreateWindowExW(WS_EX_TOPMOST, L"AR6Cal", L"", WS_POPUP | WS_VISIBLE, mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left,
                              mi.rcMonitor.bottom - mi.rcMonitor.top, g_wnd, nullptr, GetModuleHandleW(nullptr), nullptr);
  if (!g_cal.wnd) { g_cal.active = false; return; }
  SetTimer(g_cal.wnd, 7, 30, nullptr);
  SetForegroundWindow(g_cal.wnd);
  SetFocus(g_cal.wnd);
}

// Étalonnage de la bouche : une expression à la fois ; la sonde mesure 1,5 s après 1 s de préparation.
void MouthCal(const char* phase) {
  if (GetSel(ID_MOUTHCAM) <= 0) {
    MessageBoxW(g_wnd, Tr(L"Coche « Activer », choisis la caméra de la bouche, puis clique sur Enregistrer.",
                          L"Tick “Enable”, pick the mouth camera, then click Save."),
                Tr(L"Étalonnage de la bouche", L"Mouth calibration"), MB_OK | MB_ICONINFORMATION);
    return;
  }
  g_pvOn[2] = true;
  const DWORD delay = EnsureProbe();
  QueueUdp(9887, std::string("MCAL ") + phase + "\n", delay + 300);
  OnFastTick();
}

// ------------------------------------------------------------------ page Calibration de la caméra (les calculs sont faits par ar6dof_probe ; ici : commandes UDP + lecture des fichiers d'état)
// Étapes : préparation (mire, mode caméra, zoom du mode) -> capture guidée (aperçu, coins détectés, pose « fantôme », barre de progression) -> calcul -> résultat noté
// -> vérification à la règle. Fichiers lus (dans le dossier de l'exe) : calib_status.json (5 fois par seconde au plus) et calib_info.json.
// Commandes (UDP 9877) : CALSTART c r mm base, CALCOMPUTE, CALCANCEL, CALAPPLY, CALVERIFY mm, CALSCALE ratio, CALIMPORT, CALZOOM z, CALZOOMAUTO 0|1, CALZOOMRESET, x (inverser G / D).

std::wstring Dec(double v, int dec) {   // nombre avec la virgule en français
  std::wstring t = calview::Num(v, dec);
  if (!g_en) for (auto& ch : t) if (ch == L'.') ch = L',';
  return t;
}

bool ReadShared(const std::wstring& path, std::string& out, FILETIME* stamp) {
  HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
  if (f == INVALID_HANDLE_VALUE) return false;
  LARGE_INTEGER sz;
  bool ok = GetFileSizeEx(f, &sz) && sz.QuadPart > 0 && sz.QuadPart < (4 << 20);
  if (ok) {
    if (stamp) { FILETIME c, a; GetFileTime(f, &c, &a, stamp); }
    out.assign((size_t)sz.QuadPart, '\0');
    DWORD got = 0;
    ok = ReadFile(f, &out[0], (DWORD)out.size(), &got, nullptr) != 0;
    out.resize(got);
  }
  CloseHandle(f);
  return ok && !out.empty();
}

double CalSquareMm() {
  int n = 0;
  const calview::BoardPreset* p = calview::Presets(&n);
  const int i = std::min(std::max(GetSel(ID_CAL_PRESET), 0), n - 1);
  double total = 0;
  if (!ParseMm(ID_CAL_TOTAL, 20, 2000, &total)) return 0;
  return total / p[i].sx;
}

void CalApplyStep() {
  const int bit = 1 << g_calStep;
  for (const auto& sc : g_stepCtl) ShowWindow(sc.first, (g_page == PG_CALIB && (sc.second & bit)) ? SW_SHOW : SW_HIDE);
  RedrawWindow(g_wnd, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
}

// Réinitialise un texte statique seulement s'il a changé (pas de clignotement).
void CalText(int id, const std::wstring& t) { SetTextIfChanged(H(id), t); }

std::wstring CalInfoText() {
  const calview::Info& i = g_cp.info;
  if (!i.valid) {
    return Tr(L"Aucune calibration lue pour l'instant : soit il n'y en a pas encore (lance la calibration ci-dessous), soit le suivi n'a pas encore tourné.",
              L"No calibration read yet: either there is none (run the calibration below), or tracking has not run yet.");
  }
  std::wstring src = i.source == "ar6dof" ? L"AR6DoF" : i.source == "rtabmap" ? L"RTAB-Map" : A2W(i.source);
  std::wstring t = std::wstring(Tr(L"Calibration ", L"Calibration ")) + src;
  if (!i.model.empty()) t += L" (" + A2W(i.model) + L")";
  t += L" · " + std::to_wstring(i.views) + Tr(L" vues · base ", L" views · baseline ") + Dec(i.baselineMm, 1) + Tr(L" mm · champ ", L" mm · field of view ") + Dec(i.hfov, 0) + L"°\r\n";
  t += std::wstring(Tr(L"Faite en ", L"Made at ")) + std::to_wstring(i.calibW) + L" × " + std::to_wstring(i.calibH) + (i.fps > 0 ? Tr(L" à ", L" at ") + std::to_wstring(i.fps) + Tr(L" i/s", L" fps") : L"") +
       Tr(L" par œil ; mode caméra actuel : ", L" per eye; current camera mode: ") + std::to_wstring(i.runW) + L" × " + std::to_wstring(i.runH) +
       (i.runFps > 0 ? Tr(L" à ", L" at ") + std::to_wstring(i.runFps) + Tr(L" i/s", L" fps") : L"") + L"\r\n";
  t += std::wstring(Tr(L"Zoom du mode : ×", L"Mode zoom: ×")) + Dec(i.zoom, 2) + L"  (" + calview::Pick(calview::ZoomSourceText(i.zoomSource, i.zoomConfirmed), g_en) + L")";
  if (i.zoomN > 0 && i.zoomSource != "calib") t += L" · " + std::wstring(Tr(L"mesuré ", L"measured ")) + Dec(i.zoomResidual, 2) + L" (" + std::to_wstring(i.zoomN) + Tr(L" pas)", L" steps)");
  if (!i.error.empty()) t += L"\r\n" + A2W(i.error, CP_UTF8);
  if (!g_procs.full) t += std::wstring(L"\r\n") + Tr(L"(Le suivi est arrêté : ce sont les valeurs de son dernier lancement.)", L"(Tracking is stopped: these are the values of its last run.)");
  return t;
}

std::wstring CalPrepStatus() {
  if (!g_cp.note.empty()) return g_cp.note;
  if (g_cp.starting) return g_procs.full ? Tr(L"Démarrage de la calibration…", L"Starting the calibration…") : Tr(L"Démarrage du suivi (ouverture de la caméra)…", L"Starting tracking (opening the camera)…");
  return Tr(L"La calibration se fait avec le suivi lancé : le suivi de position est suspendu pendant la capture et reprend à la fin.",
            L"Calibration runs with tracking started: position tracking is paused during the capture and resumes afterwards.");
}

void CalUpdateUi() {
  using namespace calview;
  g_cp.uiDirty = false;
  const Status& s = g_cp.st;
  const bool run = g_procs.full;

  // préparation
  const double sq = CalSquareMm();
  CalText(ID_CAL_SQ, sq > 0 ? std::wstring(Tr(L"Un carreau = ", L"One square = ")) + Dec(sq, 2) + L" mm" : std::wstring(Tr(L"Saisis la largeur mesurée de la grille", L"Enter the measured width of the grid")));
  CalText(ID_CAL_INFO, CalInfoText());
  CalText(ID_CAL_STATUS, CalPrepStatus());
  EnableWindow(H(ID_CAL_IMPORT), run);
  for (int id : {ID_CZ_AUTO, ID_CZ_VAL, ID_CZ_SET, ID_CZ_RESET}) EnableWindow(H(id), run && g_cp.info.valid);
  if (g_cp.info.valid) {
    SetChecked(ID_CZ_AUTO, g_cp.info.zoomAuto);
    if (GetFocus() != H(ID_CZ_VAL)) SetTextIfChanged(H(ID_CZ_VAL), Dec(g_cp.info.zoom, 2));
  }
  SetTextIfChanged(H(ID_CAL_START), run ? Tr(L"▶  Commencer la calibration", L"▶  Start the calibration") : Tr(L"▶  Démarrer le suivi et commencer", L"▶  Start tracking and begin"));
  EnableWindow(H(ID_CAL_START), !g_cp.starting);

  // capture
  if (g_cp.step == kCapture) {
    std::wstring msg;
    if (GetTickCount() < g_cp.flashUntil) msg = Tr(L"Vue enregistrée !", L"View saved!");
    else msg = Pick(MessageText(s.msg), g_en);
    CalText(ID_CAL_MSG, msg);
    int n = 0;
    const GoalInfo* g = Goals(&n);
    const bool has = s.goal >= 0 && s.goal < n;
    CalText(ID_CAL_TIPT, has ? Pick(g[s.goal].title, g_en) : Tr(L"Tout est couvert !", L"Everything is covered!"));
    CalText(ID_CAL_TIPB, has ? Pick(g[s.goal].tip, g_en)
                             : Tr(L"Tu as montré la mire sous tous les angles. Tu peux lancer le calcul, ou continuer quelques vues pour plus de précision.",
                                  L"You have shown the board from every angle. You can run the computation, or keep going a few views for more precision."));
    EnableWindow(H(ID_CAL_CALC), s.views >= 10);
  }

  // calcul
  if (g_cp.step == kCompute)
    CalText(ID_CAL_COMP, Tr(L"Je cherche le meilleur modèle de distorsion de ton objectif, la focale et la position exacte des deux caméras l'une par rapport à l'autre. Cela prend en général 10 à 60 secondes. Tu peux enlever la mire.\r\n\r\nNe ferme pas cette page et n'annule pas pendant ce temps : le programme de suivi attend la fin du calcul.",
                            L"I am looking for the best distortion model of your lens, the focal length and the exact position of the two cameras relative to each other. This usually takes 10 to 60 seconds. You can put the board away.\r\n\r\nDo not close this page or cancel meanwhile: the tracking program waits for the computation to finish."));

  // résultat
  if (g_cp.step == kResult) {
    const Result& r = s.result;
    std::wstring t, w;
    if (s.state == "error") {
      t = std::wstring(Tr(L"La calibration a échoué.\r\n\r\n", L"The calibration failed.\r\n\r\n")) + (s.error.empty() ? std::wstring(Tr(L"Erreur inconnue.", L"Unknown error.")) : A2W(s.error, CP_UTF8));
      w = Tr(L"Causes fréquentes : mire pas assez variée, floue ou pliée ; mauvais éclairage ; pas assez de vues. Recommence en suivant les conseils.",
             L"Common causes: not enough variety in the poses, blurry or bent board, poor lighting, too few views. Start again following the tips.");
    } else {
      t += std::wstring(Tr(L"Erreur de reprojection : G ", L"Reprojection error: L ")) + Dec(r.rmsL, 2) + Tr(L" px, D ", L" px, R ") + Dec(r.rmsR, 2) + Tr(L" px  (moins de 0,5 = très bien)\r\n", L" px  (under 0.5 = very good)\r\n");
      t += std::wstring(Tr(L"Alignement des deux caméras : ", L"Alignment of the two cameras: ")) + Dec(r.epi, 2) + Tr(L" px, pire cas ", L" px, worst case ") + Dec(r.epiMax, 2) + Tr(L" px  (moins de 0,3 = très bien)\r\n", L" px  (under 0.3 = very good)\r\n");
      t += std::wstring(Tr(L"Contrôle sur des vues mises de côté : ", L"Check on held-out views: ")) + Dec(r.heldOut, 2) + L" px\r\n";
      t += std::wstring(Tr(L"Distance entre les deux caméras : ", L"Distance between the two cameras: ")) + Dec(r.baselineMm, 1) + L" mm\r\n";
      t += std::wstring(Tr(L"Champ de vision horizontal : ", L"Horizontal field of view: ")) + Dec(r.hfov, 0) + L"°\r\n";
      t += std::wstring(Tr(L"Vues utilisées : ", L"Views used: ")) + std::to_wstring(r.used) + Tr(L" sur ", L" of ") + std::to_wstring(r.total) + Tr(L" · image couverte à ", L" · image covered at ") + Dec(r.coverage * 100.0, 0) + L" %\r\n";
      t += std::wstring(Tr(L"Modèle de distorsion choisi : ", L"Distortion model chosen: ")) + (r.model.empty() ? L"-" : A2W(r.model));
      for (const auto& code : r.warnings) {
        const std::wstring one = Pick(WarningText(code), g_en);
        if (!one.empty()) w += L"! " + one + L"\r\n";
      }
    }
    CalText(ID_CAL_RES, t);
    CalText(ID_CAL_WARN, w);
    const bool ok = s.state != "error";
    SetTextIfChanged(H(ID_CAL_APPLY), s.applied ? Tr(L"Enregistrée et appliquée", L"Saved and applied") : Tr(L"Enregistrer et appliquer", L"Save and apply"));
    EnableWindow(H(ID_CAL_APPLY), ok && !s.applied);
    EnableWindow(H(ID_CAL_TOVERIFY), ok);
    CalText(ID_CAL_RNOTE, ok ? Tr(L"Cette calibration sert pour tous les modes de la caméra (résolution, cadence) : le suivi recalcule les focales pour le mode utilisé et apprend le « zoom du mode » avec le gyro de l'IMU. "
                                  L"L'échelle (les millimètres) vient de la taille d'un carreau : la vérification à la règle confirme que les distances sont justes.",
                                  L"This calibration is used for every camera mode (resolution, frame rate): tracking recomputes the focal lengths for the mode in use and learns the “mode zoom” with the IMU gyro. "
                                  L"The scale (millimetres) comes from the square size: the ruler check confirms that distances are right.")
                           : std::wstring());
    for (int id : {ID_CAL_APPLY, ID_CAL_TOVERIFY}) ShowWindow(H(id), (g_page == PG_CALIB && g_cp.step == kResult && ok) ? SW_SHOW : SW_HIDE);
  }

  // vérification à la règle
  if (g_cp.step == kVerify) {
    std::wstring t;
    const bool waiting = g_cp.verifySentAt != 0 && s.verifySeq == g_cp.verifySentSeq;
    if (waiting) {
      t = (GetTickCount() - g_cp.verifySentAt > 2500) ? Tr(L"Mesure impossible : la mire doit être visible par les deux caméras, de face. Rapproche-la ou centre-la, puis recommence.",
                                                          L"Cannot measure: the board must be visible to both cameras, facing them. Move it closer or center it, then try again.")
                                                       : Tr(L"Mesure en cours…", L"Measuring…");
    } else if (s.verifySeq <= 0) {
      t = Tr(L"Pas encore de mesure. Place la mire, règle la distance puis clique sur Mesurer.", L"No measurement yet. Place the board, enter the distance, then click Measure.");
    } else {
      double dist = 0;
      ParseMm(ID_CAL_VDIST, 100, 3000, &dist);
      const double dev = (s.verifyRatio - 1.0) * 100.0;
      t = std::wstring(Tr(L"Le système estime ", L"The system estimates ")) + Dec(s.verifyEstMm, 0) + Tr(L" mm ; tu as mesuré ", L" mm; you measured ") + Dec(dist, 0) + Tr(L" mm. Rapport : ", L" mm. Ratio: ") + Dec(s.verifyRatio, 3) + L".\r\n";
      t += std::fabs(dev) <= 2.0 ? std::wstring(Tr(L"Écart de ", L"Difference of ")) + Dec(std::fabs(dev), 1) + Tr(L" % seulement : l'échelle est bonne.", L" % only: the scale is right.")
                                 : std::wstring(Tr(L"Écart de ", L"Difference of ")) + Dec(std::fabs(dev), 1) + Tr(L" % : clique sur Corriger l'échelle. Si l'écart revient, vérifie la taille d'un carreau et mesure de plus loin.",
                                                                    L" %: click Correct the scale. If it comes back, check the square size and measure from farther.");
    }
    CalText(ID_CAL_VTXT, t);
    const bool measured = s.verifySeq > 0 && s.verifyRatio > 0.3 && !waiting;
    const bool off = measured && std::fabs(s.verifyRatio - 1.0) > 0.02;
    SetTextIfChanged(H(ID_CAL_VFIX), off ? std::wstring(Tr(L"Corriger l'échelle (× ", L"Correct the scale (× ")) + Dec(s.verifyRatio, 3) + L")" : std::wstring(Tr(L"Échelle correcte, rien à corriger", L"Scale is right, nothing to correct")));
    EnableWindow(H(ID_CAL_VFIX), off);
  }
  InvalidateRect(H(ID_CAL_GFX_BAR), nullptr, FALSE);
  InvalidateRect(H(ID_CAL_GFX_TIP), nullptr, FALSE);
  InvalidateRect(H(ID_CAL_GFX_GOALS), nullptr, FALSE);
  InvalidateRect(H(ID_CAL_GFX_EX), nullptr, FALSE);
  InvalidateRect(H(ID_CAL_GFX_VB), nullptr, FALSE);
  InvalidateRect(H(ID_CAL_GFX_PV), nullptr, FALSE);
  InvalidateRect(H(ID_CAL_GFX_PV2), nullptr, FALSE);
}

void CalSetStep(int step) {
  if (step == g_calStep) return;
  g_calStep = step;
  g_cp.step = step;
  CalApplyStep();
  OnFastTick();   // l'aperçu de la caméra démarre ou s'arrête selon l'étape
}

// Lit les fichiers d'état (au plus 5 fois par seconde), met à jour l'étape et les textes.
void CalPoll(bool force = false) {
  using namespace calview;
  const DWORD now = GetTickCount();
  if (!force && now - g_cp.lastPoll < 200) return;
  g_cp.lastPoll = now;
  std::string txt;
  FILETIME ft = {0, 0};
  bool changed = g_cp.uiDirty;
  if (g_procs.full && ReadShared(ExeDir() + L"\\calib_status.json", txt, &ft) && (force || CompareFileTime(&ft, &g_cp.stStamp) != 0)) {
    Status n;
    if (ParseStatus(txt, n)) {
      if (n.acceptSeq != g_cp.st.acceptSeq && g_cp.lastAccept >= 0 && n.state == "capture") g_cp.flashUntil = now + 1200;
      g_cp.lastAccept = n.acceptSeq;
      g_cp.st = n;
      g_cp.stStamp = ft;
      changed = true;
    }
  }
  if (ReadShared(ExeDir() + L"\\calib_info.json", txt, &ft) && (force || CompareFileTime(&ft, &g_cp.infoStamp) != 0)) {
    Info n;
    if (ParseInfo(txt, n)) { g_cp.info = n; g_cp.infoStamp = ft; changed = true; }
  }
  if (g_procs.full != g_cp.lastRun) { g_cp.lastRun = g_procs.full; changed = true; }
  if (g_cp.flashUntil && now > g_cp.flashUntil) { g_cp.flashUntil = 0; changed = true; }
  if (g_cp.step == kVerify && g_cp.verifySentAt != 0) changed = true;   // « mesure en cours » devient « mesure impossible » au bout de 2,5 s
  if (!g_procs.full) {   // plus de sonde : l'état lu n'est plus valable
    if (g_cp.st.state != "idle") { g_cp.st = Status(); changed = true; }
    g_cp.wantVerify = false;
  }
  if (g_cp.starting) {
    if (g_cp.st.state == "capture" || g_cp.st.state == "compute") {
      g_cp.starting = false;
      g_cp.note.clear();
      changed = true;
    } else if (now - g_cp.startReq > 45000) {
      g_cp.starting = false;
      g_cp.note = Tr(L"Pas de réponse du suivi. Vérifie la caméra dans la page Caméra, puis réessaie.", L"Tracking did not answer. Check the camera on the Camera page, then try again.");
      changed = true;
    } else if (g_procs.full && now - g_cp.lastStartSend >= 1500) {
      double total = 0, hint = 0;
      int n = 0;
      const BoardPreset* p = Presets(&n);
      const BoardPreset b = p[std::min(std::max(GetSel(ID_CAL_PRESET), 0), n - 1)];
      ParseMm(ID_CAL_TOTAL, 20, 2000, &total);
      if (!ParseMm(ID_CAL_HINT, 0, 400, &hint)) hint = 0;
      char line[96];
      std::snprintf(line, sizeof(line), "CALSTART %d %d %.3f %.1f", b.sx - 1, b.sy - 1, total / b.sx, hint);
      SendUdp(9877, line);
      g_cp.lastStartSend = now ? now : 1;
    }
  }
  const int ns = DecideStep(g_procs.full, g_cp.st, g_cp.wantVerify);
  if (ns != g_cp.step) {
    if (ns == kResult || ns == kPrep) g_cp.verifySentAt = 0;
    CalSetStep(ns);
    changed = true;
  }
  if (changed) CalUpdateUi();
}

// Rend la main à la sonde (le suivi reprend) si une calibration est restée ouverte, hors calcul (l'annuler bloquerait la sonde jusqu'à sa fin).
void CalCancelIfOpen(bool force) {
  if (!g_procs.full) return;
  const std::string& st = g_cp.st.state;
  if (st != "capture" && st != "done" && st != "error") return;
  const DWORD now = GetTickCount();
  if (!force && now - g_cp.lastCancel < 1500) return;
  g_cp.lastCancel = now ? now : 1;
  SendUdp(9877, "CALCANCEL");
  g_cp.wantVerify = false;
}

// Appelée à chaque changement de page et toutes les 700 ms : une calibration laissée ouverte hors de sa page est annulée.
void CalBackground() {
  if (g_page == PG_CALIB) return;
  g_cp.starting = false;   // on a quitté la page : plus de CALSTART renvoyé
  if (!g_procs.full) return;
  std::string txt;
  FILETIME ft = {0, 0};
  if (ReadShared(ExeDir() + L"\\calib_status.json", txt, &ft)) {
    calview::Status n;
    if (calview::ParseStatus(txt, n)) g_cp.st = n;
  }
  CalCancelIfOpen(false);
}

void CalStart() {
  double total = 0;
  if (!ParseMm(ID_CAL_TOTAL, 60, 2000, &total)) {
    MessageBoxW(g_wnd, Tr(L"Mesure la largeur de TOUTE la grille imprimée (en mm) et saisis-la : entre 60 et 2000.", L"Measure the width of the WHOLE printed grid (in mm) and enter it: between 60 and 2000."),
                Tr(L"Calibration de la caméra", L"Camera calibration"), MB_OK | MB_ICONWARNING);
    SetFocus(H(ID_CAL_TOTAL));
    return;
  }
  if (!SaveFields()) return;   // mémorise aussi la grille et la caméra choisies
  g_cp.note.clear();
  g_cp.starting = true;
  g_cp.startReq = GetTickCount();
  g_cp.lastStartSend = 0;
  g_cp.lastAccept = -1;
  ScanProbes();
  if (!g_procs.full) StartTracking();
  CalPoll(true);
  CalUpdateUi();
}

void CalMeasure() {
  double mm = 0;
  if (!ParseMm(ID_CAL_VDIST, 100, 3000, &mm)) {
    MessageBoxW(g_wnd, Tr(L"Mesure la distance entre la vitre des objectifs et la mire (de 100 à 3000 mm).", L"Measure the distance between the lens glass and the board (100 to 3000 mm)."),
                Tr(L"Vérification à la règle", L"Ruler check"), MB_OK | MB_ICONWARNING);
    return;
  }
  g_cp.verifySentSeq = g_cp.st.verifySeq;
  g_cp.verifySentAt = GetTickCount() ? GetTickCount() : 1;
  char line[48];
  std::snprintf(line, sizeof(line), "CALVERIFY %.1f", mm);
  SendUdp(9877, line);
  CalUpdateUi();
}

// ---- dessin : barre de progression, aperçu avec coins détectés et pose « fantôme », conseil illustré, liste des poses
COLORREF CalOnAccent() { return Luma(g_accent) > 0.6 ? RGB(0, 0, 0) : RGB(255, 255, 255); }

void DrawBoard(HDC dc, const calview::Pose& pose, int sx, int sy, const RECT& rc, COLORREF dark, COLORREF light, bool fill, COLORREF line, int lineW, bool outlineOnly) {
  const double rw = rc.right - rc.left, rh = rc.bottom - rc.top;
  HPEN pen = outlineOnly || !fill ? CreatePen(PS_SOLID, std::max(1, lineW), line) : (HPEN)nullptr;
  HGDIOBJ oldPen = SelectObject(dc, pen ? pen : GetStockObject(NULL_PEN));
  HGDIOBJ oldBr = SelectObject(dc, GetStockObject(NULL_BRUSH));
  HBRUSH bd = CreateSolidBrush(dark), bl = CreateSolidBrush(light);
  for (int j = 0; j < sy; j++)
    for (int i = 0; i < sx; i++) {
      const double xs[2] = {-1.0 + 2.0 * i / sx, -1.0 + 2.0 * (i + 1) / sx};
      const double ys[2] = {-1.0 + 2.0 * j / sy, -1.0 + 2.0 * (j + 1) / sy};
      const int cx[4] = {0, 1, 1, 0}, cy[4] = {0, 0, 1, 1};
      POINT pt[4];
      for (int k = 0; k < 4; k++) {
        double ox, oy;
        calview::ProjectBoard(pose, sx, sy, xs[cx[k]], ys[cy[k]], rc.left, rc.top, rw, rh, ox, oy);
        pt[k].x = (LONG)std::lround(ox);
        pt[k].y = (LONG)std::lround(oy);
      }
      if (fill) SelectObject(dc, ((i + j) & 1) == 0 ? bd : bl);
      else SelectObject(dc, GetStockObject(NULL_BRUSH));
      Polygon(dc, pt, 4);
    }
  SelectObject(dc, oldBr);
  SelectObject(dc, oldPen);
  DeleteObject(bd);
  DeleteObject(bl);
  if (pen) DeleteObject(pen);
}

calview::Pose GoalPose(int goal) {
  calview::Pose p;
  int n = 0;
  const calview::GoalInfo* g = calview::Goals(&n);
  if (goal >= 0 && goal < n) {
    p.px = g[goal].px; p.py = g[goal].py; p.size = g[goal].size; p.yaw = g[goal].yaw; p.pitch = g[goal].pitch; p.roll = g[goal].roll;
  } else {
    p.size = 0.6;
  }
  return p;
}

void BoardSquares(int* sx, int* sy) {
  if (g_cp.st.cols > 0 && g_cp.st.rows > 0 && (g_cp.step == calview::kCapture || g_cp.step == calview::kVerify)) { *sx = g_cp.st.cols + 1; *sy = g_cp.st.rows + 1; return; }
  int n = 0;
  const calview::BoardPreset* p = calview::Presets(&n);
  const int i = std::min(std::max(GetSel(ID_CAL_PRESET), 0), n - 1);
  *sx = p[i].sx;
  *sy = p[i].sy;
}

void DrawCameraView(HDC dc, const RECT& rc, bool ghost) {
  using namespace calview;
  const int W = rc.right - rc.left, Hh = rc.bottom - rc.top;
  const bool have = g_pvHave && g_pvSrc == 1 && WantedSource() == 1 && g_pvBuf.size() >= (size_t)kPvW * kPvH * 4;
  if (have) {
    BITMAPINFO bi;
    memset(&bi, 0, sizeof(bi));
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = kPvW;
    bi.bmiHeader.biHeight = -kPvH;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    SetStretchBltMode(dc, HALFTONE);
    SetBrushOrgEx(dc, 0, 0, nullptr);
    StretchDIBits(dc, rc.left, rc.top, W, Hh, 0, 0, kPvW, kPvH, g_pvBuf.data(), &bi, DIB_RGB_COLORS, SRCCOPY);
  } else {
    HBRUSH b = CreateSolidBrush(RGB(22, 22, 22));
    FillRect(dc, &rc, b);
    DeleteObject(b);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(160, 160, 160));
    SelectObject(dc, g_font);
    RECT t = rc;
    DrawTextW(dc, Tr(L"En attente de la caméra…", L"Waiting for the camera…"), -1, &t, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
    return;
  }
  const Status& s = g_cp.st;
  const EyeRect e = PreviewEye(s.imgW, s.imgH, s.swapEyes);
  const double sxw = (double)W / kPvW, syh = (double)Hh / kPvH;
  RECT er = {rc.left + (LONG)std::lround(e.x * sxw), rc.top + (LONG)std::lround(e.y * syh), rc.left + (LONG)std::lround((e.x + e.w) * sxw), rc.top + (LONG)std::lround((e.y + e.h) * syh)};
  if (ghost && s.state == "capture") {   // pose à reproduire : contour de la mire sur l'œil « gauche » de la sonde
    int sqx = 0, sqy = 0;
    BoardSquares(&sqx, &sqy);
    DrawBoard(dc, GoalPose(s.goal), sqx, sqy, er, 0, 0, false, RGB(255, 200, 0), 1, true);
    Pose pg = GoalPose(s.goal);
    HPEN pen = CreatePen(PS_SOLID, 2, RGB(255, 200, 0));
    HGDIOBJ op = SelectObject(dc, pen);
    const double cxs[5] = {-1, 1, 1, -1, -1}, cys[5] = {-1, -1, 1, 1, -1};
    for (int k = 0; k < 5; k++) {
      double ox, oy;
      ProjectBoard(pg, sqx, sqy, cxs[k], cys[k], er.left, er.top, er.right - er.left, er.bottom - er.top, ox, oy);
      if (k == 0) MoveToEx(dc, (int)std::lround(ox), (int)std::lround(oy), nullptr);
      else LineTo(dc, (int)std::lround(ox), (int)std::lround(oy));
    }
    SelectObject(dc, op);
    DeleteObject(pen);
  }
  const int n = (int)s.corners.size() / 2;
  if (n > 0 && s.foundL && (s.state == "capture" || s.state == "done")) {
    HBRUSH b = CreateSolidBrush(s.foundR ? RGB(50, 255, 90) : RGB(255, 190, 25));
    const int ew = er.right - er.left, eh = er.bottom - er.top;
    for (int i = 0; i < n; i++) {
      const int x = er.left + (int)std::lround(s.corners[2 * i] * ew), y = er.top + (int)std::lround(s.corners[2 * i + 1] * eh);
      RECT d = {x - 2, y - 2, x + 3, y + 3};
      FillRect(dc, &d, b);
    }
    DeleteObject(b);
  }
  if (ghost) {   // séparation entre les deux yeux + rappel de ce qui est l'œil « gauche »
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(255, 255, 255));
    SelectObject(dc, g_font);
    RECT t = {er.left + 6, er.top + 4, er.right, er.top + 24};
    DrawTextW(dc, Tr(L"œil gauche (calcul)", L"left eye (used)"), -1, &t, DT_SINGLELINE | DT_LEFT | DT_NOPREFIX);
  }
}

LRESULT CALLBACK CalGfxProc(HWND h, UINT m, WPARAM w, LPARAM l) {
  if (m == WM_ERASEBKGND) return 1;
  if (m != WM_PAINT) return DefWindowProcW(h, m, w, l);
  using namespace calview;
  PAINTSTRUCT ps;
  HDC dc = BeginPaint(h, &ps);
  RECT rc;
  GetClientRect(h, &rc);
  HDC mem = CreateCompatibleDC(dc);
  HBITMAP bmp = CreateCompatibleBitmap(dc, std::max<LONG>(1, rc.right), std::max<LONG>(1, rc.bottom));
  HGDIOBJ old = SelectObject(mem, bmp);
  const int kind = (int)GetWindowLongPtrW(h, GWLP_USERDATA);
  const Status& s = g_cp.st;
  SetBkMode(mem, TRANSPARENT);
  if (kind == 1) {   // barre de progression
    HBRUSH bg = g_brBg;
    FillRect(mem, &rc, bg);
    double v = 0;
    std::wstring text;
    if (g_cp.step == kCapture) {
      v = s.progress;
      text = std::wstring(Tr(L"Progression ", L"Progress ")) + Dec(v * 100, 0) + L" %   ·   " + std::to_wstring(s.views) + Tr(L" vues / ", L" views / ") + std::to_wstring(s.target) + L"   ·   " +
             std::to_wstring(PopCount(s.goalsDone)) + Tr(L" poses sur ", L" poses of ") + std::to_wstring(s.goalCount);
    } else if (g_cp.step == kCompute) {
      v = s.cprogress;
      text = std::wstring(Tr(L"Calcul ", L"Computing ")) + Dec(v * 100, 0) + L" %   ·   " + Pick(StageText(s.stage), g_en);
    } else if (g_cp.step == kResult) {
      v = s.state == "error" ? 0.0 : s.result.score / 100.0;
      text = s.state == "error" ? std::wstring(Tr(L"Échec de la calibration", L"Calibration failed"))
                                : std::wstring(Tr(L"Note ", L"Score ")) + std::to_wstring(s.result.score) + L" / 100   ·   " + Pick(Verdict(s.result.score), g_en);
    }
    v = std::min(1.0, std::max(0.0, v));
    RECT tr = rc;
    FillRound(mem, tr, S(6), g_pal.border, g_pal.border, false);
    RECT fr = rc;
    fr.right = rc.left + (LONG)std::lround((rc.right - rc.left) * v);
    if (fr.right - fr.left > S(12)) FillRound(mem, fr, S(6), g_accent, g_accent, false);
    else if (fr.right - fr.left > 0) { fr.right = fr.left + S(12); FillRound(mem, fr, S(6), g_accent, g_accent, false); }
    SelectObject(mem, g_fontTitle);
    for (int pass = 0; pass < 2; pass++) {
      const int saved = SaveDC(mem);
      RECT clip = rc;
      if (pass == 0) clip.right = std::max(clip.left, fr.right); else clip.left = std::min(clip.right, std::max(clip.left, fr.right));
      IntersectClipRect(mem, clip.left, clip.top, clip.right, clip.bottom);
      SetTextColor(mem, pass == 0 ? CalOnAccent() : g_pal.text);
      RECT t = rc;
      DrawTextW(mem, text.c_str(), -1, &t, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
      RestoreDC(mem, saved);
    }
  } else if (kind == 2 || kind == 6) {   // aperçu de la caméra (2 : avec la pose à reproduire, 6 : règle)
    DrawCameraView(mem, rc, kind == 2);
  } else if (kind == 3 || kind == 5 || kind == 7) {   // mire en perspective : conseil, exemple à imprimer, schéma de la règle
    FillRect(mem, &rc, g_brCard);
    RECT inner = {rc.left + S(4), rc.top + S(4), rc.right - S(4), rc.bottom - S(4)};
    FillRound(mem, inner, S(6), g_pal.field, g_pal.border, true);
    int sx = 0, sy = 0;
    BoardSquares(&sx, &sy);
    Pose p;
    if (kind == 3) p = GoalPose(s.goal);
    else if (kind == 5) p.size = 0.9;
    else p.size = 0.62;
    DrawBoard(mem, p, sx, sy, inner, RGB(18, 18, 24), RGB(244, 244, 244), true, RGB(120, 120, 120), 1, false);
  } else if (kind == 4) {   // les 16 poses : faites / en cours / à faire
    FillRect(mem, &rc, g_brBg);
    int n = 0;
    const GoalInfo* g = Goals(&n);
    const int cols = 4, rows = (n + cols - 1) / cols;
    const int cw = (rc.right - rc.left) / cols, ch = (rc.bottom - rc.top) / rows;
    SelectObject(mem, g_font);
    for (int i = 0; i < n; i++) {
      const bool done = (s.goalsDone & (1 << i)) != 0;
      const bool cur = i == s.goal;
      const std::wstring t = std::wstring(done ? L"[x] " : cur ? L"[>] " : L"[ ] ") + Pick(g[i].name, g_en);
      RECT r = {rc.left + (i % cols) * cw, rc.top + (i / cols) * ch, rc.left + (i % cols + 1) * cw - S(4), rc.top + (i / cols + 1) * ch};
      SetTextColor(mem, done ? g_accent : cur ? g_pal.text : g_pal.dim);
      DrawTextW(mem, t.c_str(), -1, &r, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX | DT_END_ELLIPSIS);
    }
  }
  BitBlt(dc, 0, 0, rc.right, rc.bottom, mem, 0, 0, SRCCOPY);
  SelectObject(mem, old);
  DeleteObject(bmp);
  DeleteDC(mem);
  EndPaint(h, &ps);
  return 0;
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
    if (l.rfind("[imu] ", 0) == 0) { g_lastImu = l.substr(6); continue; }   // idem : affichée dans la page Capteurs
    if (l.rfind("[montage] ", 0) == 0) g_lastMount = l.substr(10);
    else if (l.rfind("[yeux] ", 0) == 0) g_lastEye = l.substr(7);
    else if (l.rfind("[bouche] ", 0) == 0) g_lastMouth = l.substr(9);
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
  ScanProbes();
  const bool running = g_procs.full;
  g_running = running;
  if (!running) g_stopDeadline = 0;
  if (!g_procs.full && !g_procs.svc) g_lastImu.clear();
  SetTextIfChanged(H(ID_START), running ? Tr(L"■  Arrêter le suivi", L"■  Stop tracking") : Tr(L"▶  Démarrer le suivi", L"▶  Start tracking"));
  for (int id : {ID_A_RESTART, ID_A_FLOOR, ID_A_YAW, ID_A_SWAP, ID_A_CHECK}) EnableWindow(H(id), running);
  if (g_msgUntil == 0 || (LONG)(GetTickCount() - g_msgUntil) > 0) {
    g_msgUntil = 0;
    SetTextIfChanged(H(ID_STATUS), running ? Tr(L"En marche", L"Running") : g_procs.svc ? Tr(L"Services (sans suivi)", L"Services (no tracking)") : Tr(L"Arrêté", L"Stopped"));
  }
  SetTextIfChanged(H(ID_STATS), running && !g_lastStats.empty() ? LogToW(g_lastStats) : std::wstring());
  if (g_stopDeadline && running && (LONG)(GetTickCount() - g_stopDeadline) > 0) { KillProbe(); g_stopDeadline = 0; }

  // « Démarrer le suivi » demandé pendant le mode services : on attend qu'il ait rendu la caméra et le port série
  if (g_startAfterSvc) {
    if (!g_procs.svc) { g_startAfterSvc = false; StartProbe(); ScanProbes(); }
    else if ((LONG)(GetTickCount() - g_svcStopDeadline) > 0) KillPid(g_svcPid);
  }

  // textes d'état des pages
  std::wstring imu;
  if (g_procs.full) imu = Tr(L"Le suivi tourne : il lit l'IMU lui-même.", L"Tracking is running: it reads the IMU itself.");
  else if (GetSel(ID_PORT) <= 0) imu = Tr(L"Choisis le port de l'IMU, puis clique sur Enregistrer.", L"Pick the IMU port, then click Save.");
  else if (!g_lastImu.empty()) imu = LogToW(g_lastImu);
  else imu = Tr(L"Lecture de l'IMU en cours de démarrage…", L"Starting the IMU reading…");
  SetTextIfChanged(H(ID_IMUSTAT), imu);
  SetTextIfChanged(H(ID_MOUNTMSG), g_lastMount.empty() ? std::wstring(Tr(L"Clique sur « Calibrer le montage » : les consignes et le résultat s'affichent ici.", L"Click “Calibrate the mounting”: the prompts and the result appear here."))
                                                      : LogToW(g_lastMount));
  SetTextIfChanged(H(ID_EYESTAT), g_lastEye.empty() ? std::wstring(Tr(L"Aperçu et étalonnage des yeux : aucune mesure pour l'instant.", L"Eye preview and calibration: nothing measured yet."))
                                                    : LogToW(g_lastEye));
  SetTextIfChanged(H(ID_MOUTHSTAT), g_lastMouth.empty() ? std::wstring(Tr(L"Aperçu et étalonnage de la bouche : aucune mesure pour l'instant.", L"Mouth preview and calibration: nothing measured yet."))
                                                        : LogToW(g_lastMouth));
  SetTextIfChanged(H(ID_PV_CAM), g_pvOn[0] ? Tr(L"Arrêter l'aperçu", L"Stop the preview") : Tr(L"Voir l'aperçu", L"Show the preview"));
  SetTextIfChanged(H(ID_PV_EYE), g_pvOn[1] ? Tr(L"Arrêter l'aperçu", L"Stop the preview") : Tr(L"Voir l'aperçu", L"Show the preview"));
  SetTextIfChanged(H(ID_PV_MOUTH), g_pvOn[2] ? Tr(L"Arrêter l'aperçu", L"Stop the preview") : Tr(L"Voir l'aperçu", L"Show the preview"));

  // mode services : lancé seulement quand une page en a besoin, arrêté après 20 s sans besoin
  const int want = WantedSource();
  if (!g_procs.full && !g_procs.svc && !g_startAfterSvc) {
    if (want || g_cal.active || (g_page == PG_SENSORS && GetSel(ID_PORT) > 0)) StartServices(false);
  } else if (g_procs.svc && !g_startAfterSvc) {
    const bool needed = want || g_cal.active || g_page == PG_SENSORS || !g_pending.empty();
    if (needed) {
      g_svcIdleSince = 0;
    } else if (g_svcIdleSince == 0) {
      g_svcIdleSince = GetTickCount() ? GetTickCount() : 1;
    } else if (GetTickCount() - g_svcIdleSince > 20000) {
      SendCmd('Q');
      g_svcIdleSince = 0;
    }
  }
}

void ApplyLanguage() {
  for (const auto& l : g_loc) SetTextIfChanged(l.h, Tr(l.fr, l.en));
  SetWindowTextW(g_wnd, Tr(L"AR6DoF — Centre de contrôle", L"AR6DoF — Control center"));
  g_loading = true;
  FillCamTypes();
  FillModes();
  FillBackends(ID_BACKEND);
  FillBackends(ID_EYEBACK);
  FillBackends(ID_MOUTHBACK);
  FillCams(ID_EYEL, true);
  FillCams(ID_EYER, true);
  FillCams(ID_MOUTHCAM, true);
  FillPorts();
  g_loading = false;
  UpdateCamType();
  UpdateCamHint();
  UpdateState();
  CalUpdateUi();
  RedrawWindow(g_wnd, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
}

void BrowseCalibDir() {
  BROWSEINFOW bi;
  memset(&bi, 0, sizeof(bi));
  bi.hwndOwner = g_wnd;
  bi.lpszTitle = Tr(L"Dossier de calibration (AR6DoF ou RTAB-Map)", L"Calibration folder (AR6DoF or RTAB-Map)");
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
const int kClientW = 940, kClientH = 752;   // taille de la zone cliente à 96 ppp (réduite automatiquement si l'écran est trop petit)

void CreateControls() {
  // Disposition : navigation à gauche (x 0..204), page à droite (cartes de x 224 à 916), barre du bas (y 704..738). Contrôles posés à la main, en pixels à 96 ppp.
  const int X = 224, CWd = 692, PAD = 20, C0 = 190, CR = 472;   // x de la carte, largeur, marge gauche, début des champs (relatif), largeur d'un champ plein
  const int PVX = PAD, PVW = 480, PVH = 180, COLX = PAD + PVW + 16, COLW = CWd - COLX - PAD;   // aperçu (480x180) et colonne de boutons à sa droite

  // ---- navigation (visible sur toutes les pages)
  g_buildPage = -1;
  for (int i = 0; i < kNavCount; i++) Mk(L"BUTTON", kNavFr[i], kNavEn[i], BS_OWNERDRAW | WS_TABSTOP, 12, 64 + i * 44, 180, 40, ID_NAV0 + i);
  Combo(16, 706, 172, ID_LANG);

  // ---- barre du bas
  Button(L"Enregistrer", L"Save", X, 704, 120, 34, ID_SAVE);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX | SS_ENDELLIPSIS, X + 136, 712, CWd - 136, 20, ID_STATUS);

  // ---- page 0 : Caméra
  g_buildPage = PG_CAM;
  CardBox(L"Caméra stéréo", L"Stereo camera", X, 62, CWd, 272);
  Label(L"Type", L"Type", X + PAD, 100, 160);
  Combo(X + C0, 96, CR, ID_CAMTYPE);
  g_lblCam = Label(L"Caméra", L"Camera", X + PAD, 138, 160);
  Combo(X + C0, 134, CR - 90, ID_CAM);
  Button(L"Actualiser", L"Refresh", X + C0 + CR - 80, 133, 80, 28, ID_CAM_REFRESH);
  Label(L"Caméra droite", L"Right camera", X + PAD, 176, 160);
  Combo(X + C0, 172, CR - 90, ID_CAMR);
  Button(L"Actualiser", L"Refresh", X + C0 + CR - 80, 171, 80, 28, ID_CAMR_REFRESH);
  Label(L"Mode", L"Mode", X + PAD, 214, 160);
  Combo(X + C0, 210, CR, ID_MODE);
  Label(L"Taille / cadence", L"Size / rate", X + PAD, 252, 160);
  Edit(X + C0, 248, 76, ID_W, true);
  Label(L"×", L"×", X + C0 + 84, 252, 14);
  Edit(X + C0 + 102, 248, 76, ID_H, true);
  Label(L"à", L"at", X + C0 + 188, 252, 18);
  Edit(X + C0 + 212, 248, 62, ID_FPS, true);
  Label(L"i/s", L"fps", X + C0 + 284, 252, 40);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + C0, 284, CR, 38, ID_CAMHINT);

  // aperçu un peu plus petit que sur les autres pages (400x150) pour que la calibration de la caméra tienne dans la même page
  const int CPW = 400, CPH = 150, CCX = PAD + CPW + 16, CCW = CWd - CCX - PAD;
  CardBox(L"Aperçu de la caméra", L"Camera preview", X, 348, CWd, 212);
  Mk(L"AR6Preview", nullptr, nullptr, 0, X + PVX, 394, CPW, CPH, ID_PVW_CAM);
  Button(L"Voir l'aperçu", L"Show the preview", X + CCX, 394, CCW, 34, ID_PV_CAM);
  Label(L"La caméra n'est ouverte que pendant l'aperçu. Elle est rendue quand tu quittes la page ou démarres le suivi.",
        L"The camera is only opened while the preview is shown. It is released when you leave the page or start tracking.", X + CCX, 440, CCW, 100);

  CardBox(L"Calibration de la caméra", L"Camera calibration", X, 574, CWd, 122);
  Label(L"Dossier", L"Folder", X + PAD, 626, 160);
  Edit(X + C0, 622, CR - 100, ID_CALDIR);
  Button(L"Parcourir…", L"Browse…", X + C0 + CR - 90, 621, 90, 28, ID_CALBROWSE);
  Label(L"Base réelle (mm)", L"Real baseline (mm)", X + PAD, 662, 160);
  Edit(X + C0, 658, 76, ID_BASE);
  Label(L"0 = celle de la calibration (ignorée pour une calibration AR6DoF)", L"0 = keep the calibration’s (ignored for an AR6DoF calibration)", X + C0 + 90, 662, 330);

  // ---- page 1 : Calibration (étapes : 1 préparation, 2 capture, 4 calcul, 8 résultat, 16 vérification à la règle ; masque de bits = 1 << calview::Step)
  g_buildPage = PG_CALIB;
  g_buildSteps = 1;
  CardBox(L"1 · Ta mire imprimée", L"1 · Your printed board", X, 62, CWd, 292);
  Mk(L"AR6CalGfx", nullptr, nullptr, 0, X + PAD, 104, 220, 130, ID_CAL_GFX_EX);
  Mk(L"STATIC", L"Imprime une mire (damier noir et blanc) et colle-la sur une surface PLATE et RIGIDE : carton épais, plaque, vitre. Compte les CARREAUX, pas les coins : sur l'exemple, 10 en largeur et 7 en hauteur. "
                L"Imprime à 100 % (pas « ajuster à la page »), puis mesure à la règle la largeur de TOUTE la grille : c'est elle qui donne l'échelle en millimètres.",
     L"Print a board (black and white checkerboard) and stick it on a FLAT and RIGID surface: thick cardboard, a plate, glass. Count the SQUARES, not the corners: in the example, 10 across and 7 down. "
     L"Print at 100% (not “fit to page”), then measure the width of the WHOLE grid with a ruler: it sets the scale in millimetres.",
     SS_LEFT | SS_NOPREFIX, X + PAD + 236, 104, CWd - 2 * PAD - 236, 132, ID_CAL_EXTXT);
  Label(L"Grille (carreaux)", L"Grid (squares)", X + PAD, 252, 160);
  Combo(X + C0, 248, 150, ID_CAL_PRESET);
  Label(L"largeur × hauteur, en comptant les carreaux", L"width × height, counting squares", X + C0 + 164, 252, 310);
  Label(L"Largeur de toute la grille", L"Width of the whole grid", X + PAD, 288, 170);
  Edit(X + C0, 284, 90, ID_CAL_TOTAL);
  Label(L"mm", L"mm", X + C0 + 98, 288, 30);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + C0 + 134, 288, 330, 20, ID_CAL_SQ);
  Label(L"Écart entre les objectifs", L"Distance between lenses", X + PAD, 324, 170);
  Edit(X + C0, 320, 90, ID_CAL_HINT);
  Label(L"mm  (facultatif, 0 = inconnu : sert à prévenir si le résultat s'en écarte)", L"mm  (optional, 0 = unknown: used to warn if the result differs)", X + C0 + 98, 324, 380);

  CardBox(L"2 · Calibration utilisée et mode caméra", L"2 · Calibration in use and camera mode", X, 366, CWd, 194);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 404, CWd - 2 * PAD, 76, ID_CAL_INFO);
  Check(L"Apprendre le zoom du mode automatiquement (avec le gyro de l'IMU)", L"Learn the mode zoom automatically (with the IMU gyro)", X + PAD, 484, CWd - 2 * PAD, ID_CZ_AUTO);
  Label(L"Zoom du mode", L"Mode zoom", X + PAD, 524, 120);
  Edit(X + PAD + 120, 520, 70, ID_CZ_VAL);
  Button(L"Fixer", L"Set", X + PAD + 200, 519, 80, 28, ID_CZ_SET);
  Button(L"Oublier", L"Forget", X + PAD + 290, 519, 100, 28, ID_CZ_RESET);
  Label(L"1 = rien à corriger · 2 = mode recadré ×2", L"1 = nothing to fix · 2 = mode cropped ×2", X + PAD + 404, 524, 250);

  CardBox(L"3 · Calibrer", L"3 · Calibrate", X, 572, CWd, 122);
  Button(L"▶  Commencer la calibration", L"▶  Start the calibration", X + PAD, 610, 320, 36, ID_CAL_START);
  Button(L"Importer RTAB-Map (converti)", L"Import RTAB-Map (converted)", X + PAD + 332, 610, CWd - 2 * PAD - 332, 36, ID_CAL_IMPORT);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 652, CWd - 2 * PAD, 38, ID_CAL_STATUS);

  g_buildSteps = 2 | 4 | 8;
  Mk(L"AR6CalGfx", nullptr, nullptr, 0, X, 62, CWd, 34, ID_CAL_GFX_BAR);

  g_buildSteps = 2;   // capture
  SendMessageW(Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X, 102, CWd, 40, ID_CAL_MSG), WM_SETFONT, (WPARAM)g_fontTitle, TRUE);
  Mk(L"AR6CalGfx", nullptr, nullptr, 0, X, 146, CWd, 259, ID_CAL_GFX_PV);
  CardBox(L"", L"", X, 412, CWd, 148);
  Mk(L"AR6CalGfx", nullptr, nullptr, 0, X + 8, 416, 190, 140, ID_CAL_GFX_TIP);
  SendMessageW(Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + 212, 420, 460, 28, ID_CAL_TIPT), WM_SETFONT, (WPARAM)g_fontTitle, TRUE);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + 212, 452, 460, 100, ID_CAL_TIPB);
  Mk(L"AR6CalGfx", nullptr, nullptr, 0, X, 566, CWd, 88, ID_CAL_GFX_GOALS);
  Button(L"Calculer maintenant", L"Compute now", X, 660, 220, 34, ID_CAL_CALC);
  Button(L"Inverser G / D", L"Swap L / R", X + 232, 660, 200, 34, ID_CAL_SWAP);
  Button(L"Annuler", L"Cancel", X + 444, 660, 248, 34, ID_CAL_CANCEL);

  g_buildSteps = 4;   // calcul
  CardBox(L"Calcul en cours", L"Computing", X, 110, CWd, 230);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 152, CWd - 2 * PAD, 176, ID_CAL_COMP);

  g_buildSteps = 8;   // résultat
  CardBox(L"Résultat", L"Result", X, 110, CWd, 300);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 150, CWd - 2 * PAD, 160, ID_CAL_RES);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 314, CWd - 2 * PAD, 90, ID_CAL_WARN);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X, 418, CWd, 66, ID_CAL_RNOTE);
  Button(L"Enregistrer et appliquer", L"Save and apply", X, 494, 330, 40, ID_CAL_APPLY);
  Button(L"Vérifier l'échelle avec une règle", L"Check the scale with a ruler", X, 542, 330, 36, ID_CAL_TOVERIFY);
  Button(L"Refaire la calibration", L"Redo the calibration", X, 586, 330, 36, ID_CAL_REDO);

  g_buildSteps = 16;   // vérification à la règle
  CardBox(L"Vérification à la règle", L"Ruler check", X, 62, CWd, 500);
  Label(L"Tiens la mire de FACE, immobile, bien visible par les deux caméras. Mesure avec une règle la distance entre la vitre des objectifs et la mire, puis saisis-la ci-dessous. Plus c'est loin (50 cm ou plus), plus la mesure est juste.",
        L"Hold the board FACING the camera, still, clearly visible to both cameras. Measure with a ruler the distance between the lens glass and the board, then enter it below. The farther (50 cm or more), the more accurate.",
        X + PAD, 102, CWd - 2 * PAD, 60);
  Mk(L"AR6CalGfx", nullptr, nullptr, 0, X + PAD, 168, 400, 150, ID_CAL_GFX_PV2);
  Mk(L"AR6CalGfx", nullptr, nullptr, 0, X + PAD + 416, 168, 236, 150, ID_CAL_GFX_VB);
  Label(L"Distance mesurée", L"Measured distance", X + PAD, 336, 160);
  Edit(X + C0, 332, 90, ID_CAL_VDIST);
  Label(L"mm", L"mm", X + C0 + 98, 336, 30);
  Button(L"Mesurer", L"Measure", X + C0 + 140, 331, 160, 30, ID_CAL_VMEASURE);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 376, CWd - 2 * PAD, 80, ID_CAL_VTXT);
  Button(L"Corriger l'échelle", L"Correct the scale", X + PAD, 466, 340, 36, ID_CAL_VFIX);
  Button(L"Retour au résultat", L"Back to the result", X + PAD, 512, 340, 34, ID_CAL_VBACK);
  g_buildSteps = 0;

  // ---- page 2 : Capteurs
  g_buildPage = PG_SENSORS;
  CardBox(L"Capteur de mouvement (IMU)", L"Motion sensor (IMU)", X, 62, CWd, 136);
  Label(L"Port série", L"Serial port", X + PAD, 108, 160);
  Combo(X + C0, 104, CR - 90, ID_PORT);
  Button(L"Actualiser", L"Refresh", X + C0 + CR - 80, 103, 80, 28, ID_PORT_REFRESH);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 146, CWd - 2 * PAD, 46, ID_IMUSTAT);

  CardBox(L"Montage de l'IMU (une seule fois, sans lancer le suivi)", L"IMU mounting (once, without starting tracking)", X, 206, CWd, 340);
  Label(L"Clique sur Calibrer, puis suis les consignes (compte à rebours, un bip à chaque mesure) : d'abord le casque à plat, à l'endroit ; ensuite incliné d'environ 30° vers l'AVANT (comme pour regarder tes pieds). Chaque pose est mesurée sur 1 s. Contrôle ensuite avec le modèle : il doit pencher comme ton casque.",
        L"Click Calibrate, then follow the prompts (countdown, a beep at each measurement): first the headset flat and the right way up, then tilted about 30 degrees FORWARD (as if looking at your feet). Each pose is averaged over 1 s. Then check with the model: it must tilt like your headset.", X + PAD, 244, CWd - 2 * PAD, 66);
  Mk(L"AR6Preview", nullptr, nullptr, 0, X + PVX, 318, PVW, PVH, ID_PVW_IMU);
  Button(L"Calibrer le montage", L"Calibrate the mounting", X + COLX, 318, COLW, 34, ID_M_STEP1);
  Button(L"Annuler", L"Cancel", X + COLX, 360, COLW, 34, ID_M_STEP2);
  Button(L"Inverser avant / arrière", L"Swap front / back", X + COLX, 402, COLW, 34, ID_M_FLIP);
  Button(L"Réinitialiser", L"Reset", X + COLX, 444, COLW, 34, ID_M_RESET);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 502, CWd - 2 * PAD, 38, ID_MOUNTMSG);

  // ---- page 2 : Unity
  g_buildPage = PG_UNITY;
  CardBox(L"Sorties vers Unity", L"Output to Unity", X, 62, CWd, 208);
  Label(L"Port UDP", L"UDP port", X + PAD, 100, 160);
  Edit(X + C0, 96, 90, ID_UDP, true);
  Label(L"0 = désactivé", L"0 = off", X + C0 + 102, 100, 200);
  Check(L"Partager l'image de la caméra (passthrough)", L"Share the camera image (passthrough)", X + PAD, 140, CWd - 2 * PAD, ID_SHARE);
  Check(L"Télécommande Bluetooth", L"Bluetooth remote", X + PAD, 176, CWd - 2 * PAD, ID_BLE);
  Check(L"Démarrer le suivi dès la première image", L"Start tracking on the first frame", X + PAD, 212, CWd - 2 * PAD, ID_AUTOTRACK);

  // ---- page 3 : Yeux
  g_buildPage = PG_EYES;
  CardBox(L"Suivi des yeux (eye tracking)", L"Eye tracking", X, 62, CWd, 216);
  Check(L"Activer avec 2 caméras d'œil", L"Enable with 2 eye cameras", X + PAD, 100, CWd - 2 * PAD, ID_EYE);
  Label(L"Œil gauche", L"Left eye", X + PAD, 148, 160);
  Combo(X + C0, 144, CR - 90, ID_EYEL);
  Button(L"Actualiser", L"Refresh", X + C0 + CR - 80, 143, 80, 28, ID_EYEL_REFRESH);
  Label(L"Œil droit", L"Right eye", X + PAD, 186, 160);
  Combo(X + C0, 182, CR - 90, ID_EYER);
  Button(L"Actualiser", L"Refresh", X + C0 + CR - 80, 181, 80, 28, ID_EYER_REFRESH);
  Label(L"Cadence", L"Rate", X + PAD, 224, 160);
  Edit(X + C0, 220, 76, ID_EYEFPS, true);
  Label(L"Pilote", L"Driver", X + C0 + 100, 224, 60);
  Combo(X + C0 + 160, 220, CR - 160, ID_EYEBACK);

  CardBox(L"Aperçu et étalonnage", L"Preview and calibration", X, 292, CWd, 388);
  Mk(L"AR6Preview", nullptr, nullptr, 0, X + PVX, 338, PVW, PVH, ID_PVW_EYE);
  Button(L"Voir l'aperçu", L"Show the preview", X + COLX, 338, COLW, 34, ID_PV_EYE);
  Button(L"Étalonner (9 points)", L"Calibrate (9 points)", X + COLX, 382, COLW, 34, ID_EYE_CAL);
  Button(L"Recentrer le regard", L"Recenter the gaze", X + COLX, 426, COLW, 34, ID_EYE_RECENTER);
  Button(L"Effacer l'étalonnage", L"Clear the calibration", X + COLX, 470, COLW, 34, ID_EYE_CLEAR);
  Label(L"Champ de vision de l'écran (°)", L"Screen field of view (°)", X + PAD, 536, 230);
  Edit(X + PAD + 236, 532, 60, ID_EYE_FOV, true);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 568, CWd - 2 * PAD, 40, ID_EYESTAT);
  Label(L"Étalonnage : regarde chaque point sans bouger la tête (Échap pour annuler). Champ de vision : sur un moniteur, 2 × atan(largeur ÷ (2 × distance)), "
        L"par exemple 53° pour un écran de 60 cm vu à 60 cm. Pour un étalonnage plus fidèle, refais-le dans le casque (menu Yeux d'Unity).",
        L"Calibration: look at each dot without moving your head (Esc to cancel). Field of view: on a monitor, 2 × atan(width ÷ (2 × distance)), "
        L"e.g. 53° for a 60 cm screen seen from 60 cm. For a more faithful calibration, redo it in the headset (Unity Eyes menu).",
        X + PAD, 612, CWd - 2 * PAD, 60);

  // ---- page 4 : Bouche
  g_buildPage = PG_MOUTH;
  CardBox(L"Suivi de la bouche (mouth tracking)", L"Mouth tracking", X, 62, CWd, 170);
  Check(L"Activer avec une caméra de bouche", L"Enable with a mouth camera", X + PAD, 100, CWd - 2 * PAD, ID_MOUTH);
  Label(L"Caméra", L"Camera", X + PAD, 148, 160);
  Combo(X + C0, 144, CR - 90, ID_MOUTHCAM);
  Button(L"Actualiser", L"Refresh", X + C0 + CR - 80, 143, 80, 28, ID_MOUTH_REFRESH);
  Label(L"Cadence", L"Rate", X + PAD, 186, 160);
  Edit(X + C0, 182, 76, ID_MOUTHFPS, true);
  Label(L"Pilote", L"Driver", X + C0 + 100, 186, 60);
  Combo(X + C0 + 160, 182, CR - 160, ID_MOUTHBACK);

  CardBox(L"Aperçu et étalonnage", L"Preview and calibration", X, 246, CWd, 434);
  Mk(L"AR6Preview", nullptr, nullptr, 0, X + PVX, 292, PVW, PVH, ID_PVW_MOUTH);
  Button(L"Voir l'aperçu", L"Show the preview", X + COLX, 292, COLW, 34, ID_PV_MOUTH);
  Button(L"1 · Bouche neutre", L"1 · Neutral mouth", X + COLX, 336, COLW, 34, ID_MC_NEUTRAL);
  Button(L"2 · Grande ouverture", L"2 · Wide open", X + COLX, 380, COLW, 34, ID_MC_OPEN);
  Button(L"3 · Sourire large", L"3 · Wide smile", X + COLX, 424, COLW, 34, ID_MC_WIDE);
  Button(L"4 · Bouche serrée", L"4 · Pursed lips", X + COLX, 468, COLW, 34, ID_MC_NARROW);
  Button(L"Effacer l'étalonnage", L"Clear the calibration", X + COLX, 512, COLW, 34, ID_MC_CLEAR);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX, X + PAD, 486, PVW, 56, ID_MOUTHSTAT);
  Label(L"Clique sur une étape, puis prends l'expression dans la seconde qui suit et garde-la 1,5 s. Les étapes 1 et 2 suffisent pour l'ouverture de la bouche ; "
        L"3 et 4 règlent la largeur. La détection repère la cavité sombre de la bouche : une caméra proche, bien éclairée (ou infrarouge), cadrée sur la bouche. "
        L"La zone analysée se règle dans ar6dof_mouth.txt. AVEC LE MODÈLE IA (fichier babble_model.onnx à côté de l'exe, 45 paramètres) : "
        L"l'étape 1 = visage neutre, les étapes 2 à 4 = 8 s d'expressions libres (bouche grande ouverte, sourire, moue, langue, joues…).",
        L"Click a step, then make the expression within a second and hold it for 1.5 s. Steps 1 and 2 are enough for mouth opening; "
        L"3 and 4 tune the width. Detection finds the dark cavity of the mouth: use a close, well-lit (or infrared) camera framing the mouth. "
        L"The analysed area is set in ar6dof_mouth.txt. WITH THE AI MODEL (babble_model.onnx next to the exe, 45 parameters): "
        L"step 1 = neutral face, steps 2 to 4 = 8 s of free expressions (wide open, smile, pout, tongue, cheeks…).",
        X + PAD, 556, CWd - 2 * PAD, 112);

  // ---- page 5 : Suivi
  g_buildPage = PG_TRACK;
  CardBox(L"Suivi 6DoF", L"6DoF tracking", X, 62, CWd, 188);
  Mk(L"BUTTON", L"▶  Démarrer le suivi", L"▶  Start tracking", BS_OWNERDRAW | WS_TABSTOP, X + PAD, 106, 300, 38, ID_START);
  Button(L"Redémarrer le suivi", L"Restart tracking", X + PAD, 158, 208, 34, ID_A_RESTART);
  Button(L"Verrouiller le sol", L"Lock the floor", X + PAD + 218, 158, 208, 34, ID_A_FLOOR);
  Button(L"Cap à zéro", L"Zero the heading", X + PAD + 436, 158, 208, 34, ID_A_YAW);
  Button(L"Inverser gauche / droite", L"Swap left / right", X + PAD, 202, 208, 34, ID_A_SWAP);
  Button(L"Vérifier la calibration", L"Check the calibration", X + PAD + 218, 202, 208, 34, ID_A_CHECK);

  CardBox(L"Vue du suivi", L"Tracking view", X, 264, CWd, 326);
  Mk(L"AR6Preview", nullptr, nullptr, 0, X + 26, 310, 640, 240, ID_PVW_TRK);
  Mk(L"STATIC", nullptr, nullptr, SS_LEFT | SS_NOPREFIX | SS_ENDELLIPSIS, X + PAD, 558, CWd - 2 * PAD, 20, ID_STATS);

  // ---- page 6 : Journal (les messages d'ar6dof_probe, sur toute la page)
  g_buildPage = PG_LOG;
  CardBox(L"Journal", L"Log", X, 62, CWd, 628);
  Mk(L"EDIT", nullptr, nullptr, ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL, X + PAD, 106, CWd - 2 * PAD, 566, ID_LOG, WS_EX_CLIENTEDGE);

  // ---- page 7 : Avancé
  g_buildPage = PG_ADV;
  CardBox(L"Avancé", L"Advanced", X, 62, CWd, 130);
  Label(L"Threads de calcul", L"Compute threads", X + PAD, 104, 160);
  Edit(X + C0, 100, 60, ID_THREADS, true);
  Check(L"Laisser tourner quand je ferme cette fenêtre", L"Keep running when I close this window", X + PAD, 144, CWd - 2 * PAD, ID_KEEP);

  CardBox(L"Lecture de la caméra", L"Camera reading", X, 206, CWd, 128);
  Label(L"Pilote caméra", L"Camera driver", X + PAD, 252, 160);
  Combo(X + C0, 248, CR, ID_BACKEND);
  Check(L"Lecteur direct Media Foundation (moins de latence et de CPU)", L"Direct Media Foundation reader (lower latency and CPU)", X + PAD, 288, CWd - 2 * PAD, ID_MFDIRECT);

  g_buildPage = -1;
}

// Changement de page : l'aperçu de l'ancienne page s'arrête (plus aucune demande envoyée, la sonde le coupe seule), celui de la nouvelle reprend s'il était activé.
void OnPageChanged() {
  CalBackground();               // une calibration laissée ouverte sur une autre page est annulée : le suivi reprend
  if (g_page == PG_CALIB) CalPoll(true);
  OnFastTick();
  if (g_page == PG_SENSORS && GetSel(ID_PORT) > 0) EnsureProbe();   // lecture de l'IMU en direct dans la page Capteurs
  UpdateState();
}

LRESULT CALLBACK WndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
  switch (m) {
    case WM_ERASEBKGND:
      return 1;   // tout est dessiné par WM_PAINT (double tampon) : pas de scintillement
    case WM_PAINT: {
      PAINTSTRUCT ps;
      HDC dc = BeginPaint(h, &ps);
      RECT rc;
      GetClientRect(h, &rc);
      HDC mem = CreateCompatibleDC(dc);
      HBITMAP bmp = CreateCompatibleBitmap(dc, rc.right, rc.bottom);
      HGDIOBJ old = SelectObject(mem, bmp);
      FillRect(mem, &rc, g_brBg);
      PaintChrome(mem);
      BitBlt(dc, 0, 0, rc.right, rc.bottom, mem, 0, 0, SRCCOPY);
      SelectObject(mem, old);
      DeleteObject(bmp);
      DeleteDC(mem);
      EndPaint(h, &ps);
      return 0;
    }
    case WM_DRAWITEM:
      DrawOwner(reinterpret_cast<const DRAWITEMSTRUCT*>(l));
      return TRUE;
    case WM_CTLCOLORSTATIC:
    case WM_CTLCOLOREDIT:
    case WM_CTLCOLORLISTBOX:
    case WM_CTLCOLORBTN: {
      HDC dc = (HDC)w;
      HWND ctl = (HWND)l;
      wchar_t cls[16] = L"";
      GetClassNameW(ctl, cls, 16);
      bool field = (m == WM_CTLCOLOREDIT || m == WM_CTLCOLORLISTBOX || _wcsicmp(cls, L"Edit") == 0);   // champ de saisie, liste, journal (EDIT en lecture seule)
      SetTextColor(dc, IsWindowEnabled(ctl) ? g_pal.text : g_pal.dim);
      if (field) {
        SetBkColor(dc, g_pal.field);
        return (LRESULT)g_brField;
      }
      bool card = OnCard(ctl);
      SetBkColor(dc, card ? g_pal.card : g_pal.bg);
      return (LRESULT)(card ? g_brCard : g_brBg);
    }
    case WM_SETTINGCHANGE:
      if (l && lstrcmpW((LPCWSTR)l, L"ImmersiveColorSet") == 0) ApplyTheme();   // mode sombre / clair ou couleur d'accentuation changés pendant que l'application tourne
      break;
    case WM_COMMAND: {
      int id = LOWORD(w), code = HIWORD(w);
      if (code == BN_CLICKED && id >= ID_NAV0 && id < ID_NAV0 + kNavCount) { ShowPage(id - ID_NAV0); OnPageChanged(); return 0; }
      if (code == BN_CLICKED && GetPropW(H(id), L"ischk")) SetChecked(id, !IsChecked(id));   // cases dessinées par nous : on bascule l'état ici
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
        case ID_CAM_REFRESH: case ID_CAMR_REFRESH: case ID_EYEL_REFRESH: case ID_EYER_REFRESH: case ID_MOUTH_REFRESH:
          if (code == BN_CLICKED) RefreshCameras();
          break;
        case ID_W: case ID_H: case ID_FPS:
          if (code == EN_CHANGE) OnSizeEdited();
          break;
        case ID_EYE:
          if (code == BN_CLICKED) UpdateEyeEnable();
          break;
        case ID_MOUTH:
          if (code == BN_CLICKED) UpdateMouthEnable();
          break;
        case ID_PV_CAM: TogglePreview(0); UpdateState(); break;
        case ID_PV_EYE: TogglePreview(1); UpdateState(); break;
        case ID_PV_MOUTH: TogglePreview(2); UpdateState(); break;
        case ID_EYE_CAL: StartEyeCal(); break;
        case ID_EYE_RECENTER: QueueUdp(9884, "RECENTER\n", EnsureProbe()); break;
        case ID_EYE_CLEAR: QueueUdp(9884, "CAL_CLEAR\n", EnsureProbe()); break;
        case ID_MC_NEUTRAL: MouthCal("neutral"); break;
        case ID_MC_OPEN: MouthCal("open"); break;
        case ID_MC_WIDE: MouthCal("wide"); break;
        case ID_MC_NARROW: MouthCal("narrow"); break;
        case ID_MC_CLEAR: QueueUdp(9887, "MCAL_CLEAR\n", EnsureProbe()); break;
        case ID_PORT_REFRESH:
          ScanPorts();
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
            ScanProbes();
            if (g_procs.svc) SendCmd('Q');   // le mode services relit la configuration : il est relancé tout seul si une page en a besoin
          }
          break;
        case ID_START:
          if (g_running) StopProbe(); else StartTracking();
          UpdateState();
          break;
        case ID_A_RESTART: SendCmd('T'); break;
        case ID_A_FLOOR: SendCmd('V'); break;
        case ID_A_YAW: SendCmd('Z'); break;
        case ID_A_SWAP: SendCmd('x'); break;
        case ID_A_CHECK: SendCmd('c'); break;
        case ID_CAL_START: if (code == BN_CLICKED) CalStart(); break;
        case ID_CAL_IMPORT:
          if (code == BN_CLICKED && g_procs.full) {
            SendUdp(9877, "CALIMPORT");
            g_cp.note = Tr(L"Import demandé : le résultat est dans le journal (page Journal).", L"Import requested: the result is in the log (Log page).");
            CalUpdateUi();
          }
          break;
        case ID_CAL_PRESET:
          if (code == CBN_SELCHANGE) CalUpdateUi();
          break;
        case ID_CAL_TOTAL:
          if (code == EN_CHANGE && !g_loading) CalUpdateUi();
          break;
        case ID_CZ_AUTO: SendUdp(9877, IsChecked(ID_CZ_AUTO) ? "CALZOOMAUTO 1" : "CALZOOMAUTO 0"); break;
        case ID_CZ_SET:
          if (code == BN_CLICKED) {
            double z = 0;
            if (ParseMm(ID_CZ_VAL, 0.25, 4.0, &z)) {
              char b[48];
              std::snprintf(b, sizeof(b), "CALZOOM %.3f", z);
              SendUdp(9877, b);
            } else {
              MessageBoxW(g_wnd, Tr(L"Le zoom du mode doit être compris entre 0,25 et 4 (1 = les focales calculées sont justes).", L"The mode zoom must be between 0.25 and 4 (1 = the computed focal lengths are right)."),
                          Tr(L"Zoom du mode", L"Mode zoom"), MB_OK | MB_ICONWARNING);
            }
          }
          break;
        case ID_CZ_RESET: if (code == BN_CLICKED) SendUdp(9877, "CALZOOMRESET"); break;
        case ID_CAL_CALC: if (code == BN_CLICKED) SendUdp(9877, "CALCOMPUTE"); break;
        case ID_CAL_SWAP: if (code == BN_CLICKED) SendCmd('x'); break;
        case ID_CAL_CANCEL: case ID_CAL_REDO:
          if (code == BN_CLICKED) { g_cp.wantVerify = false; SendUdp(9877, "CALCANCEL"); }
          break;
        case ID_CAL_APPLY: if (code == BN_CLICKED) SendUdp(9877, "CALAPPLY"); break;
        case ID_CAL_TOVERIFY: if (code == BN_CLICKED) { g_cp.wantVerify = true; g_cp.verifySentAt = 0; CalPoll(true); } break;
        case ID_CAL_VMEASURE: if (code == BN_CLICKED) CalMeasure(); break;
        case ID_CAL_VFIX:
          if (code == BN_CLICKED) {
            const double ratio = g_cp.st.verifyRatio;
            if (ratio > 0.3 && ratio < 3.0) {
              char b[48];
              std::snprintf(b, sizeof(b), "CALSCALE %.5f", ratio);
              SendUdp(9877, b);
              SendUdp(9877, "CALAPPLY");
              g_cp.verifySentSeq = g_cp.st.verifySeq;
              g_cp.verifySentAt = 0;
            }
          }
          break;
        case ID_CAL_VBACK: if (code == BN_CLICKED) { g_cp.wantVerify = false; CalPoll(true); } break;
        case ID_M_STEP1: QueueUdp(9877, "MOUNT", EnsureProbe()); break;    // fonctionne sans suivi : le mode services lit l'IMU
        case ID_M_STEP2: QueueUdp(9877, "MOUNTX", EnsureProbe()); break;
        case ID_M_RESET: QueueUdp(9877, "0", EnsureProbe()); break;
        case ID_M_FLIP: QueueUdp(9877, "MOUNTFLIP", EnsureProbe()); break;
      }
      return 0;
    }
    case WM_TIMER:
      if (w == 2) {   // 15 fois par seconde : ne fait rien tant qu'aucun aperçu n'est demandé
        OnFastTick();
        if (g_page == PG_CALIB || g_cp.starting) CalPoll();
        if (g_startAfterSvc) { static int n = 0; if (++n % 4 == 0) UpdateState(); }
        return 0;
      }
      RefreshLog();
      UpdateState();
      CalBackground();
      return 0;
    case WM_CLOSE:
      if (g_cal.active) EndEyeCal(true);
      CalCancelIfOpen(true);
      if (g_running && !IsChecked(ID_KEEP) && g_ourProc) SendCmd('Q');   // arrêt propre du programme que cette fenêtre a lancé
      else if (g_procs.svc) SendCmd('Q');                                 // et du mode services
      DestroyWindow(h);
      return 0;
    case WM_DESTROY:
      ClosePreview();
      KillTimer(h, 1);
      KillTimer(h, 2);
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
  // La fenêtre est grande (aperçus des caméras) : si elle ne tient pas dans l'écran à cette échelle, tout est réduit proportionnellement.
  double fit = 1.0;
  {
    RECT wa;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0);
    const double needH = MulDiv(kClientH, g_dpi, 96) + 48, needW = MulDiv(kClientW, g_dpi, 96) + 16;
    fit = std::min(1.0, std::min((wa.bottom - wa.top) / needH, (wa.right - wa.left) / needW));
    if (fit < 1.0) g_dpi = std::max(48, (int)(g_dpi * fit));
  }

  NONCLIENTMETRICSW ncm;
  memset(&ncm, 0, sizeof(ncm));
  ncm.cbSize = sizeof(ncm);
  SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0);
  ncm.lfMessageFont.lfHeight = (LONG)(ncm.lfMessageFont.lfHeight * fit);
  g_font = CreateFontIndirectW(&ncm.lfMessageFont);
  {
    LOGFONTW lf = ncm.lfMessageFont;
    lf.lfHeight = lf.lfHeight * 19 / 10;     // titre de la page
    lf.lfWeight = FW_SEMIBOLD;
    g_fontBig = CreateFontIndirectW(&lf);
    lf = ncm.lfMessageFont;
    lf.lfHeight = lf.lfHeight * 12 / 10;     // titre d'une carte
    lf.lfWeight = FW_SEMIBOLD;
    g_fontTitle = CreateFontIndirectW(&lf);
    lf = ncm.lfMessageFont;
    lf.lfHeight = lf.lfHeight * 12 / 10;     // icônes de la navigation
    lf.lfWeight = FW_NORMAL;
    const wchar_t* face = FontExists(L"Segoe Fluent Icons") ? L"Segoe Fluent Icons" : FontExists(L"Segoe MDL2 Assets") ? L"Segoe MDL2 Assets" : nullptr;
    if (face) {
      lstrcpynW(lf.lfFaceName, face, 32);
      g_fontIcon = CreateFontIndirectW(&lf);
    }
  }

  LoadCfg();
  std::string lang = CfgStr("app_lang", "");
  if (lang.empty()) g_en = (PRIMARYLANGID(GetUserDefaultUILanguage()) != LANG_FRENCH);
  else g_en = (lang == "en");

  WNDCLASSW wc;
  memset(&wc, 0, sizeof(wc));
  wc.lpfnWndProc = WndProc;
  wc.hInstance = inst;
  wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
  wc.hbrBackground = nullptr;   // fond dessiné par WM_PAINT
  wc.style = CS_HREDRAW | CS_VREDRAW;
  wc.lpszClassName = L"AR6DoFApp";
  wc.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
  RegisterClassW(&wc);
  {   // aperçu des caméras et fenêtre plein écran d'étalonnage des yeux
    WNDCLASSW pc;
    memset(&pc, 0, sizeof(pc));
    pc.lpfnWndProc = PvProc;
    pc.hInstance = inst;
    pc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    pc.lpszClassName = L"AR6Preview";
    RegisterClassW(&pc);
    WNDCLASSW cc;
    memset(&cc, 0, sizeof(cc));
    cc.lpfnWndProc = CalProc;
    cc.hInstance = inst;
    cc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    cc.lpszClassName = L"AR6Cal";
    RegisterClassW(&cc);
    WNDCLASSW gc;   // page Calibration : barre de progression, aperçu, mires dessinées
    memset(&gc, 0, sizeof(gc));
    gc.lpfnWndProc = CalGfxProc;
    gc.hInstance = inst;
    gc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    gc.lpszClassName = L"AR6CalGfx";
    RegisterClassW(&gc);
  }

  const DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_CLIPCHILDREN;
  RECT r = {0, 0, S(kClientW), S(kClientH)};
  AdjustWindowRect(&r, style, FALSE);
  g_wnd = CreateWindowExW(0, wc.lpszClassName, Tr(L"AR6DoF — Centre de contrôle", L"AR6DoF — Control center"), style, CW_USEDEFAULT, CW_USEDEFAULT,
                          r.right - r.left, r.bottom - r.top, nullptr, nullptr, inst, nullptr);
  if (!g_wnd) return 1;

  CreateControls();
  SetWindowLongPtrW(H(ID_PVW_CAM), GWLP_USERDATA, 1);
  SetWindowLongPtrW(H(ID_PVW_EYE), GWLP_USERDATA, 2);
  SetWindowLongPtrW(H(ID_PVW_MOUTH), GWLP_USERDATA, 3);
  SetWindowLongPtrW(H(ID_PVW_IMU), GWLP_USERDATA, 4);
  SetWindowLongPtrW(H(ID_PVW_TRK), GWLP_USERDATA, 5);
  SetWindowLongPtrW(H(ID_CAL_GFX_BAR), GWLP_USERDATA, 1);    // genre de dessin de chaque fenêtre de la page Calibration (voir CalGfxProc)
  SetWindowLongPtrW(H(ID_CAL_GFX_PV), GWLP_USERDATA, 2);
  SetWindowLongPtrW(H(ID_CAL_GFX_TIP), GWLP_USERDATA, 3);
  SetWindowLongPtrW(H(ID_CAL_GFX_GOALS), GWLP_USERDATA, 4);
  SetWindowLongPtrW(H(ID_CAL_GFX_EX), GWLP_USERDATA, 5);
  SetWindowLongPtrW(H(ID_CAL_GFX_PV2), GWLP_USERDATA, 6);
  SetWindowLongPtrW(H(ID_CAL_GFX_VB), GWLP_USERDATA, 7);
  {
    int np = 0;
    const calview::BoardPreset* bp = calview::Presets(&np);
    for (int i = 0; i < np; i++) AddItem(ID_CAL_PRESET, std::to_wstring(bp[i].sx) + L" × " + std::to_wstring(bp[i].sy));
    SetSel(ID_CAL_PRESET, 0);
  }
  ApplyTheme();
  ShowPage(0);
  // liste de langue
  AddItem(ID_LANG, L"Français");
  AddItem(ID_LANG, L"English");
  SetSel(ID_LANG, g_en ? 1 : 0);

  ScanDevices();
  g_loading = true;
  FillCamTypes();
  FillBackends(ID_BACKEND);
  FillBackends(ID_EYEBACK);
  FillBackends(ID_MOUTHBACK);
  FillCams(ID_CAM, false);
  FillCams(ID_CAMR, false);
  FillCams(ID_EYEL, true);
  FillCams(ID_EYER, true);
  FillCams(ID_MOUTHCAM, true);
  FillPorts();
  g_loading = false;
  ConfigToFields();
  SendMessageW(H(ID_THREADS), EM_SETLIMITTEXT, 2, 0);
  UpdateState();
  CalUpdateUi();
  RefreshLog();
  SetTimer(g_wnd, 1, 700, nullptr);
  SetTimer(g_wnd, 2, 66, nullptr);

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
