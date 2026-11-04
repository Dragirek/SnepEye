using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Application « Voix » : choisis ton micro et tes enceintes externes, puis modifie ta voix (hauteur, robot, écho, réverbération, filtres, porte de bruit,
    /// compresseur) avec la visualisation du son. Le son de Windows et de la réalité augmentée reste dans le casque : ici, seule ta voix (et le soundboard / la
    /// musique) sort sur l'appareil choisi. Tout le traitement est fait par ar6dof_audio.exe, lancé seulement quand le moteur audio est activé.
    /// </summary>
    public class Ar6dofVoiceApp : Ar6dofAppWindow
    {
        static readonly string[] TabNames = { "General", "Base", "Voix", "Espace", "Filtres" };
        static readonly string[] TabKeys = { "general", "base", "fx", "space", "filter" };
        int tab;
        Ar6dofSpectrum specIn, specOut;
        Ar6dofMeterBar barIn, barOut;

        public Ar6dofVoiceApp(Ar6dofDock d) : base(d, "voice", "Voix", "mic", "Micro, voix modifiee et sortie sur des enceintes externes", 680f) { }

        protected override void OnRebuildStart() { specIn = specOut = null; barIn = barOut = null; }

        protected override void BuildMain()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 40, 22); return; }
            Tabs(TabNames, tab, t => tab = t);
            if (tab == 0) General(a);
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

        void General(Ar6dofAudio a)
        {
            EngineRow();
            Text t1, t2;
            BtnT("", () => PickDevice(a, true), out t1, 52f, 22);
            BtnT("", () => PickDevice(a, false), out t2, 52f, 22);
            refreshers.Add(() => { t1.text = Ar6dofLoc.T("Micro : ") + a.InputLabel(); t2.text = Ar6dofLoc.T("Enceintes : ") + a.OutputLabel(); });
            var st = Lbl("", 30, 18);
            refreshers.Add(() => st.text = Ar6dofLoc.T("Micro ") + StatusWord(a.InStatus) + "  -  " + Ar6dofLoc.T("Enceintes ") + StatusWord(a.OutStatus));
            Para(a.Online ? "Choisis tes enceintes externes : le casque garde le son de Windows et de la realite augmentee." : "Active le moteur audio pour lister tes micros et tes enceintes.", 56, 18);
            ToggleRow("Effets de voix", () => a.GetVoice("on", 1f) > 0.5f, v => a.SetVoice("on", v ? 1f : 0f));
            Section("Preselections");
            var items = new List<object>();
            for (int i = 0; i < 4; i++) { int k = i; items.Add(Ar6dofAudio.PresetNames[i]); items.Add((Action)(() => { a.ApplyPreset(k); Refresh(); })); }
            BtnRow(48f, 21, items.ToArray());
            items.Clear();
            for (int i = 4; i < Ar6dofAudio.PresetNames.Length; i++) { int k = i; items.Add(Ar6dofAudio.PresetNames[i]); items.Add((Action)(() => { a.ApplyPreset(k); Refresh(); })); }
            BtnRow(48f, 21, items.ToArray());
        }

        static string StatusWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return "...";
            if (s == "ok") return Ar6dofLoc.T("ok");
            return Ar6dofLoc.T("erreur ") + s.Replace("err ", "");
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

        void PickDevice(Ar6dofAudio a, bool input)
        {
            a.RequestDevices();
            var items = new List<string> { Ar6dofLoc.T("Desactive"), Ar6dofLoc.T("Par defaut de Windows") };
            var list = input ? a.inputs : a.outputs;
            foreach (var d in list) items.Add(d.name + (d.isDefault ? "  (" + Ar6dofLoc.T("defaut") + ")" : ""));
            string cur = input ? a.data.inDev : a.data.outDev;
            int ci = cur == "off" ? 0 : cur == "default" ? 1 : -1;
            if (ci < 0) for (int i = 0; i < list.Count; i++) if (list[i].id == cur) ci = i + 2;
            var snapshot = new List<AudioDevice>(list);
            Pick(input ? "Choisir le micro" : "Choisir les enceintes externes", items, ci, i =>
            {
                string id = i == 0 ? "off" : i == 1 ? "default" : snapshot[i - 2].id;
                string name = i < 2 ? "" : snapshot[i - 2].name;
                if (input) a.SetInput(id, name); else a.SetOutput(id, name);
                if (id != "off" && !a.Enabled) a.SetEnabled(true);
                Refresh();
            });
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
