using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Application « Changeur de voix » : modifie ta voix (hauteur, robot, écho, réverbération, filtres, porte de bruit, compresseur) avec des présélections
    /// et la visualisation du son. Le micro, les enceintes et le moteur se choisissent dans l'application Audio. Tout le traitement est fait par
    /// ar6dof_audio.exe, lancé seulement quand une application de son est ouverte.
    /// </summary>
    public class Ar6dofVoiceApp : Ar6dofAppWindow
    {
        static readonly string[] TabNames = { "Preselections", "Base", "Voix", "Espace", "Filtres" };
        static readonly string[] TabKeys = { "presets", "base", "fx", "space", "filter" };
        int tab;
        Ar6dofSpectrum specIn, specOut;
        Ar6dofMeterBar barIn, barOut;

        public Ar6dofVoiceApp(Ar6dofDock d) : base(d, "voice", "Changeur de voix", "mic", "Voix modifiee (hauteur, robot, echo, filtres...) avec presets et vision du son", 680f) { }

        protected override void OnRebuildStart() { specIn = specOut = null; barIn = barOut = null; }

        protected override void BuildMain()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 40, 22); return; }
            Tabs(TabNames, tab, t => tab = t);
            if (tab == 0) Presets(a);
            else Sliders(a, TabKeys[tab]);

            // visualisation du son : micro (avant effets) et sortie (tout ce qui sort sur les enceintes)
            Space(4f);
            float cy = RowY(26f);
            Ar6dofVrUi.Label(content, "Micro", new Vector2(-IW * 0.25f, cy), new Vector2(IW * 0.5f, 26f), 19);
            Ar6dofVrUi.Label(content, "Sortie", new Vector2(IW * 0.25f, cy), new Vector2(IW * 0.5f, 26f), 19);
            float cy2 = RowY(90f);
            specIn = Ar6dofSpectrum.Make(content, new Vector2(-IW * 0.25f - 2f, cy2), new Vector2(IW * 0.5f - 6f, 90f), 32);
            specOut = Ar6dofSpectrum.Make(content, new Vector2(IW * 0.25f + 2f, cy2), new Vector2(IW * 0.5f - 6f, 90f), 32);
            float cy3 = RowY(26f);
            barIn = Ar6dofMeterBar.Make(content, new Vector2(-IW * 0.25f - 2f, cy3), new Vector2(IW * 0.5f - 6f, 26f));
            barOut = Ar6dofMeterBar.Make(content, new Vector2(IW * 0.25f + 2f, cy3), new Vector2(IW * 0.5f - 6f, 26f));
        }

        void Presets(Ar6dofAudio a)
        {
            if (!a.Enabled) EngineRow();   // le micro et les enceintes se choisissent dans l'application Audio
            ToggleRow("Effets de voix", () => a.GetVoice("on", 1f) > 0.5f, v => a.SetVoice("on", v ? 1f : 0f));
            if (a.data.inDev == "off") Para("Choisis ton micro dans l'application Audio.", 30, 19);
            Section("Preselections");
            var items = new List<object>();
            for (int i = 0; i < 4; i++) { int k = i; items.Add(Ar6dofAudio.PresetNames[i]); items.Add((Action)(() => { a.ApplyPreset(k); Refresh(); })); }
            BtnRow(48f, 21, items.ToArray());
            items.Clear();
            for (int i = 4; i < Ar6dofAudio.PresetNames.Length; i++) { int k = i; items.Add(Ar6dofAudio.PresetNames[i]); items.Add((Action)(() => { a.ApplyPreset(k); Refresh(); })); }
            BtnRow(48f, 21, items.ToArray());
        }

        void Sliders(Ar6dofAudio a, string tabKey)
        {
            if (tabKey == "base")
            {
                var g = Lbl("", 28, 19);
                refreshers.Add(() => g.text = Ar6dofLoc.T("Porte de bruit : ") + Ar6dofLoc.T(a.Meters.GateOpen ? "ouverte" : "fermee"));
                ToggleRow("Porte de bruit", () => a.GetVoice("gate", 1f) > 0.5f, v => a.SetVoice("gate", v ? 1f : 0f), 48f, 22);
                ToggleRow("Compresseur", () => a.GetVoice("comp", 1f) > 0.5f, v => a.SetVoice("comp", v ? 1f : 0f), 48f, 22);
            }
            foreach (var k in Ar6dofAudio.VoiceKeys)
            {
                if (k.tab != tabKey) continue;
                var kk = k;
                SliderRow(kk.label, kk.min, kk.max, () => a.GetVoice(kk.key, kk.def), v => a.SetVoice(kk.key, v), v => Fmt(kk.fmt, kk.fmt.Contains("%") ? v * 100f : v), 54f, kk.key == "crush");
            }
        }

        protected override void OnTick()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null || !a.Online || specIn == null) return;
            a.WantMeters();
            var m = a.Meters;
            specIn.Set(m.specIn);
            specOut.Set(m.specOut);
            barIn.SetPeak(m.peak[0]);
            barOut.SetPeak(Mathf.Max(m.peak[4], m.peak[5]));
        }
    }
}
