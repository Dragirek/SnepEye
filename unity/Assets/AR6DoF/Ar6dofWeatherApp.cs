using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Application « Météo » : la météo de ta position (ou d'une ville, ou de coordonnées exactes) avec un petit monde 3D animé en style dessin animé
    /// (soleil et lune qui suivent l'heure, nuages, pluie, neige qui recouvre tout, brouillard, orage, vent qui plie les arbres et fait tourner le moulin),
    /// les prévisions sur 7 jours et sur 24 heures, et un curseur pour avancer dans le temps d'heure en heure (le temps s'accélère un instant à chaque heure).
    /// Page Plus (ex-onglet Test, cachée par défaut) : tous les types de temps, l'heure, le vent et la température à la main. Roue « Options » : montrer / cacher les cases et les effets.
    /// Données : Open-Meteo.com (gratuit, sans clé, tous les pays). Rien ne tourne et rien n'est téléchargé tant que la fenêtre n'est pas ouverte.
    /// </summary>
    public class Ar6dofWeatherApp : Ar6dofAppWindow
    {
        static readonly string[] TabNamesBase = { "Maintenant", "Jours", "Heures", "Lieu" };
        static readonly string[] TabNamesPlus = { "Maintenant", "Jours", "Heures", "Lieu", "Plus" };   // « Plus » (ex-Test) : cachée tant qu'on ne l'active pas dans Options
        const int TabNow = 0, TabDays = 1, TabHours = 2, TabPlace = 3, TabTest = 4, TabOptions = 5;   // Options : page de la roue, hors de la barre d'onglets
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly float[] FrameH = { 300f, 340f, 380f, 440f, 520f };        // hauteur du cadre bleu de la scène, par niveau (le niveau 2, 380 px, est celui par défaut)
        static readonly float[] DropBy = { 0.14f, 0.28f, 0.42f, 0.54f, 0.66f };   // de combien l'île descend (en tailles de scène), par niveau ; la scène ne rétrécit plus, la pointe peut sortir du cadre
        static readonly float[] CloudBy = { -0.40f, -0.25f, -0.10f, 0.02f, 0.15f };  // de combien les nuages (et la pluie, la neige, les éclairs) montent, par niveau
        static readonly string[] CloudNames = { "Tres bas", "Bas", "Moyen", "Haut", "Tres haut" };   // par defaut : tres haut
        static readonly string[] DropNames = { "Comme avant", "Un peu plus bas", "Plus bas", "Bas", "Tres bas" };
        int DropLevel { get { return wx.prefs.dropSel <= 0 ? 2 : Mathf.Clamp(wx.prefs.dropSel - 1, 0, 4); } }
        int CloudLevel { get { return wx.prefs.cloudSel <= 0 ? 4 : Mathf.Clamp(wx.prefs.cloudSel - 1, 0, 4); } }
        int FrameLevel { get { return wx.prefs.frameSel <= 0 ? 2 : Mathf.Clamp(wx.prefs.frameSel - 1, 0, 4); } }
        const float PlayStep = 2.6f;     // secondes entre deux heures en lecture
        const float BurstTime = 0.9f;    // durée de l'accélération du temps entre deux heures
        const int TileCount = 10;
        static readonly string[] TileNames = { "Vent", "Rafales", "Humidite", "Pression", "Nuages", "Pluie maintenant", "Pluie aujourd'hui", "Indice UV", "Lever / coucher", "Min / max aujourd'hui" };
        // masque « caché » (WxPrefs.off) : bits 0..9 = les cases ci-dessus, puis :
        const int OptFeels = 10, OptScrub = 11, OptWindFx = 12, OptWindLine = 13;

        struct Now
        {
            public WxCond cond;
            public bool day;
            public float temp, feels, wind, windDir, gust, humidity, pressure, precip, cover;
            public string time;        // « 2026-11-05T14:00 » ("" en mode test)
            public int dayIdx;         // jour correspondant dans les prévisions quotidiennes
            public float sunF, moonF;  // avancement du jour / de la nuit (0..1), -1 si inconnu
        }

        int tab;
        string query = "";
        string coordText = "";
        bool coordEdited, coordErr;
        bool loaded;
        float nextTry;
        readonly Ar6dofWeatherClient wx = new Ar6dofWeatherClient();

        // scène et textes dans les coins de l'île
        Ar6dofWeatherScene scene;
        Ar6dofSkyGraphic sky;
        Color skyTop, skyBot;
        float lastFlash = -1f;
        Text ageText, tTemp, tFeels, tCond, tWindT;

        // cases d'information
        readonly Text[] tileVal = new Text[TileCount];
        readonly Text[] tileTitle = new Text[TileCount];

        // avancer dans le temps
        int hourOffset;                  // 0 = maintenant, 1 = dans 1 h...
        float animFrom, animTo, animT = 1f;
        bool playing;
        float playTimer;
        Text timeLabel, playLabel;

        // page Plus
        int tType = 6;
        float tHour = 12f, tWind = 12f, tTemp0 = 14f;
        bool tAuto;
        float tAutoTimer;
        readonly List<Button> typeBtns = new List<Button>();
        float rowCy;

        // ce qui a été affiché : si la réalité change (réponse reçue, erreur...), la fenêtre est reconstruite
        float seenFetch = -2f;
        int seenPhase = -1, seenResults = -1;
        string seenErr = "", seenStatus = "", seenPlace = "", seenHint = "";
        bool seenSearched;

        public Ar6dofWeatherApp(Ar6dofDock d) : base(d, "weather", "Meteo", "weather", "Meteo de ta position en 3D : soleil, nuages, pluie, neige, vent et previsions.", 660f) { }

        protected override void OnRebuildStart()
        {
            scene = null; sky = null; ageText = null; lastFlash = -1f;
            tTemp = tFeels = tCond = tWindT = null;
            timeLabel = playLabel = null;
            typeBtns.Clear();
            Array.Clear(tileVal, 0, tileVal.Length);
            Array.Clear(tileTitle, 0, tileTitle.Length);
        }

        // ------------------------------------------------------------------ options (cases cachées)
        bool Hidden(int bit) { return (wx.prefs.off & (1 << bit)) != 0; }

        void SetHidden(int bit, bool hide)
        {
            if (hide) wx.prefs.off |= 1 << bit; else wx.prefs.off &= ~(1 << bit);
            wx.Save();
        }

        static string OptName(int bit)
        {
            if (bit < TileCount) return TileNames[bit];
            switch (bit)
            {
                case OptFeels: return "Ressenti";
                case OptScrub: return "Avancer dans le temps";
                case OptWindFx: return "Effets de vent";
                default: return "Vent dans le coin";
            }
        }

        // ------------------------------------------------------------------ vie de la fenêtre
        protected override void OnVisible(bool on)
        {
            if (!on) return;
            playing = false;
            if (!loaded) { wx.Load(); loaded = true; Rebuild(); }
            nextTry = 0f;
            MaybeRefresh();
        }

        public override void OnClosed() { wx.Cancel(); StopAuto(); }

        void StopAuto() { playing = false; tAuto = false; SetPlayLabel(); }

        void MaybeRefresh()
        {
            if (wx.Busy || Time.unscaledTime < nextTry) return;
            if (wx.prefs.place.valid)
            {
                if (wx.HasData && wx.AgeSeconds < 900f) return;
                nextTry = Time.unscaledTime + 60f;
                wx.Refresh();
            }
            else
            {
                nextTry = Time.unscaledTime + 60f;
                wx.UseIp();
            }
        }

        void ForceRefresh()
        {
            wx.Cancel();
            wx.error = "";
            nextTry = Time.unscaledTime + 60f;
            if (wx.prefs.place.valid) wx.Refresh(); else wx.UseIp();
        }

        protected override void OnTick()
        {
            wx.Poll();
            MaybeRefresh();
            if (tab < TabTest &&
                (wx.fetchedAt != seenFetch || (int)wx.phase != seenPhase || wx.error != seenErr || wx.status != seenStatus || wx.hint != seenHint ||
                 wx.prefs.place.name != seenPlace || wx.results.Count != seenResults || wx.searched != seenSearched))
            {
                Rebuild();
                return;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            TickScrub(dt);
            TickAuto(dt);
            // éclair : le ciel s'éclaircit un instant
            if (scene != null && sky != null)
            {
                float f = scene.Flash;
                if (Mathf.Abs(f - lastFlash) > 0.02f)
                {
                    lastFlash = f;
                    sky.SetColors(Color.Lerp(skyTop, Color.white, f * 0.55f), Color.Lerp(skyBot, Color.white, f * 0.55f), 26f);
                }
            }
        }

        // ------------------------------------------------------------------ unités
        string Temp(float c) { return Mathf.RoundToInt(wx.Imperial ? c * 1.8f + 32f : c).ToString(Inv) + "°"; }
        string TempUnit(float c) { return Temp(c) + (wx.Imperial ? "F" : "C"); }
        string Speed(float kmh) { return wx.Imperial ? Mathf.RoundToInt(kmh * 0.621371f).ToString(Inv) + " mph" : Mathf.RoundToInt(kmh).ToString(Inv) + " km/h"; }
        string Precip(float mm) { return wx.Imperial ? (mm * 0.0393701f).ToString("0.00", Inv) + " in" : mm.ToString("0.0", Inv) + " mm"; }
        string Pressure(float hpa) { return wx.Imperial ? (hpa * 0.02953f).ToString("0.00", Inv) + " inHg" : Mathf.RoundToInt(hpa).ToString(Inv) + " hPa"; }
        static string Percent(float v) { return Mathf.RoundToInt(v).ToString(Inv) + " %"; }

        static float At(float[] a, int i, float def) { return a != null && i >= 0 && i < a.Length ? a[i] : def; }
        static int AtI(int[] a, int i, int def) { return a != null && i >= 0 && i < a.Length ? a[i] : def; }
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        string AgeString()
        {
            int m = Mathf.FloorToInt(wx.AgeSeconds / 60f);
            return m < 1 ? Ar6dofLoc.T("Mis a jour a l'instant") : Ar6dofLoc.F("Mis a jour il y a {0} min", m);
        }

        // ------------------------------------------------------------------ couleurs du ciel
        static void SkyColors(WxCond c, bool day, out Color top, out Color bot, float tempC = 15f)
        {
            float g, dk;
            switch (c)
            {
                case WxCond.Clear: g = 0f; dk = 0f; break;
                case WxCond.MostlyClear: g = 0.05f; dk = 0f; break;
                case WxCond.PartlyCloudy: g = 0.2f; dk = 0f; break;
                case WxCond.Overcast: g = 0.6f; dk = 0.1f; break;
                case WxCond.Fog: g = 0.6f; dk = 0f; break;
                case WxCond.Drizzle: g = 0.6f; dk = 0.15f; break;
                case WxCond.Rain: g = 0.7f; dk = 0.3f; break;
                case WxCond.HeavyRain: g = 0.8f; dk = 0.5f; break;
                case WxCond.Showers: g = 0.65f; dk = 0.25f; break;
                case WxCond.FreezingRain: g = 0.7f; dk = 0.3f; break;
                case WxCond.Snow: g = 0.55f; dk = 0.05f; break;
                case WxCond.HeavySnow: g = 0.65f; dk = 0.15f; break;
                case WxCond.SnowShowers: g = 0.5f; dk = 0.1f; break;
                case WxCond.Thunder: g = 0.9f; dk = 0.8f; break;
                default: g = 0.85f; dk = 0.7f; break;
            }
            Color ct, cb, gt, gb;
            if (day)
            {
                ct = new Color(0.28f, 0.56f, 0.92f); cb = new Color(0.64f, 0.85f, 0.98f);
                gt = Color.Lerp(new Color(0.50f, 0.55f, 0.64f), new Color(0.20f, 0.22f, 0.30f), dk);
                gb = Color.Lerp(new Color(0.72f, 0.76f, 0.82f), new Color(0.38f, 0.40f, 0.50f), dk);
            }
            else
            {
                ct = new Color(0.03f, 0.05f, 0.15f); cb = new Color(0.12f, 0.16f, 0.36f);
                gt = Color.Lerp(new Color(0.09f, 0.10f, 0.17f), new Color(0.04f, 0.05f, 0.09f), dk);
                gb = Color.Lerp(new Color(0.20f, 0.22f, 0.31f), new Color(0.10f, 0.11f, 0.18f), dk);
            }
            top = Color.Lerp(ct, gt, g);
            bot = Color.Lerp(cb, gb, g);
            // canicule : l'horizon vire à l'orange par grand soleil
            if (day && tempC >= 28f && g < 0.4f) bot = Color.Lerp(bot, new Color(1f, 0.78f, 0.50f), Mathf.Clamp01((tempC - 28f) / 12f) * 0.65f);
        }

        // ------------------------------------------------------------------ petits éléments
        Text L(Transform parent, string text, float x, float y, float w, float h, int fs, TextAnchor a = TextAnchor.MiddleCenter)
        {
            return Ar6dofVrUi.Label(parent, text, new Vector2(x, y), new Vector2(w, h), fs, a);
        }

        RectTransform Box(Transform parent, Vector2 pos, Vector2 size, Ar6dofRole role, int radius)
        {
            var rt = Ar6dofVrUi.Rect(parent, "Cadre", pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            Ar6dofTheme.Attach(rt.gameObject, role, radius);
            return rt;
        }

        void Icon(Transform parent, Vector2 pos, float size, WxCond c, bool day)
        {
            var tile = Ar6dofVrUi.Rect(parent, "Icone", pos, new Vector2(size, size));
            var img = tile.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            Color t, b;
            SkyColors(c, day, out t, out b);
            img.color = Color.Lerp(t, b, 0.5f);
            Ar6dofTheme.SetRounded(img, Mathf.RoundToInt(size * 0.28f));
            var g = Ar6dofVrUi.Rect(tile, "Dessin", Vector2.zero, new Vector2(size, size));
            var ic = g.gameObject.AddComponent<Ar6dofWxIcon>();
            ic.raycastTarget = false;
            ic.Set(c, day);
        }

        // ------------------------------------------------------------------ pages
        protected override void BuildMain()
        {
            if (tab == TabOptions) { BuildOptions(); return; }
            // roue des options, à droite du titre
            Text gl;
            BtnAt(IW * 0.5f - 62f, -38f, 124f, 40f, "Options", () => { StopAuto(); tab = TabOptions; Rebuild(); }, out gl, 19);
            Tabs(wx.prefs.plus != 0 ? TabNamesPlus : TabNamesBase, tab, t => { tab = t; StopAuto(); });
            Remember();
            if (tab != TabTest) PlaceLine();
            switch (tab)
            {
                case TabNow: BuildNow(); break;
                case TabDays: BuildDays(); break;
                case TabHours: BuildHours(); break;
                case TabPlace: BuildPlace(); break;
                default: BuildTest(); break;
            }
        }

        void Remember()
        {
            seenFetch = wx.fetchedAt;
            seenPhase = (int)wx.phase;
            seenErr = wx.error;
            seenStatus = wx.status;
            seenHint = wx.hint;
            seenPlace = wx.prefs.place.name;
            seenResults = wx.results.Count;
            seenSearched = wx.searched;
        }

        string SourceText(WxPlace p)
        {
            string key;
            switch (p.source)
            {
                case "windows": key = "Source : Windows (Wi-Fi / GPS)"; break;
                case "manual": key = "Source : position saisie"; break;
                case "search": key = "Source : ville choisie"; break;
                default: key = "Ville estimee par internet (peut etre fausse)"; break;   // « ip » et les anciens enregistrements sans source
            }
            return Ar6dofLoc.T(key);   // jamais de coordonnees affichees : un cadre d'ecran ou un stream ne doit pas reveler ou tu habites
        }

        void PlaceLine()
        {
            var p = wx.prefs.place;
            string nm = !p.valid ? "Position inconnue" : (string.IsNullOrEmpty(p.country) || p.country == p.name ? p.name : p.name + ", " + p.country);
            var t = Lbl(nm, 34, 24);
            t.fontStyle = FontStyle.Bold;
            if (p.valid && (tab == TabNow || tab == TabPlace)) Lbl(SourceText(p), 24, 16);
            if (p.valid && tab == TabNow && (p.source == "ip" || string.IsNullOrEmpty(p.source)))
                Btn("Ce n'est pas ma ville ? Choisir ma ville", () => { tab = TabPlace; Rebuild(); }, 40, 18);
            if (wx.Busy)
            {
                string s = !string.IsNullOrEmpty(wx.status) ? wx.status : (wx.phase == Ar6dofWeatherClient.Phase.Searching ? "Recherche..." : "Chargement de la meteo...");
                Lbl(s, 28, 19);
            }
            else if (!string.IsNullOrEmpty(wx.error)) Para(wx.error, 52, 18);
            else if (wx.HasData && tab != TabPlace)
            {
                ageText = Lbl("", 26, 17);
                var at = ageText;
                refreshers.Add(() => { if (at != null) at.text = AgeString(); });
            }
            if (tab == TabPlace && !wx.Busy && !string.IsNullOrEmpty(wx.hint)) Para(wx.hint, 52, 17);
        }

        void NoData()
        {
            if (wx.Busy) return;
            Para("Pas encore de meteo. Verifie ta connexion internet ou choisis ta ville dans l'onglet Lieu.", 76, 19);
            Btn("Reessayer", () => { ForceRefresh(); Rebuild(); }, 52, 22);
        }

        // ------------------------------------------------------------------ données « maintenant » / heure choisie / test
        int HourBase()
        {
            if (!wx.HasData) return -1;
            var h = wx.data.hourly;
            if (h == null || h.time == null || h.time.Length == 0) return -1;
            string cur = wx.data.current.time ?? "";
            if (cur.Length >= 13)
            {
                string pre = cur.Substring(0, 13);
                for (int i = 0; i < h.time.Length; i++) if (h.time[i].StartsWith(pre, StringComparison.Ordinal)) return i;
            }
            return 0;
        }

        int MaxHours()
        {
            int b = HourBase();
            return b < 0 ? 0 : Mathf.Clamp(wx.data.hourly.time.Length - 1 - b, 0, 96);
        }

        static float Mins(string hm)
        {
            int h, m;
            if (string.IsNullOrEmpty(hm) || hm.Length < 5) return -1f;
            if (!int.TryParse(hm.Substring(0, 2), NumberStyles.Integer, Inv, out h) || !int.TryParse(hm.Substring(3, 2), NumberStyles.Integer, Inv, out m)) return -1f;
            return h * 60f + m;
        }

        /// <summary>Avancement du jour (soleil) ou de la nuit (lune) à cette heure, d'après le lever / coucher du jour.</summary>
        void Fracs(ref Now n)
        {
            n.sunF = n.moonF = -1f;
            if (!wx.HasData || string.IsNullOrEmpty(n.time) || n.time.Length < 16) return;
            var d = wx.data.daily;
            int i = n.dayIdx;
            if (d.sunrise == null || d.sunset == null || i >= d.sunrise.Length || i >= d.sunset.Length) return;
            float rise = Mins(WxCodes.Clock(d.sunrise[i])), sunset = Mins(WxCodes.Clock(d.sunset[i])), t = Mins(WxCodes.Clock(n.time));
            if (rise < 0f || sunset <= rise || t < 0f) return;
            if (t >= rise && t < sunset) n.sunF = (t - rise) / (sunset - rise);
            else
            {
                float night = rise + 1440f - sunset;
                float tt = t >= sunset ? t - sunset : t + 1440f - sunset;
                n.moonF = Mathf.Clamp01(tt / night);
            }
        }

        int DayIndex(string iso)
        {
            if (!wx.HasData || string.IsNullOrEmpty(iso) || iso.Length < 10) return 0;
            string date = iso.Substring(0, 10);
            var d = wx.data.daily;
            for (int i = 0; i < d.time.Length; i++) if (d.time[i] == date) return i;
            return 0;
        }

        bool GetNow(out Now n)
        {
            n = new Now();
            n.time = "";
            if (tab == TabTest)
            {
                n.cond = (WxCond)Mathf.Clamp(tType, 0, 14);
                n.day = tHour >= 7f && tHour < 20f;
                n.temp = tTemp0;
                n.feels = tTemp0 - Mathf.Min(8f, tWind * 0.06f);
                n.wind = tWind; n.gust = tWind * 1.4f; n.windDir = 270f;
                bool wet = WxCodes.IsRain(n.cond) || WxCodes.IsSnow(n.cond);
                n.humidity = wet ? 90f : 55f; n.pressure = 1013f;
                n.precip = wet ? 1.5f : 0f;
                n.cover = n.cond == WxCond.Clear ? 5f : (n.cond == WxCond.MostlyClear ? 20f : (n.cond == WxCond.PartlyCloudy ? 45f : 90f));
                n.sunF = n.moonF = -1f;
                if (n.day) n.sunF = (tHour - 7f) / 13f;
                else n.moonF = Mathf.Clamp01((tHour >= 20f ? tHour - 20f : tHour + 4f) / 11f);
                return true;
            }
            if (!wx.HasData) return false;
            var c = wx.data.current;
            int max = MaxHours();
            if (hourOffset > max) hourOffset = max;
            int b = HourBase();
            if (hourOffset <= 0 || b < 0)
            {
                n.time = c.time ?? "";
                n.cond = WxCodes.FromCode(c.weather_code);
                n.day = c.is_day != 0;
                n.temp = c.temperature_2m; n.feels = c.apparent_temperature;
                n.wind = c.wind_speed_10m; n.windDir = c.wind_direction_10m; n.gust = c.wind_gusts_10m;
                n.humidity = c.relative_humidity_2m; n.pressure = c.surface_pressure; n.precip = c.precipitation; n.cover = c.cloud_cover;
            }
            else
            {
                var h = wx.data.hourly;
                int j = b + hourOffset;
                n.time = h.time[j];
                n.cond = WxCodes.FromCode(AtI(h.weather_code, j, c.weather_code));
                n.day = h.is_day != null && j < h.is_day.Length ? h.is_day[j] != 0 : IsDayAt(n.time);
                n.temp = At(h.temperature_2m, j, c.temperature_2m);
                n.feels = At(h.apparent_temperature, j, n.temp);
                n.wind = At(h.wind_speed_10m, j, c.wind_speed_10m);
                n.windDir = At(h.wind_direction_10m, j, c.wind_direction_10m);
                n.gust = At(h.wind_gusts_10m, j, c.wind_gusts_10m);
                n.humidity = At(h.relative_humidity_2m, j, c.relative_humidity_2m);
                n.pressure = At(h.surface_pressure, j, c.surface_pressure);
                n.precip = At(h.precipitation, j, c.precipitation);
                n.cover = At(h.cloud_cover, j, c.cloud_cover);
            }
            n.dayIdx = DayIndex(n.time);
            Fracs(ref n);
            return true;
        }

        // ------------------------------------------------------------------ la scène (ciel, île 3D) et les textes dans ses coins
        Text StageText(Transform stage, float x, float y, float w, float h, int fs, TextAnchor a, bool bold)
        {
            var t = Ar6dofVrUi.Label(stage, "", new Vector2(x, y), new Vector2(w, h), fs, a);
            var th = t.GetComponent<Ar6dofThemed>();
            if (th != null) UnityEngine.Object.Destroy(th);   // la couleur suit le ciel (clair / sombre), pas le thème des menus
            if (bold) t.fontStyle = FontStyle.Bold;
            t.gameObject.AddComponent<Ar6dofFitText>().Init(t, fs);
            return t;
        }

        /// <summary>Ciel + île 3D (tient dans le cadre : rien ne dépasse sur le reste de la fenêtre) + température et ressenti en bas à gauche, temps et vent en bas à droite.</summary>
        void BuildStage(Now n)
        {
            float sh = FrameH[FrameLevel];
            float sy = RowY(sh);
            var stage = Ar6dofVrUi.Rect(content, "Scene", new Vector2(0f, sy), new Vector2(IW, sh));
            sky = stage.gameObject.AddComponent<Ar6dofSkyGraphic>();
            sky.raycastTarget = false;
            SkyColors(n.cond, n.day, out skyTop, out skyBot, n.temp);
            sky.SetColors(skyTop, skyBot, 26f);
            scene = Ar6dofWeatherScene.Create(stage);
            float drop = DropBy[DropLevel];
            float s = Mathf.Min(IW / 4.6f, sh / 2.98f);   // la scène occupe de -1,4 à +1,65 en hauteur ; descendre l'île ne rétrécit plus la scène (la pointe peut sortir du cadre)
            scene.transform.localPosition = new Vector3(0f, -(0.09f + drop) * s, -0.75f * s);
            scene.transform.localScale = Vector3.one * s;
            // haut du cadre en unites de la scene ; soleil, lune et etoiles gardent leur hauteur d'avant dans le cadre ; tout ce qui sort du cadre est coupe (relief 3D gardé dedans)
            float top = sh * 0.5f / s + 0.09f + drop;
            scene.SetStage(stage, new Vector2(IW, sh), top, drop - 0.12f, 26f);   // 26 = rayon des coins du ciel : la 3D est coupee comme lui
            scene.SetCloudShift(CloudBy[CloudLevel]);

            // coins du bas : l'île est étroite en bas, il y a de la place (et rien ne passe devant)
            float x0 = -IW * 0.5f + 16f, bot = -sh * 0.5f, colW = IW * 0.5f - 110f;
            tFeels = StageText(stage, x0 + colW * 0.5f, bot + 18f, colW, 22f, 17, TextAnchor.MiddleLeft, false);
            tTemp = StageText(stage, x0 + colW * 0.5f, bot + 54f, colW, 48f, 42, TextAnchor.MiddleLeft, true);
            float x1 = IW * 0.5f - 16f;
            tWindT = StageText(stage, x1 - colW * 0.5f, bot + 18f, colW, 22f, 16, TextAnchor.MiddleRight, false);
            tCond = StageText(stage, x1 - colW * 0.5f, bot + 44f, colW, 30f, 22, TextAnchor.MiddleRight, true);

            ApplyScene(n, true);
        }

        void ApplyScene(Now n, bool snap)
        {
            if (scene == null) return;
            scene.WindFx = !Hidden(OptWindFx);
            scene.Apply(n.cond, n.day, n.cover, n.wind, n.windDir, n.temp, snap);
            scene.SetClock(n.sunF, n.moonF, snap);
            DateTime when;
            if (n.time == null || n.time.Length < 10 || !DateTime.TryParse(n.time, Inv, System.Globalization.DateTimeStyles.None, out when)) when = DateTime.UtcNow;
            scene.SetMoonPhase(Ar6dofWeatherScene.MoonPhaseAt(DateTime.SpecifyKind(when, DateTimeKind.Utc)), wx.prefs.place == null || wx.prefs.place.lat >= 0.0);
            SkyColors(n.cond, n.day, out skyTop, out skyBot, n.temp);
            if (sky != null) sky.SetColors(skyTop, skyBot, 26f);
            lastFlash = -1f;
            UpdateStageTexts(n);
        }

        void UpdateStageTexts(Now n)
        {
            if (tTemp == null) return;
            float lum = 0.299f * skyBot.r + 0.587f * skyBot.g + 0.114f * skyBot.b;
            Color col = lum > 0.52f ? new Color(0.06f, 0.10f, 0.22f, 1f) : Color.white;   // texte sombre sur ciel clair, blanc sur ciel sombre
            tTemp.text = TempUnit(n.temp); tTemp.color = col;
            tFeels.gameObject.SetActive(!Hidden(OptFeels));
            tFeels.text = Ar6dofLoc.F("Ressenti {0}", Temp(n.feels)); tFeels.color = col;
            tCond.text = Ar6dofLoc.T(WxCodes.Name(n.cond, n.day)); tCond.color = col;
            tWindT.gameObject.SetActive(!Hidden(OptWindLine));
            tWindT.text = Ar6dofLoc.F("Vent {0} {1}", WxCodes.Compass(n.windDir), Speed(n.wind)); tWindT.color = col;
        }

        // --- Maintenant
        void BuildNow()
        {
            Now n;
            if (!GetNow(out n)) { NoData(); return; }
            BuildStage(n);
            if (!Hidden(OptScrub)) BuildScrub();
            BuildTiles(n);
            Space(2f);
            Lbl("Donnees : Open-Meteo.com (CC BY 4.0)", 24, 16);
        }

        // --- avancer dans le temps, d'heure en heure
        void BuildScrub()
        {
            int max = MaxHours();
            if (max < 1) return;
            hourOffset = Mathf.Clamp(hourOffset, 0, max);
            Space(1f);
            timeLabel = Lbl("", 28, 22, TextAnchor.MiddleCenter);
            timeLabel.fontStyle = FontStyle.Bold;
            UpdateTimeLabel(true);
            SliderRow("Dans", 0f, max, () => hourOffset,
                v => GoHour(Mathf.RoundToInt(v), true),
                v => v < 0.5f ? Ar6dofLoc.T("Maintenant") : "+" + Mathf.RoundToInt(v).ToString(Inv) + " h", 44f, true);
            var bs = BtnRow(42f, 19,
                "-1 h", (Action)(() => GoHour(hourOffset - 1, true)),
                "Maintenant", (Action)(() => GoHour(0, true)),
                "+1 h", (Action)(() => GoHour(hourOffset + 1, true)),
                "Lecture", (Action)TogglePlay);
            playLabel = bs[3].GetComponentInChildren<Text>();
            SetPlayLabel();
        }

        void TogglePlay()
        {
            playing = !playing;
            playTimer = PlayStep * 0.8f;   // la première heure arrive vite
            SetPlayLabel();
        }

        void SetPlayLabel() { if (playLabel != null) playLabel.text = Ar6dofLoc.T(playing ? "Pause" : "Lecture"); }

        /// <summary>Va à une heure : le temps de la scène s'accélère un instant (nuages, soleil, pluie filent), puis le nouveau temps s'installe.</summary>
        void GoHour(int off, bool burst)
        {
            if (!wx.HasData || tab != TabNow) return;
            off = Mathf.Clamp(off, 0, MaxHours());
            if (off == hourOffset) return;
            int prev = hourOffset;
            hourOffset = off;
            if (scene == null) return;
            Now n;
            if (!GetNow(out n)) return;
            if (burst) scene.Burst(BurstTime);
            ApplyScene(n, false);
            UpdateTiles(n);
            animFrom = prev; animTo = off; animT = burst ? 0f : 1f;
            UpdateTimeLabel(false);
            playTimer = 0f;
        }

        void TickScrub(float dt)
        {
            if (tab != TabNow || scene == null || !wx.HasData) return;
            if (animT < 1f)
            {
                animT = Mathf.Min(1f, animT + dt / BurstTime);
                UpdateTimeLabel(false);
            }
            if (playing)
            {
                playTimer += dt;
                if (playTimer >= PlayStep)
                {
                    playTimer = 0f;
                    int max = MaxHours();
                    GoHour(hourOffset >= max ? 0 : hourOffset + 1, true);
                }
            }
        }

        bool BaseTime(out DateTime t)
        {
            t = DateTime.Now;
            int b = HourBase();
            if (b < 0) return false;
            string iso = wx.data.hourly.time[b];
            return iso != null && iso.Length >= 16 && DateTime.TryParseExact(iso.Substring(0, 16), "yyyy-MM-dd'T'HH:mm", Inv, DateTimeStyles.None, out t);
        }

        /// <summary>« Maintenant » ou « Ven. 16:00 (+4 h) » ; pendant l'accélération, l'heure défile.</summary>
        void UpdateTimeLabel(bool final)
        {
            if (timeLabel == null) return;
            DateTime t0;
            if (!BaseTime(out t0)) { timeLabel.text = Ar6dofLoc.T("Maintenant"); return; }
            bool running = !final && animT < 1f;
            float hrs = running ? Mathf.Lerp(animFrom, animTo, Smooth(animT)) : hourOffset;
            DateTime t = t0.AddMinutes(hrs * 60f);
            string clock = t.ToString("HH:mm", Inv);
            if (!running && hourOffset == 0) { timeLabel.text = Ar6dofLoc.T("Maintenant") + "  " + clock; return; }
            string day = WxCodes.DayName(t.ToString("yyyy-MM-dd", Inv), t.Date == t0.Date);
            timeLabel.text = day + "  " + clock + (running ? "" : "   (+" + hourOffset.ToString(Inv) + " h)");
        }

        // --- cases d'information
        string TileValue(int i, Now n)
        {
            var d = wx.data.daily;
            int di = n.dayIdx;
            switch (i)
            {
                case 0: return WxCodes.Compass(n.windDir) + " " + Speed(n.wind);
                case 1: return Speed(n.gust);
                case 2: return Percent(n.humidity);
                case 3: return Pressure(n.pressure);
                case 4: return Percent(n.cover);
                case 5: return Precip(n.precip);
                case 6: return Precip(At(d.precipitation_sum, di, 0f));
                case 7: return At(d.uv_index_max, di, -1f) >= 0f ? At(d.uv_index_max, di, 0f).ToString("0.#", Inv) : "--";
                case 8:
                    return d.sunrise != null && d.sunrise.Length > di && d.sunset != null && d.sunset.Length > di ? WxCodes.Clock(d.sunrise[di]) + " / " + WxCodes.Clock(d.sunset[di]) : "--";
                default: return Temp(At(d.temperature_2m_min, di, n.temp)) + " / " + Temp(At(d.temperature_2m_max, di, n.temp));
            }
        }

        void BuildTiles(Now n)
        {
            var vis = new List<int>();
            for (int i = 0; i < TileCount; i++) if (!Hidden(i)) vis.Add(i);
            if (vis.Count == 0) return;
            Space(4f);
            const float ch = 54f;                       // cases compactes : trois par ligne
            const int cols = 3;
            float cw = (IW - 6f * (cols - 1)) / cols;
            for (int k = 0; k < vis.Count; k += cols)
            {
                float cy = RowY(ch);
                for (int c = 0; c < cols && k + c < vis.Count; c++)
                {
                    int i = vis[k + c];
                    float x = -IW * 0.5f + cw * 0.5f + c * (cw + 6f);
                    var cell = Box(content, new Vector2(x, cy), new Vector2(cw, ch), Ar6dofRole.Band, 14);
                    tileTitle[i] = L(cell, TileNames[i], 0f, 14f, cw - 16f, 20f, 14, TextAnchor.MiddleLeft);
                    tileTitle[i].gameObject.AddComponent<Ar6dofFitText>().Init(tileTitle[i], 14);
                    var v = L(cell, TileValue(i, n), 0f, -9f, cw - 16f, 26f, 20, TextAnchor.MiddleLeft);
                    v.fontStyle = FontStyle.Bold;
                    v.gameObject.AddComponent<Ar6dofFitText>().Init(v, 20);
                    tileVal[i] = v;
                }
            }
            UpdateTiles(n);
        }

        void UpdateTiles(Now n)
        {
            if (!wx.HasData) return;
            for (int i = 0; i < TileCount; i++) if (tileVal[i] != null) tileVal[i].text = TileValue(i, n);
            if (tileTitle[5] != null) tileTitle[5].text = Ar6dofLoc.T(hourOffset > 0 ? "Pluie a cette heure" : "Pluie maintenant");
        }

        // --- Jours
        void BuildDays()
        {
            if (!wx.HasData) { NoData(); return; }
            var d = wx.data.daily;
            int n = Mathf.Min(7, d.time.Length);
            float gMin = 999f, gMax = -999f;
            for (int i = 0; i < n; i++)
            {
                gMin = Mathf.Min(gMin, At(d.temperature_2m_min, i, 0f));
                gMax = Mathf.Max(gMax, At(d.temperature_2m_max, i, 0f));
            }
            if (gMax - gMin < 1f) gMax = gMin + 1f;
            Space(4f);
            float x0 = -IW * 0.5f;
            const float barW = 200f;
            for (int i = 0; i < n; i++)
            {
                float cy = RowY(62f);
                WxCond c = WxCodes.FromCode(AtI(d.weather_code, i, 2));
                var nm = L(content, WxCodes.DayName(d.time[i], i == 0), x0 + 55f, cy, 100f, 62f, 24, TextAnchor.MiddleLeft);
                if (i == 0) nm.fontStyle = FontStyle.Bold;
                Icon(content, new Vector2(x0 + 135f, cy), 54f, c, true);
                float pop = At(d.precipitation_probability_max, i, 0f);
                L(content, pop >= 5f ? Percent(pop) : "", x0 + 205f, cy, 70f, 62f, 20);
                float mn = At(d.temperature_2m_min, i, 0f), mx = At(d.temperature_2m_max, i, 0f);
                L(content, Temp(mn), x0 + 268f, cy, 62f, 62f, 22, TextAnchor.MiddleRight);
                float bx0 = x0 + 310f;
                Box(content, new Vector2(bx0 + barW * 0.5f, cy), new Vector2(barW, 12f), Ar6dofRole.Track, 6);
                float f0 = Mathf.InverseLerp(gMin, gMax, mn), f1 = Mathf.InverseLerp(gMin, gMax, mx);
                float fw = Mathf.Max(10f, (f1 - f0) * barW);
                float fx = Mathf.Min(bx0 + f0 * barW, bx0 + barW - fw);
                Box(content, new Vector2(fx + fw * 0.5f, cy), new Vector2(fw, 12f), Ar6dofRole.Accent, 6);
                var mxT = L(content, Temp(mx), x0 + 550f, cy, 62f, 62f, 22, TextAnchor.MiddleLeft);
                mxT.fontStyle = FontStyle.Bold;
            }
        }

        // --- Heures
        bool IsDayAt(string iso)
        {
            var d = wx.data.daily;
            if (iso == null || iso.Length < 16) return true;
            string date = iso.Substring(0, 10), hm = iso.Substring(11, 5);
            for (int i = 0; i < d.time.Length; i++)
            {
                if (d.time[i] != date || i >= d.sunrise.Length || i >= d.sunset.Length) continue;
                return string.CompareOrdinal(hm, WxCodes.Clock(d.sunrise[i])) >= 0 && string.CompareOrdinal(hm, WxCodes.Clock(d.sunset[i])) < 0;
            }
            return true;
        }

        void BuildHours()
        {
            if (!wx.HasData) { NoData(); return; }
            var h = wx.data.hourly;
            int idx = Mathf.Max(0, HourBase());
            int count = Mathf.Min(24, h.time.Length - idx);
            if (count < 2) { NoData(); return; }
            var temps = new float[count];
            var pops = new float[count];
            for (int k = 0; k < count; k++) { temps[k] = At(h.temperature_2m, idx + k, 0f); pops[k] = At(h.precipitation_probability, idx + k, 0f); }

            Section("Prochaines 24 heures");
            const float chH = 210f;
            float cy = RowY(chH);
            var bg = Box(content, new Vector2(0f, cy), new Vector2(IW, chH), Ar6dofRole.Band, 18);
            var cg = Ar6dofVrUi.Rect(bg, "Courbe", Vector2.zero, new Vector2(IW, chH));
            var chart = cg.gameObject.AddComponent<Ar6dofWxChart>();
            chart.raycastTarget = false;
            chart.Set(temps, pops, Ar6dofVrUi.Accent);
            float yTop = chH * 0.5f - 12f, yBot = -chH * 0.5f + 12f;
            L(bg, Temp(chart.tMax), -IW * 0.5f + 28f, yTop, 52f, 24f, 17, TextAnchor.MiddleLeft);
            L(bg, Temp((chart.tMax + chart.tMin) * 0.5f), -IW * 0.5f + 28f, 0f, 52f, 24f, 17, TextAnchor.MiddleLeft);
            L(bg, Temp(chart.tMin), -IW * 0.5f + 28f, yBot + 14f, 52f, 24f, 17, TextAnchor.MiddleLeft);

            // heures sous la courbe
            float lx0 = -IW * 0.5f + 56f, slot = (IW - 56f - 16f) / (count - 1);
            float ly = RowY(24f);
            for (int k = 0; k < count; k += 6)
                L(content, WxCodes.Clock(h.time[idx + k]), lx0 + k * slot, ly, 70f, 24f, 17);
            Lbl("Courbe : temperature.  Barres bleues : risque de pluie.", 24, 16);

            // une colonne toutes les 3 heures
            Space(4f);
            float colW = IW / 8f;
            float ty = RowY(24f), iy = RowY(48f), my = RowY(28f), py = RowY(22f);
            for (int k = 0; k < 8; k++)
            {
                int j = idx + 3 * k;
                if (j >= h.time.Length) break;
                float x = -IW * 0.5f + colW * (k + 0.5f);
                L(content, WxCodes.Clock(h.time[j]), x, ty, colW, 24f, 18);
                Icon(content, new Vector2(x, iy), 44f, WxCodes.FromCode(AtI(h.weather_code, j, 2)), IsDayAt(h.time[j]));
                var tt = L(content, Temp(At(h.temperature_2m, j, 0f)), x, my, colW, 28f, 22);
                tt.fontStyle = FontStyle.Bold;
                float pp = At(h.precipitation_probability, j, 0f);
                L(content, pp >= 5f ? Percent(pp) : "", x, py, colW, 22f, 16);
            }
        }

        // --- Lieu
        void UseCoordText()
        {
            double lat, lon;
            if (Ar6dofWeatherClient.ParseCoords(coordText, out lat, out lon))
            {
                coordErr = false;
                coordEdited = false;
                tab = TabNow;
                hourOffset = 0;
                nextTry = Time.unscaledTime + 60f;
                wx.Cancel();
                wx.UseCoords(lat, lon);
            }
            else coordErr = true;
            Rebuild();
        }

        void BuildPlace()
        {
            // la ville d'abord : c'est le moyen sur d'avoir la bonne (l'adresse IP donne souvent la ville de ton fournisseur, pas la tienne) ; elle est retenue
            Section("Ma ville");
            Para("Cherche ta ville, choisis-la dans la liste : elle est gardee, et plus jamais remplacee par une estimation. Aucune coordonnee n'est affichee.", 70, 17);
            float cy = RowY(50f);
            Ar6dofVrTextField.Make(content, new Vector2(-75f, cy), new Vector2(IW - 150f, 50f), query, "Nom de ville", s => query = s, s => { query = s; wx.Search(s); }, 24);
            Text lb;
            BtnAt(IW * 0.5f - 70f, cy, 140f, 50f, "Chercher", () => wx.Search(query), out lb, 21);
            for (int i = 0; i < wx.results.Count; i++)
            {
                var p = wx.results[i];
                string name = string.IsNullOrEmpty(p.country) ? p.name : p.name + " - " + p.country;
                Btn(name, () => { tab = TabNow; hourOffset = 0; coordEdited = false; wx.Pick(p); }, 48, 20);
            }
            if (wx.searched && wx.results.Count == 0 && string.IsNullOrEmpty(wx.error)) Lbl("Aucun resultat", 30, 19);

            Section("Position automatique");
            Btn("Utiliser la position de Windows (Wi-Fi / GPS)", () => { nextTry = Time.unscaledTime + 60f; coordEdited = false; hourOffset = 0; wx.UseWindowsLocation(); }, 52, 21);
            Btn("Estimer par internet", () => { nextTry = Time.unscaledTime + 60f; coordEdited = false; hourOffset = 0; wx.Cancel(); wx.UseIp(); }, 52, 21);
            Para("Un PC fixe n'a pas de GPS : Windows et internet ne donnent que la zone de ton fournisseur d'acces, parfois a des dizaines de km. La position est arrondie a 1 km environ et jamais affichee.", 92, 17);

            Section("Coordonnees (avance)");
            if (!coordEdited) coordText = "";
            cy = RowY(50f);
            Ar6dofVrTextField.Make(content, new Vector2(-95f, cy), new Vector2(IW - 190f, 50f), coordText, "latitude, longitude", s => { coordText = s; coordEdited = true; }, s => { coordText = s; UseCoordText(); }, 24);
            Text cb;
            BtnAt(IW * 0.5f - 90f, cy, 180f, 50f, "Utiliser", UseCoordText, out cb, 21);
            if (coordErr) Lbl("Coordonnees non valides. Exemple : 47.39, 0.68", 30, 18);

            Section("Unites");
            var bs = BtnRow(48f, 21,
                "Auto (selon le pays)", (Action)(() => { wx.prefs.units = -1; wx.Save(); Rebuild(); }),
                "Metrique", (Action)(() => { wx.prefs.units = 0; wx.Save(); Rebuild(); }),
                "Imperial", (Action)(() => { wx.prefs.units = 1; wx.Save(); Rebuild(); }));
            for (int i = 0; i < bs.Count; i++) Ar6dofTheme.SetSelected(bs[i], wx.prefs.units == i - 1);

            Section("Autres");
            Btn("Actualiser maintenant", () => { ForceRefresh(); tab = TabNow; Rebuild(); }, 52, 22);
            Para("Donnees meteo : Open-Meteo.com (licence CC BY 4.0), sans compte ni cle. Rien n'est telecharge quand cette fenetre est fermee.", 56, 17);
        }

        // --- Test : tous les types de temps, a la main
        static void TypeDefaults(int type, ref float temp)
        {
            WxCond c = (WxCond)type;
            if (WxCodes.IsSnow(c)) temp = -2f;
            else if (c == WxCond.FreezingRain) temp = -1f;
            else if (c == WxCond.Fog) temp = 6f;
            else if (c == WxCond.Clear) temp = 24f;
            else if (c == WxCond.Thunder) temp = 22f;
            else temp = 14f;
        }

        void ApplyTest(bool burst)
        {
            if (scene == null) return;
            Now n;
            GetNow(out n);
            if (burst) scene.Burst(0.8f);
            ApplyScene(n, false);
            SelectTypeButtons();
        }

        void SelectTypeButtons()
        {
            for (int i = 0; i < typeBtns.Count; i++) if (typeBtns[i] != null) Ar6dofTheme.SetSelected(typeBtns[i], i == tType);
        }

        void TickAuto(float dt)
        {
            if (tab != TabTest || !tAuto || scene == null) return;
            tAutoTimer += dt;
            if (tAutoTimer < 4.5f) return;
            tAutoTimer = 0f;
            tType = (tType + 1) % 15;
            TypeDefaults(tType, ref tTemp0);
            ApplyTest(true);
            Refresh();
        }

        void SetWind(float w)
        {
            tWind = w;
            ApplyTest(false);
            Refresh();
        }

        void BuildTest()
        {
            Now n;
            GetNow(out n);
            BuildStage(n);

            Section("Type de temps");
            const int Cols = 3;
            float bw = (IW - (Cols - 1) * 8f) / Cols;
            for (int i = 0; i < 15; i++)
            {
                int col = i % Cols;
                if (col == 0) rowCy = RowY(48f);
                int idx = i;
                Text lb;
                var b = BtnAt(-IW * 0.5f + bw * 0.5f + col * (bw + 8f), rowCy, bw, 48f, WxCodes.Name((WxCond)i, true),
                    () => { tType = idx; TypeDefaults(idx, ref tTemp0); ApplyTest(false); Refresh(); }, out lb, 19);
                typeBtns.Add(b);
            }
            SelectTypeButtons();

            Section("Conditions");
            SliderRow("Heure du jour", 0f, 23f, () => tHour, v => { tHour = v; ApplyTest(false); }, v => Mathf.RoundToInt(v).ToString(Inv) + " h", 50f, true);
            SliderRow("Vent", 0f, 120f, () => tWind, v => { tWind = v; ApplyTest(false); }, v => Speed(v), 50f, true);
            SliderRow("Temperature", -25f, 45f, () => tTemp0, v => { tTemp0 = v; ApplyTest(false); }, v => TempUnit(v), 50f, true);
            BtnRow(46f, 19,
                "Calme", (Action)(() => SetWind(4f)),
                "Brise", (Action)(() => SetWind(25f)),
                "Vent fort", (Action)(() => SetWind(55f)),
                "Tempete", (Action)(() => SetWind(95f)));
            ToggleRow("Tour automatique des temps", () => tAuto, v => { tAuto = v; tAutoTimer = 0f; }, 52, 21);
            Para("Le tour automatique passe tous les temps en revue, du ciel degage a l'orage et a la grele. Les curseurs changent l'heure (le soleil et la lune se deplacent), le vent (arbres, moulin, traits de vent) et la temperature (gel, canicule).", 76, 17);
        }

        // --- Options (roue) : montrer / cacher
        void OptGrid(params int[] bits)
        {
            float w = (IW - 8f) * 0.5f;
            for (int i = 0; i < bits.Length; i += 2)
            {
                float cy = RowY(50f);
                for (int c = 0; c < 2 && i + c < bits.Length; c++)
                {
                    int bit = bits[i + c];
                    string name = OptName(bit);
                    Text lbl;
                    var b = BtnAt(-IW * 0.5f + w * 0.5f + c * (w + 8f), cy, w, 50f, name, () => { SetHidden(bit, !Hidden(bit)); Refresh(); }, out lbl, 19);
                    Text tl = lbl;
                    Button bb = b;
                    string nm = name;
                    refreshers.Add(() => { tl.text = Ar6dofLoc.T(nm) + " : " + Ar6dofLoc.T(Hidden(bit) ? "Non" : "Oui"); Ar6dofTheme.SetSelected(bb, !Hidden(bit)); });
                }
            }
        }

        void BuildOptions()
        {
            Text bl;
            BtnAt(IW * 0.5f - 62f, -38f, 124f, 40f, "Retour", () => { tab = TabNow; Rebuild(); }, out bl, 19);
            Para("Choisis ce que montre l'ecran Maintenant. Ce qui est cache ne coute rien.", 44, 18);
            Section("Cases d'information");
            OptGrid(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
            Section("Ecran Maintenant");
            OptGrid(OptFeels, OptWindLine, OptScrub, OptWindFx);
            Section("Scene 3D");
            SliderRow("Hauteur de l'ile", 0f, 4f, () => DropLevel, v => { wx.prefs.dropSel = Mathf.RoundToInt(v) + 1; wx.Save(); }, v => Ar6dofLoc.T(DropNames[Mathf.Clamp(Mathf.RoundToInt(v), 0, 4)]), 50f, true);
            SliderRow("Hauteur des nuages et effets", 0f, 4f, () => CloudLevel, v => { wx.prefs.cloudSel = Mathf.RoundToInt(v) + 1; wx.Save(); }, v => Ar6dofLoc.T(CloudNames[Mathf.Clamp(Mathf.RoundToInt(v), 0, 4)]), 50f, true);
            SliderRow("Taille du cadre", 0f, 4f, () => FrameLevel, v => { wx.prefs.frameSel = Mathf.RoundToInt(v) + 1; wx.Save(); }, v => Mathf.RoundToInt(FrameH[Mathf.Clamp(Mathf.RoundToInt(v), 0, 4)]).ToString(Inv) + " px", 50f, true);
            Section("Page Plus");
            Text pl;
            BtnT("", () => { wx.prefs.plus = wx.prefs.plus != 0 ? 0 : 1; wx.Save(); Refresh(); }, out pl, 52f, 21);
            Text pll = pl;
            refreshers.Add(() => pll.text = Ar6dofLoc.T("Page Plus (tester tous les temps)") + " : " + Ar6dofLoc.T(wx.prefs.plus != 0 ? "Oui" : "Non"));
            Para("La page Plus permet de voir tous les types de temps a la main (heure, vent, temperature). Elle reste cachee tant que tu ne l'actives pas.", 52, 17);
            if (wx.prefs.plus != 0) Btn("Ouvrir la page Plus", () => { tab = TabTest; Rebuild(); }, 52, 21);
        }
    }
}
