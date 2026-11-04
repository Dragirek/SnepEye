using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Soundboard : des boutons qui lancent des sons ou des effets sur les enceintes externes, sans passer par la modification de voix.
    /// Chaque bouton a son fichier, sa couleur, son volume, sa vitesse, son panoramique, un mode boucle et sa taille (en cases). Un même bouton peut être
    /// pressé plusieurs fois de suite : les sons s'ajoutent sans couper le précédent. Disposition : grille, liste, flux (retour à la ligne) ou cercle.
    /// </summary>
    public class Ar6dofSoundboardApp : Ar6dofAppWindow
    {
        static readonly string[] AudioExt = { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".mp4" };
        static readonly string[] LayoutNames = { "Grille", "Liste", "Flux", "Cercle" };
        bool edit, confirmDel;
        PadDef editing;
        readonly List<KeyValuePair<Text, PadDef>> padTexts = new List<KeyValuePair<Text, PadDef>>();

        public Ar6dofSoundboardApp(Ar6dofDock d) : base(d, "soundboard", "Soundboard", "pads", "Boutons de sons et d'effets sur les enceintes externes", 780f) { }

        protected override void OnRebuildStart() { padTexts.Clear(); }

        protected override void OnVisible(bool on) { if (!on) { edit = false; editing = null; confirmDel = false; } }

        protected override void BuildMain()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 40, 22); return; }
            if (editing != null && a.data.pads.Contains(editing)) { BuildPadEditor(a, editing); return; }
            editing = null;

            if (!a.Enabled) EngineRow();
            else if (a.data.outDev == "off") Para("Choisis tes enceintes externes dans l'application Voix.", 30, 19);

            var bar = new List<object>();
            bar.Add("Edition"); bar.Add((Action)(() => { edit = !edit; Rebuild(); }));
            bar.Add("Ajouter des sons"); bar.Add((Action)(() => AddSounds(a)));
            bar.Add("Tout arreter"); bar.Add((Action)(() => a.StopAllSounds()));
            var bs = BtnRow(52f, 21, bar.ToArray());
            Ar6dofTheme.SetSelected(bs[0], edit);

            if (edit)
            {
                var lt = Btn("", () => { a.data.layout = (a.data.layout + 1) % LayoutNames.Length; a.MarkDirty(); Rebuild(); }, 48f, 21);
                lt.GetComponentInChildren<Text>().text = Ar6dofLoc.T("Disposition : ") + Ar6dofLoc.T(LayoutNames[Mathf.Clamp(a.data.layout, 0, LayoutNames.Length - 1)]);
                if (a.data.layout == 0)
                    SliderRow("Colonnes", 1, 10, () => a.data.cols, v => { a.data.cols = Mathf.RoundToInt(v); a.MarkDirty(); RebuildPadsSoon(); }, v => Mathf.RoundToInt(v).ToString(), 50f, true);
                SliderRow("Taille des boutons", 70, 240, () => a.data.cell, v => { a.data.cell = v; a.MarkDirty(); RebuildPadsSoon(); }, v => Mathf.RoundToInt(v) + " px", 50f);
                Para("Touche un bouton pour le modifier (nom, fichier, couleur, volume, taille...).", 30, 18);
            }

            Space(4f);
            BuildPads(a);
        }

        // la disposition change pendant que le curseur bouge : on reconstruit à la fin du mouvement (pas à chaque valeur)
        float rebuildAt = -1f;
        void RebuildPadsSoon() { rebuildAt = Time.unscaledTime + 0.25f; }

        protected override void OnTick()
        {
            if (rebuildAt > 0f && Time.unscaledTime > rebuildAt) { rebuildAt = -1f; Rebuild(); }
        }

        void AddSounds(Ar6dofAudio a)
        {
            Browse("Choisir des sons", AudioExt, true, files =>
            {
                foreach (var f in files) a.AddPad(f);
                if (!a.Enabled) a.SetEnabled(true);
                Rebuild();
            }, Ar6dofLibrary.Sounds);
        }

        // ------------------------------------------------------------------ disposition des boutons
        static bool Fits(List<bool[]> occ, int r, int c, int w, int h, int cols)
        {
            while (occ.Count < r + h) occ.Add(new bool[cols]);
            for (int i = r; i < r + h; i++) for (int j = c; j < c + w; j++) if (occ[i][j]) return false;
            return true;
        }

        Vector2 Layout(AudioData d, List<Rect> rects)
        {
            const float gap = 8f;
            float avail = IW;
            int n = d.pads.Count;
            switch (d.layout)
            {
                case 1:   // liste : un bouton par ligne, sur toute la largeur
                {
                    float rowH = Mathf.Clamp(d.cell * 0.4f, 46f, 90f), yy = 0f;
                    foreach (var p in d.pads)
                    {
                        int hh = Mathf.Max(1, p.h);
                        float h = rowH * hh + (hh - 1) * gap;
                        rects.Add(new Rect(0f, yy, avail, h));
                        yy += h + gap;
                    }
                    return new Vector2(avail, Mathf.Max(0f, yy - gap));
                }
                case 2:   // flux : les boutons se suivent et passent à la ligne
                {
                    float cell = Mathf.Min(d.cell, avail), x = 0f, yy = 0f, rowH = 0f;
                    foreach (var p in d.pads)
                    {
                        float w = Mathf.Min(avail, Mathf.Max(1, p.w) * cell + (Mathf.Max(1, p.w) - 1) * gap);
                        float h = Mathf.Max(1, p.h) * cell + (Mathf.Max(1, p.h) - 1) * gap;
                        if (x > 0f && x + w > avail + 0.5f) { x = 0f; yy += rowH + gap; rowH = 0f; }
                        rects.Add(new Rect(x, yy, w, h));
                        x += w + gap;
                        rowH = Mathf.Max(rowH, h);
                    }
                    return new Vector2(avail, n == 0 ? 0f : yy + rowH);
                }
                case 3:   // cercle
                {
                    float cell = Mathf.Min(d.cell, 150f);
                    float radius = Mathf.Max(cell * 1.15f, n * (cell + gap) / (2f * Mathf.PI));
                    float size = 2f * radius + cell;
                    if (size > avail) { float f = avail / size; cell *= f; radius *= f; size = avail; }
                    for (int i = 0; i < n; i++)
                    {
                        var p = d.pads[i];
                        float ang = -Mathf.PI * 0.5f + 2f * Mathf.PI * i / Mathf.Max(1, n);
                        float cx = size * 0.5f + radius * Mathf.Cos(ang), cy = size * 0.5f + radius * Mathf.Sin(ang);
                        float w = cell * Mathf.Clamp(p.w, 1, 3) * 0.8f, h = cell * Mathf.Clamp(p.h, 1, 3) * 0.8f;
                        rects.Add(new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h));
                    }
                    return new Vector2(size, size);
                }
                default:   // grille
                {
                    int cols = Mathf.Clamp(d.cols, 1, 10);
                    float cell = Mathf.Min(d.cell, (avail - (cols - 1) * gap) / cols);
                    var occ = new List<bool[]>();
                    foreach (var p in d.pads)
                    {
                        int w = Mathf.Min(Mathf.Max(1, p.w), cols), h = Mathf.Max(1, p.h);
                        int rr = 0, cc = 0;
                        bool found = false;
                        for (rr = 0; !found; rr++)
                        {
                            for (cc = 0; cc + w <= cols; cc++) if (Fits(occ, rr, cc, w, h, cols)) { found = true; break; }
                            if (found) break;
                        }
                        for (int i = rr; i < rr + h; i++) for (int j = cc; j < cc + w; j++) occ[i][j] = true;
                        rects.Add(new Rect(cc * (cell + gap), rr * (cell + gap), w * cell + (w - 1) * gap, h * cell + (h - 1) * gap));
                    }
                    return new Vector2(cols * cell + (cols - 1) * gap, occ.Count == 0 ? 0f : occ.Count * cell + (occ.Count - 1) * gap);
                }
            }
        }

        void BuildPads(Ar6dofAudio a)
        {
            if (a.data.pads.Count == 0) { Para("Aucun bouton : appuie sur « Ajouter des sons » pour choisir tes fichiers (wav, mp3, m4a, aac, wma, flac).", 60, 20); return; }
            var rects = new List<Rect>();
            Vector2 total = Layout(a.data, rects);
            float areaH = Mathf.Max(60f, total.y);
            float cy = RowY(areaH);
            var area = Ar6dofVrUi.Rect(content, "Boutons", new Vector2(0f, cy), new Vector2(IW, areaH));
            float offX = (IW - total.x) * 0.5f;
            for (int i = 0; i < a.data.pads.Count; i++)
            {
                var r = rects[i];
                var center = new Vector2(-IW * 0.5f + offX + r.x + r.width * 0.5f, areaH * 0.5f - r.y - r.height * 0.5f);
                MakePad(area, a.data.pads[i], center, r.size, a);
            }
        }

        void MakePad(RectTransform area, PadDef p, Vector2 center, Vector2 size, Ar6dofAudio a)
        {
            var rt = Ar6dofVrUi.Rect(area, "Bouton son", center, size);
            var img = rt.gameObject.AddComponent<Image>();
            Color col = new Color(p.r, p.g, p.b, 1f);
            img.color = col;
            Ar6dofTheme.SetRounded(img, 22);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            cb.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            cb.selectedColor = Color.white;
            b.colors = cb;
            PadDef pad = p;
            b.onClick.AddListener(() =>
            {
                if (edit) { editing = pad; confirmDel = false; Rebuild(); }
                else a.PlayPad(pad);
            });
            var lbl = Ar6dofVrUi.Label(rt, p.label, Vector2.zero, size - new Vector2(14f, 14f), 24, TextAnchor.MiddleCenter);
            lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
            lbl.verticalOverflow = VerticalWrapMode.Truncate;
            lbl.resizeTextForBestFit = true;
            lbl.resizeTextMinSize = 10;
            lbl.resizeTextMaxSize = 30;
            lbl.fontStyle = FontStyle.Bold;
            var th = lbl.GetComponent<Ar6dofThemed>();
            if (th != null) UnityEngine.Object.Destroy(th);
            lbl.color = Ar6dofTheme.OnColor(col);
            padTexts.Add(new KeyValuePair<Text, PadDef>(lbl, p));
            refreshers.Add(() =>
            {
                int st;
                a.soundState.TryGetValue(pad.slot, out st);
                string prefix = st == 2 ? "! " : (a.loopingSlots.Contains(pad.slot) ? "o " : "");
                lbl.text = prefix + pad.label;
            });
        }

        // ------------------------------------------------------------------ éditeur d'un bouton
        static readonly Color[] Palette =
        {
            new Color(0.90f, 0.30f, 0.30f), new Color(0.95f, 0.60f, 0.20f), new Color(0.95f, 0.85f, 0.25f), new Color(0.40f, 0.80f, 0.40f),
            new Color(0.25f, 0.75f, 0.75f), new Color(0.25f, 0.50f, 0.90f), new Color(0.60f, 0.40f, 0.90f), new Color(0.90f, 0.40f, 0.70f),
            new Color(0.80f, 0.80f, 0.82f), new Color(0.35f, 0.36f, 0.40f)
        };

        void Stepper(string label, Func<int> get, Action<int> set, int min, int max)
        {
            float cy = RowY(50f);
            float x0 = -IW * 0.5f;
            var l = Ar6dofVrUi.Label(content, label, new Vector2(x0 + IW * 0.2f, cy), new Vector2(IW * 0.4f, 50f), 22, TextAnchor.MiddleLeft);
            Text mt, pt;
            BtnAt(x0 + IW * 0.5f, cy, 70f, 46f, "-", () => { set(Mathf.Max(min, get() - 1)); Refresh(); }, out mt, 28);
            var v = Ar6dofVrUi.Label(content, "", new Vector2(x0 + IW * 0.5f + 75f, cy), new Vector2(80f, 50f), 24, TextAnchor.MiddleCenter);
            BtnAt(x0 + IW * 0.5f + 150f, cy, 70f, 46f, "+", () => { set(Mathf.Min(max, get() + 1)); Refresh(); }, out pt, 28);
            refreshers.Add(() => v.text = get().ToString());
        }

        void BuildPadEditor(Ar6dofAudio a, PadDef p)
        {
            Lbl("Modifier le bouton", 36, 26).MakeAccent();
            float cy = RowY(50f);
            Ar6dofVrTextField.Make(content, new Vector2(0f, cy), new Vector2(IW, 50f), p.label, "Nom du bouton", s => { p.label = s; a.MarkDirty(); }, s => { p.label = s; a.MarkDirty(); }, 24);

            var fb = Btn("", () =>
            {
                Browse("Choisir un son", AudioExt, false, files =>
                {
                    p.path = files[0];
                    if (string.IsNullOrEmpty(p.label)) p.label = System.IO.Path.GetFileNameWithoutExtension(files[0]);
                    a.MarkDirty();
                    a.LoadPad(p);
                    Rebuild();
                }, Ar6dofLibrary.Sounds);
            }, 50f, 20);
            var ft = fb.GetComponentInChildren<Text>();
            refreshers.Add(() => ft.text = string.IsNullOrEmpty(p.path) ? Ar6dofLoc.T("Choisir un fichier...") : System.IO.Path.GetFileName(p.path));
            var info = Lbl("", 28, 18);
            refreshers.Add(() =>
            {
                int st, ms;
                a.soundState.TryGetValue(p.slot, out st);
                a.soundMs.TryGetValue(p.slot, out ms);
                info.text = st == 1 ? Ar6dofLoc.F("Pret ({0} s)", (ms / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                          : st == 2 ? Ar6dofLoc.T("Fichier illisible, ou moteur audio eteint")
                          : Ar6dofLoc.T("Chargement ou moteur audio eteint");
            });

            // couleurs
            float cyc = RowY(46f);
            float sw = (IW - (Palette.Length - 1) * 6f) / Palette.Length;
            for (int i = 0; i < Palette.Length; i++)
            {
                var c = Palette[i];
                var rt = Ar6dofVrUi.Rect(content, "Couleur", new Vector2(-IW * 0.5f + sw * 0.5f + i * (sw + 6f), cyc), new Vector2(sw, 44f));
                var im = rt.gameObject.AddComponent<Image>();
                im.color = c;
                Ar6dofTheme.SetRounded(im, 12);
                var bt = rt.gameObject.AddComponent<Button>();
                bt.targetGraphic = im;
                bt.onClick.AddListener(() => { p.r = c.r; p.g = c.g; p.b = c.b; a.MarkDirty(); });
            }

            SliderRow("Volume", 0f, 2f, () => p.vol, v => { p.vol = v; a.MarkDirty(); }, Pct, 50f);
            SliderRow("Vitesse", 0.5f, 2f, () => p.rate, v => { p.rate = v; a.MarkDirty(); }, v => "x" + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), 50f);
            SliderRow("Panoramique", -1f, 1f, () => p.pan, v => { p.pan = v; a.MarkDirty(); }, v => Mathf.Abs(v) < 0.05f ? Ar6dofLoc.T("centre") : (v < 0 ? "G " : "D ") + Mathf.RoundToInt(Mathf.Abs(v) * 100f) + "%", 50f);
            ToggleRow("Boucle (2e clic = arret)", () => p.loop, v => { p.loop = v; a.MarkDirty(); }, 50f, 22);
            Stepper("Largeur (cases)", () => p.w, v => { p.w = v; a.MarkDirty(); }, 1, 6);
            Stepper("Hauteur (cases)", () => p.h, v => { p.h = v; a.MarkDirty(); }, 1, 6);

            BtnRow(50f, 21, "Tester", (Action)(() => a.PlayPad(p)), "Arreter", (Action)(() => a.Send("STOP " + p.slot)),
                            "Dupliquer", (Action)(() => { var np = a.DuplicatePad(p); editing = np; Rebuild(); }));
            BtnRow(50f, 21, "< Avant", (Action)(() => Move(a, p, -1)), "Apres >", (Action)(() => Move(a, p, 1)));
            Text dt;
            var del = BtnT(confirmDel ? "Confirmer la suppression" : "Supprimer", () =>
            {
                if (!confirmDel) { confirmDel = true; Rebuild(); return; }
                a.RemovePad(p);
                editing = null; confirmDel = false;
                Rebuild();
            }, out dt, 50f, 21);
            Ar6dofTheme.SetSelected(del, confirmDel);
            Btn("Retour", () => { editing = null; confirmDel = false; Rebuild(); }, 50f, 22);
        }

        void Move(Ar6dofAudio a, PadDef p, int dir)
        {
            int i = a.data.pads.IndexOf(p), j = i + dir;
            if (i < 0 || j < 0 || j >= a.data.pads.Count) return;
            a.data.pads.RemoveAt(i);
            a.data.pads.Insert(j, p);
            a.MarkDirty();
        }
    }
}
