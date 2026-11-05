using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Table de mixage : volumes, muets et niveaux de la voix, du soundboard et de la musique, volume général, visualisation de la sortie (8 au choix),
    /// une console avancée (faders, solo, balance, égaliseur 3 bandes, limiteur : tout visible d'un coup, sans défiler) et un lecteur de musique
    /// avec liste de lecture (pour un fursuiter qui joue de la musique sur ses enceintes). Tout sort sur les enceintes choisies dans l'application Audio.
    /// </summary>
    public class Ar6dofMixerApp : Ar6dofAppWindow
    {
        static readonly string[] AudioExt = { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".mp4" };
        int tab;
        int plPage;
        readonly List<Ar6dofMeterBar> bars = new List<Ar6dofMeterBar>();
        readonly List<Func<AudioMeters, float>> barSel = new List<Func<AudioMeters, float>>();
        Ar6dofViz viz;
        Text vizName;
        const float ConsoleW = 1000f, NormalW = 720f;
        Slider seek;
        Text timeLbl;
        float lastUserSeek = -10f;
        bool seekPending;
        const int PlPerPage = 6;

        public Ar6dofMixerApp(Ar6dofDock d) : base(d, "mixer", "Table de mixage", "mixer", "Volumes, muets, niveaux, spectre et lecteur de musique", 720f) { }

        public override void OnClosed() { var a = Ar6dofAudio.Instance; if (a != null) a.StopMusic(); }

        // la console avancée est large : la fenêtre s'élargit pour qu'on ne défile jamais
        protected override float MainWidth() { return tab == 1 ? ConsoleW : NormalW; }

        protected override void OnRebuildStart() { bars.Clear(); barSel.Clear(); viz = null; vizName = null; seek = null; timeLbl = null; }

        protected override void BuildMain()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 40, 22); return; }
            Tabs(new[] { "Table", "Console", "Musique" }, tab, t => tab = t);
            if (!a.Enabled) EngineRow();   // le moteur se lance tout seul a l'usage ; l'interrupteur n'apparait que s'il est eteint
            if (tab == 0) Table(a); else if (tab == 1) ConsoleTab(a); else Music(a);
        }

        // ------------------------------------------------------------------ table
        void ChannelRow(Ar6dofAudio a, string name, int ch, Func<AudioMeters, float> peak)
        {
            const float h = 56f;
            float cy = RowY(h);
            float x0 = -IW * 0.5f;
            float lw = 120f, sw = 250f, mw = 86f;
            float mtw = IW - lw - sw - mw - 3 * 8f;
            Ar6dofVrUi.Label(content, name, new Vector2(x0 + lw * 0.5f, cy), new Vector2(lw, h), 22, TextAnchor.MiddleLeft);
            Slider s = null;
            Text val = Ar6dofVrUi.Label(content, "", new Vector2(x0 + lw + 8f + sw * 0.5f, cy - 22f), new Vector2(sw, 18f), 15, TextAnchor.MiddleRight);
            s = Ar6dofVrUi.MakeSlider(content, new Vector2(x0 + lw + 8f + sw * 0.5f, cy + 4f), new Vector2(sw, 36f), 0f, 1.5f, a.data.vol[ch], v => { a.SetVolume(ch, v); val.text = Pct(v); });
            Slider ss = s;
            Text mt;
            var mb = BtnAt(x0 + lw + sw + 16f + mw * 0.5f, cy, mw, 44f, "Muet", () => { a.SetMute(ch, !a.data.mute[ch]); Refresh(); }, out mt, 19);
            var meter = Ar6dofMeterBar.Make(content, new Vector2(x0 + lw + sw + mw + 24f + mtw * 0.5f, cy), new Vector2(mtw, 30f));
            bars.Add(meter); barSel.Add(peak);
            refreshers.Add(() => { ss.SetValueWithoutNotify(a.data.vol[ch]); val.text = Pct(a.data.vol[ch]); Ar6dofTheme.SetSelected(mb, a.data.mute[ch]); });
        }

        void Table(Ar6dofAudio a)
        {
            // micro : seulement un niveau (le gain se règle dans Changeur de voix > Base)
            float cy = RowY(44f);
            float x0 = -IW * 0.5f;
            Ar6dofVrUi.Label(content, "Micro", new Vector2(x0 + 60f, cy), new Vector2(120f, 44f), 22, TextAnchor.MiddleLeft);
            var mic = Ar6dofMeterBar.Make(content, new Vector2(x0 + 128f + (IW - 128f) * 0.5f, cy), new Vector2(IW - 128f, 30f));
            bars.Add(mic); barSel.Add(m => m.peak[0]);
            ChannelRow(a, "Voix", 0, m => m.peak[1]);
            ChannelRow(a, "Soundboard", 1, m => m.peak[2]);
            ChannelRow(a, "Musique", 2, m => m.peak[3]);
            ChannelRow(a, "Principal", 3, m => Mathf.Max(m.peak[4], m.peak[5]));
            Section("Visuel de la sortie");
            viz = Ar6dofViz.Make(content, new Vector2(0f, RowY(150f)), new Vector2(IW, 150f));
            viz.mode = Mathf.Clamp(a.data.viz, 0, Ar6dofViz.Count - 1);
            VizSelector(a, 0f, RowY(48f), IW);
            var info = Lbl("", 26, 18);
            refreshers.Add(() => info.text = Ar6dofLoc.F("Sons en cours : {0}", a.Meters.voices));
            Btn("Tout arreter (sons et musique)", () => { a.StopAllSounds(); a.StopMusic(); Refresh(); }, 48f, 22);
        }

        // ------------------------------------------------------------------ sélecteur de visuel
        void VizSelector(Ar6dofAudio a, float cx, float cy, float w)
        {
            const float bw = 64f, h = 44f;
            Text t1, t2, t3;
            BtnAt(cx - w * 0.5f + bw * 0.5f, cy, bw, h, "<", () => ChooseViz(a, -1), out t1, 24);
            var mid = BtnAt(cx, cy, w - 2f * bw - 16f, h, "", () => ChooseViz(a, 1), out t2, 21);
            BtnAt(cx + w * 0.5f - bw * 0.5f, cy, bw, h, ">", () => ChooseViz(a, 1), out t3, 24);
            vizName = t2;
            t2.text = VizLabel(a);
        }

        string VizLabel(Ar6dofAudio a)
        {
            int m = Mathf.Clamp(a.data.viz, 0, Ar6dofViz.Count - 1);
            return Ar6dofLoc.T("Visuel") + " " + (m + 1) + "/" + Ar6dofViz.Count + " : " + Ar6dofLoc.T(Ar6dofViz.Names[m]);
        }

        void ChooseViz(Ar6dofAudio a, int dir)
        {
            int n = Ar6dofViz.Count;
            a.SetViz(((a.data.viz + dir) % n + n) % n);
            if (viz != null) { viz.mode = a.data.viz; viz.Clear(); }
            if (vizName != null) vizName.text = VizLabel(a);
        }

        // ------------------------------------------------------------------ console avancée
        static readonly string[] StripNames = { "Voix", "Soundboard", "Musique", "Principal" };
        static readonly string[] EqNames = { "Gr", "Md", "Ai" };   // graves, medium, aigus
        const float StripW = 156f, StripGap = 6f, StripH = 564f, FaderH = 260f;

        static float GainToPos(float g) { return Mathf.Sqrt(Mathf.Clamp(g, 0f, 1.5f) / 1.5f); }   // fader : courbe douce, 0 dB aux 82 % de la course
        static float PosToGain(float p) { float g = 1.5f * p * p; return Mathf.Abs(g - 1f) < 0.035f ? 1f : g; }

        static string DbText(float g)
        {
            if (g < 0.001f) return "-inf dB";
            float db = 20f * Mathf.Log10(g);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0} dB", db);
        }

        void ConsoleTab(Ar6dofAudio a)
        {
            float top = y;
            float x0 = -IW * 0.5f;
            for (int c = 0; c < 4; c++) BuildStrip(a, c, x0 + c * (StripW + StripGap) + StripW * 0.5f, top);

            // panneau de droite : visuel, sons en cours, micro, boutons globaux
            float px = x0 + 4f * (StripW + StripGap) + 6f;
            float pw = IW - (px - x0);
            float pc = px + pw * 0.5f;
            viz = Ar6dofViz.Make(content, new Vector2(pc, -(top + 115f)), new Vector2(pw, 230f));
            viz.mode = Mathf.Clamp(a.data.viz, 0, Ar6dofViz.Count - 1);
            VizSelector(a, pc, -(top + 230f + 8f + 22f), pw);

            var info = Ar6dofVrUi.Label(content, "", new Vector2(pc, -(top + 306f)), new Vector2(pw, 28f), 19, TextAnchor.MiddleLeft);
            refreshers.Add(() => info.text = Ar6dofLoc.F("Sons en cours : {0}", a.Meters.voices));

            Ar6dofVrUi.Label(content, "Micro", new Vector2(px + 40f, -(top + 346f)), new Vector2(80f, 32f), 19, TextAnchor.MiddleLeft);
            var mic = Ar6dofMeterBar.Make(content, new Vector2(px + 84f + (pw - 84f) * 0.5f, -(top + 346f)), new Vector2(pw - 84f, 28f));
            bars.Add(mic); barSel.Add(m => m.peak[0]);

            Text t1, t2;
            BtnAt(pc, -(top + 398f), pw, 48f, "Tout arreter (sons et musique)", () => { a.StopAllSounds(); a.StopMusic(); Refresh(); }, out t1, 20);
            BtnAt(pc, -(top + 454f), pw, 48f, "Remettre les tranches a zero", () =>
            {
                for (int c = 0; c < 4; c++) a.ResetStrip(c);
                Rebuild();
            }, out t2, 20);
            var hint = Ar6dofVrUi.Label(content, Ar6dofLoc.T("Touche une valeur (balance, graves, medium, aigus) pour la remettre a zero. Limiteur : plafond 0 dB, doux des -3 dB. Le vumetre Principal est avant le limiteur : voyant rouge = ca sature."), new Vector2(pc, -(top + 520f)), new Vector2(pw, 74f), 15, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Truncate;

            y = top + StripH + 6f;
        }

        void BuildStrip(Ar6dofAudio a, int c, float cx, float top)
        {
            bool master = c == 3;
            // fond de la tranche
            var bgRt = Ar6dofVrUi.Rect(content, "Tranche", new Vector2(cx, -(top + StripH * 0.5f)), new Vector2(StripW, StripH));
            var bgImg = bgRt.gameObject.AddComponent<Image>();
            bgImg.raycastTarget = false;
            Ar6dofTheme.Attach(bgRt.gameObject, Ar6dofRole.Band, 14);

            var name = Ar6dofVrUi.Label(content, StripNames[c], new Vector2(cx, -(top + 18f)), new Vector2(StripW - 8f, 30f), 22);
            name.fontStyle = FontStyle.Bold;
            if (master) name.MakeAccent();
            name.gameObject.AddComponent<Ar6dofFitText>().Init(name, 22);

            // fader + vu-mètre vertical
            float fy = -(top + 36f + FaderH * 0.5f);
            Slider fader = Ar6dofVrUi.MakeVSlider(content, new Vector2(cx - 28f, fy), new Vector2(62f, FaderH), 0f, 1f, GainToPos(a.data.vol[c]), p => { a.SetVolume(c, PosToGain(p)); });
            var tickRt = Ar6dofVrUi.Rect(content, "ZeroDB", new Vector2(cx - 28f, -(top + 36f + 16f + (1f - GainToPos(1f)) * (FaderH - 32f))), new Vector2(76f, 2f));
            var tickImg = tickRt.gameObject.AddComponent<Image>();
            tickImg.raycastTarget = false;
            Ar6dofTheme.Attach(tickRt.gameObject, Ar6dofRole.Dim, 1);
            var meter = Ar6dofMeterBar.Make(content, new Vector2(cx + 40f, fy), new Vector2(30f, FaderH), true);
            bars.Add(meter);
            if (c == 0) barSel.Add(m => m.peak[1]);
            else if (c == 1) barSel.Add(m => m.peak[2]);
            else if (c == 2) barSel.Add(m => m.peak[3]);
            else barSel.Add(m => Mathf.Max(m.peak[4], m.peak[5]));

            // valeur du fader en dB : toucher = 0 dB
            Text rt;
            var rb = BtnAt(cx, -(top + 314f), StripW - 16f, 30f, "", () => { a.SetVolume(c, 1f); Refresh(); }, out rt, 18);
            Text rtt = rt;
            Slider fd = fader;

            // muet / solo (ou limiteur pour le général)
            Text mt, st;
            float bw = (StripW - 8f - 6f) * 0.5f;
            var mb = BtnAt(cx - bw * 0.5f - 3f, -(top + 356f), bw, 44f, "Muet", () => { a.SetMute(c, !a.data.mute[c]); Refresh(); }, out mt, 17);
            Button sb;
            if (!master) sb = BtnAt(cx + bw * 0.5f + 3f, -(top + 356f), bw, 44f, "Solo", () => { a.SetSolo(c, !a.data.solo[c]); Refresh(); }, out st, 17);
            else sb = BtnAt(cx + bw * 0.5f + 3f, -(top + 356f), bw, 44f, "Limiteur", () => { a.SetLimiter(!a.data.limiter); Refresh(); }, out st, 17);

            // balance
            Text pv = MiniStep(cx, -(top + 404f), StripW - 8f, "", () => a.SetPan(c, Mathf.Max(-1f, Mathf.Round(a.data.pan[c] * 10f) / 10f - 0.1f)),
                () => a.SetPan(c, Mathf.Min(1f, Mathf.Round(a.data.pan[c] * 10f) / 10f + 0.1f)), () => a.SetPan(c, 0f), 0f);

            // égaliseur 3 bandes
            var ev = new Text[3];
            for (int b = 0; b < 3; b++)
            {
                int band = b;
                ev[b] = MiniStep(cx, -(top + 452f + b * 44f), StripW - 8f, EqNames[b],
                    () => a.SetEq(c, band, Mathf.Max(-12f, a.data.eq[c * 3 + band] - 2f)),
                    () => a.SetEq(c, band, Mathf.Min(12f, a.data.eq[c * 3 + band] + 2f)),
                    () => a.SetEq(c, band, 0f), 28f);
            }

            refreshers.Add(() =>
            {
                float g = a.data.vol[c];
                fd.SetValueWithoutNotify(GainToPos(g));
                rtt.text = DbText(g);
                Ar6dofTheme.SetSelected(mb, a.data.mute[c]);
                Ar6dofTheme.SetSelected(sb, master ? a.data.limiter : a.data.solo[c]);
                int pan = Mathf.RoundToInt(a.data.pan[c] * 100f);
                pv.text = pan == 0 ? "C" : (pan < 0 ? "L " + (-pan) : "R " + pan);
                for (int b2 = 0; b2 < 3; b2++)
                {
                    int e = Mathf.RoundToInt(a.data.eq[c * 3 + b2]);
                    ev[b2].text = e > 0 ? "+" + e : e.ToString();
                }
            });
        }

        /// <summary>Petit réglage compact : [-] valeur [+] avec une étiquette facultative à gauche. Toucher la valeur la remet à zéro. Renvoie le texte de la valeur.</summary>
        Text MiniStep(float cx, float cy, float w, string label, Action minus, Action plus, Action zero, float labelW)
        {
            const float bw = 34f, h = 38f, g = 4f;
            float x = cx - w * 0.5f;
            if (labelW > 0f)
            {
                var l = Ar6dofVrUi.Label(content, Ar6dofLoc.T(label), new Vector2(x + labelW * 0.5f, cy), new Vector2(labelW, h), 17, TextAnchor.MiddleLeft);
                l.gameObject.AddComponent<Ar6dofFitText>().Init(l, 17);
                x += labelW + g;
            }
            float vw = w - (labelW > 0f ? labelW + g : 0f) - 2f * bw - 2f * g;
            Text a1, a2, a3;
            BtnAt(x + bw * 0.5f, cy, bw, h, "-", () => { minus(); Refresh(); }, out a1, 22);
            BtnAt(x + bw + g + vw * 0.5f, cy, vw, h, "", () => { zero(); Refresh(); }, out a2, 17);
            BtnAt(x + bw + g + vw + g + bw * 0.5f, cy, bw, h, "+", () => { plus(); Refresh(); }, out a3, 22);
            return a2;
        }

        // ------------------------------------------------------------------ musique
        void Music(Ar6dofAudio a)
        {
            var now = Lbl("", 36, 24, TextAnchor.MiddleCenter);
            now.fontStyle = FontStyle.Bold;
            refreshers.Add(() =>
            {
                if (!string.IsNullOrEmpty(a.musicError)) now.text = a.musicError;
                else now.text = a.data.playlist.Count == 0 ? Ar6dofLoc.T("Liste de lecture vide") : (a.data.track < 0 ? Ar6dofLoc.T("Aucune piste") : a.TrackName);
            });

            float cy = RowY(46f);
            float x0 = -IW * 0.5f;
            timeLbl = Ar6dofVrUi.Label(content, "0:00 / 0:00", new Vector2(x0 + 80f, cy), new Vector2(160f, 40f), 20, TextAnchor.MiddleLeft);
            seek = Ar6dofVrUi.MakeSlider(content, new Vector2(x0 + 168f + (IW - 168f) * 0.5f, cy), new Vector2(IW - 168f, 38f), 0f, 1f, 0f, v =>
            {
                lastUserSeek = Time.unscaledTime;
                seekPending = true;
            });
            refreshers.Add(() =>
            {
                var m = a.Meters;
                float dur = Mathf.Max(1f, m.musicDur);
                seek.maxValue = dur;
                if (Time.unscaledTime - lastUserSeek > 1.0f) seek.SetValueWithoutNotify(Mathf.Clamp(m.musicPos, 0f, dur));
                timeLbl.text = Clock(m.musicPos) + " / " + Clock(m.musicDur);
            });

            Action playAct = () =>
            {
                if (!a.MusicActive) { if (a.data.playlist.Count > 0) a.PlayTrack(a.data.track >= 0 ? a.data.track : 0); }
                else a.PauseMusic(!a.Meters.MusicPaused);
            };
            var pb = BtnRow(56f, 22, "|<", (Action)(() => { a.PrevTrack(); }), "Lire", playAct, "Stop", (Action)(() => { a.StopMusic(); }), ">|", (Action)(() => { a.NextTrack(false); }));
            var playTxt = pb[1].GetComponentInChildren<Text>();
            refreshers.Add(() => playTxt.text = Ar6dofLoc.T(a.MusicActive && !a.Meters.MusicPaused ? "Pause" : "Lire"));

            var rb = BtnRow(48f, 20, "", (Action)(() => { a.SetRepeat((a.data.repeat + 1) % 3); Refresh(); }), "", (Action)(() => { a.data.shuffle = !a.data.shuffle; a.MarkDirty(); Refresh(); }));
            var repTxt = rb[0].GetComponentInChildren<Text>();
            var shTxt = rb[1].GetComponentInChildren<Text>();
            var shBtn = rb[1];
            refreshers.Add(() =>
            {
                repTxt.text = Ar6dofLoc.T("Repeter : ") + Ar6dofLoc.T(a.data.repeat == 0 ? "Non" : a.data.repeat == 1 ? "Tout" : "Une piste");
                shTxt.text = Ar6dofLoc.T("Aleatoire : ") + Ar6dofLoc.T(a.data.shuffle ? "Oui" : "Non");
                Ar6dofTheme.SetSelected(shBtn, a.data.shuffle);
            });

            BtnRow(48f, 20, "Ajouter des musiques", (Action)(() =>
            {
                Browse("Ajouter des musiques", AudioExt, true, files =>
                {
                    foreach (var f in files) if (!a.data.playlist.Contains(f)) a.data.playlist.Add(f);
                    if (a.data.track < 0 && a.data.playlist.Count > 0) a.data.track = 0;
                    a.MarkDirty();
                    Rebuild();
                }, Ar6dofLibrary.Music);
            }), "Vider la liste", (Action)(() => { a.StopMusic(); a.data.playlist.Clear(); a.data.track = -1; a.MarkDirty(); Rebuild(); }));

            // liste de lecture
            int n = a.data.playlist.Count;
            int pages = Mathf.Max(1, (n + PlPerPage - 1) / PlPerPage);
            plPage = Mathf.Clamp(plPage, 0, pages - 1);
            for (int i = plPage * PlPerPage; i < Mathf.Min(n, (plPage + 1) * PlPerPage); i++)
            {
                int k = i;
                float ry = RowY(44f);
                Text tt;
                var tb = BtnAt(x0 + (IW - 60f) * 0.5f, ry, IW - 60f, 44f, (k + 1) + ".  " + System.IO.Path.GetFileNameWithoutExtension(a.data.playlist[k]), () => a.PlayTrack(k), out tt, 20);
                Text xt;
                BtnAt(x0 + IW - 26f, ry, 52f, 44f, "X", () =>
                {
                    a.data.playlist.RemoveAt(k);
                    if (a.data.track == k) { a.StopMusic(); a.data.track = Mathf.Min(k, a.data.playlist.Count - 1); }
                    else if (a.data.track > k) a.data.track--;
                    a.MarkDirty();
                    Rebuild();
                }, out xt, 20);
                refreshers.Add(() => Ar6dofTheme.SetSelected(tb, a.data.track == k));
            }
            if (pages > 1)
                BtnRow(44f, 21, "<", (Action)(() => { plPage = (plPage + pages - 1) % pages; Rebuild(); }), Ar6dofLoc.F("Page {0}/{1}", plPage + 1, pages), (Action)(() => { }), ">", (Action)(() => { plPage = (plPage + 1) % pages; Rebuild(); }));
        }

        static string Clock(float s)
        {
            if (s < 0f || float.IsNaN(s)) s = 0f;
            int t = Mathf.RoundToInt(s);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

        protected override void OnTick()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null || !a.Online) return;
            a.WantMeters();
            var m = a.Meters;
            for (int i = 0; i < bars.Count; i++) bars[i].SetPeak(barSel[i](m));
            if (viz != null) viz.Feed(m, Time.unscaledTime);
            if (seekPending && seek != null && Time.unscaledTime - lastUserSeek > 0.2f) { seekPending = false; a.SeekMusic(seek.value); }
        }
    }
}
