using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Application « Audio » : le moteur audio, le micro et les enceintes externes, au même endroit pour toutes les applications de son
    /// (Changeur de voix, Soundboard, Table de mixage). Le son de Windows et de la réalité augmentée reste dans le casque : seuls ta voix,
    /// le soundboard et la musique sortent sur l'appareil choisi ici.
    /// </summary>
    public class Ar6dofAudioDevicesApp : Ar6dofAppWindow
    {
        Ar6dofMeterBar barIn, barOut;

        public Ar6dofAudioDevicesApp(Ar6dofDock d) : base(d, "audio", "Audio", "speaker", "Moteur audio, micro et enceintes externes", 640f) { }

        protected override void OnRebuildStart() { barIn = barOut = null; }

        protected override void BuildMain()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 40, 22); return; }
            EngineRow();
            Section("Appareils");
            Text t1, t2;
            BtnT("", () => PickDevice(a, true), out t1, 52f, 22);
            BtnT("", () => PickDevice(a, false), out t2, 52f, 22);
            refreshers.Add(() => { t1.text = Ar6dofLoc.T("Micro : ") + a.InputLabel(); t2.text = Ar6dofLoc.T("Enceintes : ") + a.OutputLabel(); });
            var st = Lbl("", 30, 18);
            refreshers.Add(() => st.text = Ar6dofLoc.T("Micro ") + StatusWord(a.InStatus) + "  -  " + Ar6dofLoc.T("Enceintes ") + StatusWord(a.OutStatus));
            Para(a.Online ? "Choisis tes enceintes externes : le casque garde le son de Windows et de la realite augmentee." : "Active le moteur audio pour lister tes micros et tes enceintes.", 56, 18);

            Section("Niveaux");
            float cy = RowY(30f);
            float x0 = -IW * 0.5f;
            Ar6dofVrUi.Label(content, "Micro", new Vector2(x0 + 60f, cy), new Vector2(120f, 30f), 20, TextAnchor.MiddleLeft);
            barIn = Ar6dofMeterBar.Make(content, new Vector2(x0 + 128f + (IW - 128f) * 0.5f, cy), new Vector2(IW - 128f, 26f));
            float cy2 = RowY(30f);
            Ar6dofVrUi.Label(content, "Sortie", new Vector2(x0 + 60f, cy2), new Vector2(120f, 30f), 20, TextAnchor.MiddleLeft);
            barOut = Ar6dofMeterBar.Make(content, new Vector2(x0 + 128f + (IW - 128f) * 0.5f, cy2), new Vector2(IW - 128f, 26f));
        }

        public static string StatusWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return "...";
            if (s == "ok") return Ar6dofLoc.T("ok");
            return Ar6dofLoc.T("erreur ") + s.Replace("err ", "");
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
            if (a == null || !a.Online || barIn == null) return;
            a.WantMeters();
            var m = a.Meters;
            barIn.SetPeak(m.peak[0]);
            barOut.SetPeak(Mathf.Max(m.peak[4], m.peak[5]));
        }
    }
}
