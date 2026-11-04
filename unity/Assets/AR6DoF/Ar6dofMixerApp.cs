using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Table de mixage : volumes, muets et niveaux de la voix, du soundboard et de la musique, volume général, spectre de la sortie, et un lecteur de musique
    /// avec liste de lecture (pour un fursuiter qui joue de la musique sur ses enceintes). Tout sort sur les enceintes choisies dans la fenêtre Voix.
    /// </summary>
    public class Ar6dofMixerApp : Ar6dofAppWindow
    {
        static readonly string[] AudioExt = { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".mp4" };
        int tab;
        int plPage;
        readonly List<Ar6dofMeterBar> bars = new List<Ar6dofMeterBar>();
        readonly List<Func<AudioMeters, float>> barSel = new List<Func<AudioMeters, float>>();
        Ar6dofSpectrum spec;
        Slider seek;
        Text timeLbl;
        float lastUserSeek = -10f;
        bool seekPending;
        const int PlPerPage = 6;

        public Ar6dofMixerApp(Ar6dofDock d) : base(d, "mixer", "Table de mixage", "mixer", "Volumes, muets, niveaux, spectre et lecteur de musique", 720f) { }

        protected override void OnRebuildStart() { bars.Clear(); barSel.Clear(); spec = null; seek = null; timeLbl = null; }

        protected override void BuildMain()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 40, 22); return; }
            Tabs(new[] { "Table", "Musique" }, tab, t => tab = t);
            EngineRow();
            if (tab == 0) Table(a); else Music(a);
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
            // micro : seulement un niveau (le gain se règle dans Voix > Base)
            float cy = RowY(44f);
            float x0 = -IW * 0.5f;
            Ar6dofVrUi.Label(content, "Micro", new Vector2(x0 + 60f, cy), new Vector2(120f, 44f), 22, TextAnchor.MiddleLeft);
            var mic = Ar6dofMeterBar.Make(content, new Vector2(x0 + 128f + (IW - 128f) * 0.5f, cy), new Vector2(IW - 128f, 30f));
            bars.Add(mic); barSel.Add(m => m.peak[0]);
            ChannelRow(a, "Voix", 0, m => m.peak[1]);
            ChannelRow(a, "Soundboard", 1, m => m.peak[2]);
            ChannelRow(a, "Musique", 2, m => m.peak[3]);
            ChannelRow(a, "General", 3, m => Mathf.Max(m.peak[4], m.peak[5]));
            Section("Spectre de la sortie");
            spec = Ar6dofSpectrum.Make(content, new Vector2(0f, RowY(110f)), new Vector2(IW, 110f), 32);
            var info = Lbl("", 26, 18);
            refreshers.Add(() => info.text = Ar6dofLoc.F("Sons en cours : {0}", a.Meters.voices));
            Btn("Arreter tous les sons", () => a.StopAllSounds(), 48f, 22);
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
            if (spec != null) spec.Set(m.specOut);
            if (seekPending && seek != null && Time.unscaledTime - lastUserSeek > 0.2f) { seekPending = false; a.SeekMusic(seek.value); }
        }
    }
}
