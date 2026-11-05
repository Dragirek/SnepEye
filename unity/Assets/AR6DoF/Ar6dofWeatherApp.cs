using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Application « Météo » : la météo de ta position (ou d'une ville de ton choix) avec un petit monde 3D animé en style dessin animé
    /// (soleil, lune, nuages, pluie, neige, brouillard, orage, vent qui fait tourner le moulin), les prévisions sur 7 jours et sur 24 heures.
    /// Données : Open-Meteo.com (gratuit, sans clé, tous les pays). Rien ne tourne et rien n'est téléchargé tant que la fenêtre n'est pas ouverte.
    /// </summary>
    public class Ar6dofWeatherApp : Ar6dofAppWindow
    {
        static readonly string[] TabNames = { "Maintenant", "Jours", "Heures", "Lieu" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int PreviewCount = 30;   // 15 conditions, de jour puis de nuit

        struct Now
        {
            public WxCond cond;
            public bool day;
            public float temp, feels, wind, windDir, gust, humidity, pressure, precip, cover;
        }

        int tab;
        int preview = -1;
        string query = "";
        bool loaded;
        float nextTry;
        readonly Ar6dofWeatherClient wx = new Ar6dofWeatherClient();

        Ar6dofWeatherScene scene;
        Ar6dofSkyGraphic sky;
        Color skyTop, skyBot;
        float lastFlash = -1f;
        Text ageText;

        // ce qui a été affiché : si la réalité change (réponse reçue, erreur...), la fenêtre est reconstruite
        float seenFetch = -2f;
        int seenPhase = -1, seenResults = -1;
        string seenErr = "", seenStatus = "", seenPlace = "", seenHint = "";
        bool seenSearched;

        public Ar6dofWeatherApp(Ar6dofDock d) : base(d, "weather", "Meteo", "weather", "Meteo de ta position en 3D : soleil, nuages, pluie, neige, vent et previsions.", 660f) { }

        protected override void OnRebuildStart() { scene = null; sky = null; ageText = null; lastFlash = -1f; }

        // ------------------------------------------------------------------ vie de la fenêtre
        protected override void OnVisible(bool on)
        {
            if (!on) return;
            if (!loaded) { wx.Load(); loaded = true; Rebuild(); }
            nextTry = 0f;
            MaybeRefresh();
        }

        public override void OnClosed() { wx.Cancel(); }

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
            if (wx.fetchedAt != seenFetch || (int)wx.phase != seenPhase || wx.error != seenErr || wx.status != seenStatus || wx.hint != seenHint ||
                wx.prefs.place.name != seenPlace || wx.results.Count != seenResults || wx.searched != seenSearched)
            {
                Rebuild();
                return;
            }
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

        string AgeString()
        {
            int m = Mathf.FloorToInt(wx.AgeSeconds / 60f);
            return m < 1 ? Ar6dofLoc.T("Mis a jour a l'instant") : Ar6dofLoc.F("Mis a jour il y a {0} min", m);
        }

        // ------------------------------------------------------------------ couleurs du ciel
        static void SkyColors(WxCond c, bool day, out Color top, out Color bot)
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
            Tabs(TabNames, tab, t => tab = t);
            Remember();
            PlaceLine();
            switch (tab)
            {
                case 0: BuildNow(); break;
                case 1: BuildDays(); break;
                case 2: BuildHours(); break;
                default: BuildPlace(); break;
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

        void PlaceLine()
        {
            var p = wx.prefs.place;
            string nm = !p.valid ? "Position inconnue" : (string.IsNullOrEmpty(p.country) || p.country == p.name ? p.name : p.name + ", " + p.country);
            var t = Lbl(nm, 34, 24);
            t.fontStyle = FontStyle.Bold;
            if (wx.Busy)
            {
                string s = !string.IsNullOrEmpty(wx.status) ? wx.status : (wx.phase == Ar6dofWeatherClient.Phase.Searching ? "Recherche..." : "Chargement de la meteo...");
                Lbl(s, 28, 19);
            }
            else if (!string.IsNullOrEmpty(wx.error)) Para(wx.error, 52, 18);
            else if (wx.HasData && tab != 3)
            {
                ageText = Lbl("", 26, 17);
                var at = ageText;
                refreshers.Add(() => { if (at != null) at.text = AgeString(); });
            }
            if (tab == 3 && !wx.Busy && !string.IsNullOrEmpty(wx.hint)) Para(wx.hint, 52, 17);
        }

        void NoData()
        {
            if (wx.Busy) return;
            Para("Pas encore de meteo. Verifie ta connexion internet ou choisis ta ville dans l'onglet Lieu.", 56, 19);
            Btn("Reessayer", () => { ForceRefresh(); Rebuild(); }, 52, 22);
        }

        bool GetNow(out Now n)
        {
            n = new Now();
            if (preview >= 0)
            {
                n.cond = (WxCond)(preview % 15);
                n.day = preview < 15;
                n.temp = WxCodes.IsSnow(n.cond) ? -2f : (n.cond == WxCond.Fog ? 6f : (n.cond == WxCond.Clear ? 24f : 14f));
                n.feels = n.temp - 2f;
                n.wind = n.cond == WxCond.Thunder || n.cond == WxCond.Hail || n.cond == WxCond.HeavyRain ? 38f : 22f;
                n.gust = n.wind * 1.4f;
                n.windDir = 270f;
                n.humidity = 70f; n.pressure = 1012f; n.precip = 0f; n.cover = 50f;
                return true;
            }
            if (!wx.HasData) return false;
            var c = wx.data.current;
            n.cond = WxCodes.FromCode(c.weather_code);
            n.day = c.is_day != 0;
            n.temp = c.temperature_2m; n.feels = c.apparent_temperature;
            n.wind = c.wind_speed_10m; n.windDir = c.wind_direction_10m; n.gust = c.wind_gusts_10m;
            n.humidity = c.relative_humidity_2m; n.pressure = c.surface_pressure; n.precip = c.precipitation; n.cover = c.cloud_cover;
            return true;
        }

        // --- Maintenant
        void BuildNow()
        {
            Now n;
            if (!GetNow(out n)) { NoData(); return; }

            // température et condition
            float cy = RowY(84f);
            float x0 = -IW * 0.5f;
            var big = L(content, TempUnit(n.temp), x0 + 125f, cy, 250f, 84f, 64, TextAnchor.MiddleLeft);
            big.fontStyle = FontStyle.Bold;
            float rx = x0 + 262f, rw = IW - 262f;
            var cn = L(content, WxCodes.Name(n.cond, n.day), rx + rw * 0.5f, cy + 17f, rw, 36f, 28, TextAnchor.MiddleLeft);
            cn.fontStyle = FontStyle.Bold;
            L(content, Ar6dofLoc.F("Ressenti {0}", Temp(n.feels)), rx + rw * 0.5f, cy - 20f, rw, 30f, 21, TextAnchor.MiddleLeft);

            // le monde 3D
            const float sh = 330f;
            float sy = RowY(sh);
            var stage = Ar6dofVrUi.Rect(content, "Scene", new Vector2(0f, sy), new Vector2(IW, sh));
            sky = stage.gameObject.AddComponent<Ar6dofSkyGraphic>();
            sky.raycastTarget = false;
            SkyColors(n.cond, n.day, out skyTop, out skyBot);
            sky.SetColors(skyTop, skyBot, 26f);
            scene = Ar6dofWeatherScene.Create(stage);
            float s = Mathf.Min(IW / 4.4f, sh / 2.55f);
            scene.transform.localPosition = new Vector3(0f, 6f, -0.75f * s);
            scene.transform.localScale = Vector3.one * s;
            scene.Apply(n.cond, n.day, n.cover, n.wind, n.windDir, n.temp, true);

            if (preview >= 0)
            {
                Para(Ar6dofLoc.F("Apercu {0} / {1} : {2} ({3})", preview + 1, PreviewCount, Ar6dofLoc.T(WxCodes.Name(n.cond, n.day)), Ar6dofLoc.T(n.day ? "jour" : "nuit")), 30, 19);
                BtnRow(52f, 21,
                    "Precedent", (Action)(() => { preview = (preview + PreviewCount - 1) % PreviewCount; Rebuild(); }),
                    "Suivant", (Action)(() => { preview = (preview + 1) % PreviewCount; Rebuild(); }),
                    "Quitter", (Action)(() => { preview = -1; Rebuild(); }));
                return;
            }

            // détails
            var d = wx.data.daily;
            string sun = d.sunrise != null && d.sunrise.Length > 0 && d.sunset != null && d.sunset.Length > 0 ? WxCodes.Clock(d.sunrise[0]) + " / " + WxCodes.Clock(d.sunset[0]) : "--";
            string uv = At(d.uv_index_max, 0, -1f) >= 0f ? At(d.uv_index_max, 0, 0f).ToString("0.#", Inv) : "--";
            Space(4f);
            Cells(
                "Vent", WxCodes.Compass(n.windDir) + " " + Speed(n.wind),
                "Rafales", Speed(n.gust),
                "Humidite", Percent(n.humidity),
                "Pression", Pressure(n.pressure),
                "Nuages", Percent(n.cover),
                "Pluie maintenant", Precip(n.precip),
                "Pluie aujourd'hui", Precip(At(d.precipitation_sum, 0, 0f)),
                "Indice UV", uv,
                "Lever / coucher", sun,
                "Min / max aujourd'hui", Temp(At(d.temperature_2m_min, 0, n.temp)) + " / " + Temp(At(d.temperature_2m_max, 0, n.temp)));
            Space(2f);
            Lbl("Donnees : Open-Meteo.com (CC BY 4.0)", 24, 16);
        }

        /// <summary>Cases d'information, deux par ligne : paires (titre, valeur).</summary>
        void Cells(params string[] kv)
        {
            const float ch = 66f;
            float cw = (IW - 8f) * 0.5f;
            int pairs = kv.Length / 2;
            for (int i = 0; i < pairs; i += 2)
            {
                float cy = RowY(ch);
                for (int c = 0; c < 2 && i + c < pairs; c++)
                {
                    float x = -IW * 0.5f + cw * 0.5f + c * (cw + 8f);
                    var cell = Box(content, new Vector2(x, cy), new Vector2(cw, ch), Ar6dofRole.Band, 18);
                    L(cell, kv[2 * (i + c)], 0f, 17f, cw - 24f, 24f, 17, TextAnchor.MiddleLeft);
                    var v = L(cell, kv[2 * (i + c) + 1], 0f, -13f, cw - 24f, 30f, 23, TextAnchor.MiddleLeft);
                    v.fontStyle = FontStyle.Bold;
                    v.gameObject.AddComponent<Ar6dofFitText>().Init(v, 23);
                }
            }
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
                var pt = L(content, pop >= 5f ? Percent(pop) : "", x0 + 205f, cy, 70f, 62f, 20);
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
            string cur = wx.data.current.time ?? "";
            int idx = 0;
            if (cur.Length >= 13)
            {
                string pre = cur.Substring(0, 13);
                for (int i = 0; i < h.time.Length; i++) if (h.time[i].StartsWith(pre, StringComparison.Ordinal)) { idx = i; break; }
            }
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
        void BuildPlace()
        {
            Section("Position");
            Btn("Utiliser ma position (GPS / Wi-Fi de Windows)", () => { nextTry = Time.unscaledTime + 60f; wx.UseWindowsLocation(); }, 52, 21);
            Btn("Position approximative (par internet)", () => { nextTry = Time.unscaledTime + 60f; wx.Cancel(); wx.UseIp(); }, 52, 21);
            Para("Position Windows : active Parametres > Confidentialite et securite > Localisation. Sinon la position approximative (ville) est deduite de ton adresse IP.", 70, 17);

            Section("Chercher une ville");
            float cy = RowY(50f);
            Ar6dofVrTextField.Make(content, new Vector2(-75f, cy), new Vector2(IW - 150f, 50f), query, "Nom de ville", s => query = s, s => { query = s; wx.Search(s); }, 24);
            Text lb;
            BtnAt(IW * 0.5f - 70f, cy, 140f, 50f, "Chercher", () => wx.Search(query), out lb, 21);
            for (int i = 0; i < wx.results.Count; i++)
            {
                var p = wx.results[i];
                string name = string.IsNullOrEmpty(p.country) ? p.name : p.name + " - " + p.country;
                Btn(name, () => { tab = 0; wx.Pick(p); }, 48, 20);
            }
            if (wx.searched && wx.results.Count == 0 && string.IsNullOrEmpty(wx.error)) Lbl("Aucun resultat", 30, 19);

            Section("Unites");
            var bs = BtnRow(48f, 21,
                "Auto (selon le pays)", (Action)(() => { wx.prefs.units = -1; wx.Save(); Rebuild(); }),
                "Metrique", (Action)(() => { wx.prefs.units = 0; wx.Save(); Rebuild(); }),
                "Imperial", (Action)(() => { wx.prefs.units = 1; wx.Save(); Rebuild(); }));
            for (int i = 0; i < bs.Count; i++) Ar6dofTheme.SetSelected(bs[i], wx.prefs.units == i - 1);

            Section("Autres");
            Btn("Actualiser maintenant", () => { ForceRefresh(); tab = 0; Rebuild(); }, 52, 22);
            Btn("Voir tous les effets 3D (test)", () => { preview = 0; tab = 0; Rebuild(); }, 52, 22);
            Para("Donnees meteo : Open-Meteo.com (licence CC BY 4.0), sans compte ni cle. Rien n'est telecharge quand cette fenetre est fermee.", 56, 17);
        }
    }
}
