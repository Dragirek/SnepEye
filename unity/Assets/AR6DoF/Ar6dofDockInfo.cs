using System;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Bandeau d'informations en bas du dock : heure (numérique et/ou analogique), date, batterie du PC. Chaque élément se règle dans
    /// Réglages &gt; Interface &gt; Dock. Rien n'est construit ni mis à jour quand les trois sont décochés (coût nul). Mise à jour 10 fois par seconde au plus,
    /// uniquement quand le dock est visible, et le texte n'est réécrit que s'il change.
    /// </summary>
    public class Ar6dofDockInfo
    {
        public const float Height = 108f;

        Ar6dofClockFace face;
        Text timeTxt, dateTxt, batTxt, batSub;
        Ar6dofBatteryGfx bat;
        bool wantSeconds, h24;
        int dateFmt, batStyle;
        int lastStamp = -1;
        float nextTick, nextBat;

        public static bool Wanted(Ar6dofSettingsData d) { return d.dockShowClock || d.dockShowDate || d.dockShowBattery; }

        /// <summary>Change quand un réglage qui modifie le bandeau change : le dock le reconstruit alors.</summary>
        public static int Signature(Ar6dofSettingsData d)
        {
            int h = (d.dockShowClock ? 1 : 0) | (d.dockShowDate ? 2 : 0) | (d.dockShowBattery ? 4 : 0) | (d.clock24h ? 8 : 0) | (d.clockSeconds ? 16 : 0);
            h |= d.dockClockStyle << 5;
            h |= d.dockBatteryStyle << 7;
            h |= d.dateFormat << 9;
            h |= d.dockFaceStyle << 13;
            return h;
        }

        static float TextW(string s, int font) { return Mathf.Max(20f, s.Length * font * 0.58f + 14f); }

        /// <summary>Construit le bandeau, centré sur (0, y) dans 'parent', pour une barre large de 'barW'.</summary>
        public void Build(Transform parent, float barW, float y)
        {
            var d = Ar6dofSettings.Current;
            h24 = d.clock24h;
            wantSeconds = d.clockSeconds;
            dateFmt = d.dateFormat;
            batStyle = d.dockBatteryStyle;
            bool analog = d.dockShowClock && d.dockClockStyle != 0;
            bool digital = d.dockShowClock && d.dockClockStyle != 1;
            DateTime now = DateTime.Now;

            // fond du bandeau
            var band = Ar6dofVrUi.Rect(parent, "Infos", new Vector2(0f, y), new Vector2(barW - 36f, Height - 14f));
            var bim = band.gameObject.AddComponent<Image>();
            bim.raycastTarget = false;
            Ar6dofTheme.Attach(band.gameObject, Ar6dofRole.Band, 30);

            // taille de chaque bloc, de gauche à droite
            const float gap = 40f;
            string sampleTime = Ar6dofClockFmt.Time(new DateTime(2000, 1, 1, 10, 8, 8), h24, wantSeconds);
            string dateNow = d.dockShowDate ? Ar6dofClockFmt.Date(now, dateFmt) : "";
            int dateFont = digital ? 24 : 32;
            float analogW = analog ? 92f : 0f;
            float timeW = digital ? TextW(sampleTime + (h24 ? "" : "M"), 48) : 0f;
            float dateW = d.dockShowDate ? TextW(dateNow, dateFont) : 0f;
            float textW = Mathf.Max(timeW, dateW);
            float batW = 0f;
            if (d.dockShowBattery) batW = batStyle == 0 ? 130f : batStyle == 1 ? 90f : 250f;

            int blocks = (analog ? 1 : 0) + (textW > 0f ? 1 : 0) + (batW > 0f ? 1 : 0);
            float total = analogW + textW + batW + gap * Mathf.Max(0, blocks - 1);
            float x = -total * 0.5f;

            if (analog)
            {
                var fr = Ar6dofVrUi.Rect(parent, "Cadran", new Vector2(x + analogW * 0.5f, y), new Vector2(analogW, analogW));
                face = fr.gameObject.AddComponent<Ar6dofClockFace>();
                face.raycastTarget = false;
                face.Configure(d.dockFaceStyle, wantSeconds, 1);
                x += analogW + gap;
            }
            if (textW > 0f)
            {
                float cx = x + textW * 0.5f;
                if (digital)
                {
                    timeTxt = Ar6dofVrUi.Label(parent, "", new Vector2(cx, d.dockShowDate ? y + 15f : y), new Vector2(textW + 40f, 56f), 48, TextAnchor.MiddleCenter);
                    timeTxt.fontStyle = FontStyle.Bold;
                    timeTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
                if (d.dockShowDate)
                {
                    dateTxt = Ar6dofVrUi.Label(parent, "", new Vector2(cx, digital ? y - 28f : y), new Vector2(textW + 80f, 36f), dateFont, TextAnchor.MiddleCenter);
                    dateTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
                x += textW + gap;
            }
            if (batW > 0f)
            {
                float cx = x + batW * 0.5f;
                if (batStyle == 0)
                {
                    batTxt = Ar6dofVrUi.Label(parent, "", new Vector2(cx, y), new Vector2(batW + 40f, 48f), 38, TextAnchor.MiddleCenter);
                    batTxt.fontStyle = FontStyle.Bold;
                }
                else if (batStyle == 1)
                {
                    var br = Ar6dofVrUi.Rect(parent, "Batterie", new Vector2(cx, y), new Vector2(86f, 48f));
                    bat = br.gameObject.AddComponent<Ar6dofBatteryGfx>();
                    bat.raycastTarget = false;
                }
                else
                {
                    var br = Ar6dofVrUi.Rect(parent, "Batterie", new Vector2(x + 40f, y), new Vector2(76f, 44f));
                    bat = br.gameObject.AddComponent<Ar6dofBatteryGfx>();
                    bat.raycastTarget = false;
                    batTxt = Ar6dofVrUi.Label(parent, "", new Vector2(x + 82f + 70f, y + 15f), new Vector2(150f, 40f), 32, TextAnchor.MiddleCenter);
                    batTxt.fontStyle = FontStyle.Bold;
                    batSub = Ar6dofVrUi.Label(parent, "", new Vector2(x + 82f + 70f, y - 20f), new Vector2(190f, 30f), 21, TextAnchor.MiddleCenter);
                }
            }
            lastStamp = -1;
            nextBat = 0f;
            Tick(true);
        }

        public void Tick(bool force = false)
        {
            float t = Time.unscaledTime;
            if (!force && t < nextTick) return;
            nextTick = t + 0.1f;

            DateTime now = DateTime.Now;
            int stamp = now.Hour * 3600 + now.Minute * 60 + now.Second;
            if (face != null) face.SetTime(now.TimeOfDay.TotalSeconds);
            if (stamp != lastStamp)
            {
                lastStamp = stamp;
                if (timeTxt != null) timeTxt.text = Ar6dofClockFmt.Time(now, h24, wantSeconds);
                if (dateTxt != null) dateTxt.text = Ar6dofClockFmt.Date(now, dateFmt);
            }
            if ((bat != null || batTxt != null) && (force || t >= nextBat))
            {
                nextBat = t + 5f;
                UpdateBattery();
            }
        }

        void UpdateBattery()
        {
            var b = Ar6dofSysInfo.ReadBattery();
            string pct = b.has && !float.IsNaN(b.pct) ? Mathf.RoundToInt(b.pct * 100f) + " %" : "";
            if (bat != null) bat.Set(b.has, b.pct, b.charging);
            string main, sub;
            if (!b.has) { main = Ar6dofLoc.T("Secteur"); sub = Ar6dofLoc.T("Pas de batterie"); }
            else
            {
                main = pct;
                if (b.charging) sub = Ar6dofLoc.T("En charge");
                else if (b.onAc) sub = Ar6dofLoc.T("Secteur");
                else sub = b.seconds >= 0 ? Ar6dofClockFmt.Span(TimeSpan.FromSeconds(b.seconds)) : "";
            }
            if (batTxt != null) batTxt.text = batStyle == 0 && b.has && b.charging ? main + " +" : main;
            if (batSub != null) batSub.text = sub;
        }
    }
}
