using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Onglet « DJ » de la table de mixage, dans l'esprit d'un logiciel de DJ (Rekordbox) : deux platines A et B, formes d'onde (toute la piste + vue détaillée qui défile avec la grille
    /// des temps), CUE / PLAY / SYNC, boucles, 4 hot cues, tempo, pitch bend, égaliseur 3 bandes + filtre + volume par platine, crossfader, bibliothèque pour charger une piste.
    /// Le son est fait par ar6dof_audio (voir src/audio/audio_deck.h) et sort par la tranche « Musique » de la table (fader, égaliseur et limiteur du général compris).
    /// Les pistes ne sont chargées que dans cet onglet et libérées à la fermeture de la table : aucun coût sinon.
    /// </summary>
    public partial class Ar6dofMixerApp
    {
        const float DjW = 1120f;
        static readonly Color DjColA = new Color(0.25f, 0.80f, 1f), DjColB = new Color(1f, 0.45f, 0.80f);
        static readonly float[] DjRange = { 0.08f, 0.16f, 0.5f };
        static readonly float[] DjLoops = { 1f, 2f, 4f, 8f };
        static readonly CultureInfo DjInv = CultureInfo.InvariantCulture;
        const int DjLibPerPage = 8;

        int djPage;                    // 0 platines, 1 bibliothèque
        int djTarget;                  // platine qui recevra la piste choisie dans la bibliothèque
        int djLibPage;
        readonly Ar6dofDjWave[] djOver = new Ar6dofDjWave[2], djDet = new Ar6dofDjWave[2];
        readonly Slider[] djScrub = new Slider[2], djTempoS = new Slider[2];
        readonly Text[] djTitle = new Text[2], djTempoTxt = new Text[2];
        readonly Ar6dofMeterBar[] djMeter = new Ar6dofMeterBar[2];
        readonly float[] djScrubUser = { -10f, -10f }, djScrubSent = { -10f, -10f }, djScrubVal = new float[2], djBendEnd = new float[2], djTempoUser = { -10f, -10f }, djLoopBeats = new float[2];
        readonly bool[] djScrubDirty = new bool[2], djDelHot = new bool[2];
        float djNextTitle;

        void DjClear()
        {
            Array.Clear(djOver, 0, 2); Array.Clear(djDet, 0, 2); Array.Clear(djScrub, 0, 2); Array.Clear(djTempoS, 0, 2);
            Array.Clear(djTitle, 0, 2); Array.Clear(djTempoTxt, 0, 2); Array.Clear(djMeter, 0, 2);
            djNextTitle = 0f;
        }

        string DjName(int d) { return d == 0 ? "A" : "B"; }

        // ------------------------------------------------------------------ page
        void DjTab(Ar6dofAudio a)
        {
            if (djPage == 1) { DjLibrary(a); return; }

            // forme d'onde de toute la piste + barre de position (glisser = aller à cet endroit), pour A puis B
            for (int d = 0; d < 2; d++)
            {
                int dd = d;
                var k = a.decks[d];
                Color col = d == 0 ? DjColA : DjColB;
                float cy = RowY(28f);
                var t = Ar6dofVrUi.Label(content, "", new Vector2(0f, cy), new Vector2(IW, 28f), 20, TextAnchor.MiddleLeft);
                t.gameObject.AddComponent<Ar6dofFitText>().Init(t, 20);
                t.color = col;
                t.fontStyle = FontStyle.Bold;
                djTitle[d] = t;
                djOver[d] = Ar6dofDjWave.Make(content, new Vector2(0f, RowY(38f)), new Vector2(IW, 38f), k, false, col);
                float sy = RowY(26f);
                djScrub[d] = Ar6dofVrUi.MakeSlider(content, new Vector2(0f, sy), new Vector2(IW, 26f), 0f, Mathf.Max(1f, k.dur), Mathf.Clamp(k.pos, 0f, Mathf.Max(1f, k.dur)), v =>
                {
                    djScrubUser[dd] = Time.unscaledTime; djScrubVal[dd] = v; djScrubDirty[dd] = true;
                });
            }
            for (int d = 0; d < 2; d++)
                djDet[d] = Ar6dofDjWave.Make(content, new Vector2(0f, RowY(70f)), new Vector2(IW, 70f), a.decks[d], true, d == 0 ? DjColA : DjColB);

            // platines et table au centre
            float top = y;
            const float colW = 380f, midW = 300f;
            float xl = -IW * 0.5f, xm = -midW * 0.5f, xr = IW * 0.5f - colW;
            float h0 = DjDeckControls(a, 0, xl, top, colW);
            float h2 = DjDeckControls(a, 1, xr, top, colW);
            float h1 = DjMixerColumn(a, xm, top, midW);
            y = top + Mathf.Max(h0, Mathf.Max(h1, h2)) + Gap;

            Para("Les pistes sortent par la tranche Musique (son general inclus). Le tempo change aussi la hauteur du son (comme un tourne-disque). Les reperes ne sont pas gardes.", 46, 15);
            DjTick(a, true);
        }

        // ------------------------------------------------------------------ une platine
        float DjDeckControls(Ar6dofAudio a, int d, float x0, float top, float w)
        {
            var k = a.decks[d];
            float yy = top;
            const float gap = 6f;

            // CUE / PLAY / SYNC
            float bw3 = (w - 2f * gap) / 3f, bh = 56f;
            Text t0, t1, t2;
            float cy = -(yy + bh * 0.5f);
            BtnAt(x0 + bw3 * 0.5f, cy, bw3, bh, "CUE", () => DjCue(a, d), out t0, 22);
            var play = BtnAt(x0 + bw3 * 1.5f + gap, cy, bw3, bh, "PLAY", () => DjPlay(a, d), out t1, 22);
            BtnAt(x0 + bw3 * 2.5f + 2f * gap, cy, bw3, bh, "SYNC", () => a.DjSync(d), out t2, 22);
            yy += bh + gap;

            // boucles (en temps)
            const float rh = 44f;
            cy = -(yy + rh * 0.5f);
            Ar6dofVrUi.Label(content, Ar6dofLoc.T("Boucle"), new Vector2(x0 + 38f, cy), new Vector2(76f, rh), 17, TextAnchor.MiddleLeft);
            float lw = (w - 80f - 4f * gap) / 4f;
            var loopB = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int ii = i;
                Text lt;
                loopB[i] = BtnAt(x0 + 80f + gap + lw * 0.5f + i * (lw + gap) - gap, cy, lw, rh, DjLoops[i].ToString("0", DjInv), () => DjLoopToggle(a, d, DjLoops[ii]), out lt, 20);
            }
            yy += rh + gap;

            // 4 hot cues + effacer
            cy = -(yy + rh * 0.5f);
            float pw = (w - 84f - 4f * gap) / 4f;
            var hotB = new Button[4];
            var hotT = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                int ii = i;
                hotB[i] = BtnAt(x0 + pw * 0.5f + i * (pw + gap), cy, pw, rh, (i + 1).ToString(), () => DjHot(a, d, ii), out hotT[i], 21);
            }
            Text dt;
            var delB = BtnAt(x0 + w - 42f, cy, 84f, rh, "Suppr", () => { djDelHot[d] = !djDelHot[d]; Refresh(); }, out dt, 17);
            yy += rh + gap;

            // tempo : valeur, curseur, plage
            cy = -(yy + rh * 0.5f);
            djTempoTxt[d] = Ar6dofVrUi.Label(content, "0.0%", new Vector2(x0 + 44f, cy), new Vector2(88f, rh), 19, TextAnchor.MiddleLeft);
            float range = DjRange[Mathf.Clamp(k.range, 0, 2)];
            djTempoS[d] = Ar6dofVrUi.MakeSlider(content, new Vector2(x0 + 92f + (w - 92f - 82f - gap) * 0.5f, cy), new Vector2(w - 92f - 82f - gap, rh - 4f), 1f - range, 1f + range, Mathf.Clamp(k.tempo, 1f - range, 1f + range), v =>
            {
                if (Mathf.Abs(v - 1f) < 0.003f) v = 1f;
                djTempoUser[d] = Time.unscaledTime;
                a.DjTempo(d, v);
            });
            Text rt;
            var rangeB = BtnAt(x0 + w - 41f, cy, 82f, rh, "", () =>
            {
                k.range = (k.range + 1) % 3;
                float r2 = DjRange[k.range];
                djTempoS[d].minValue = 1f - r2; djTempoS[d].maxValue = 1f + r2;
                djTempoS[d].SetValueWithoutNotify(Mathf.Clamp(k.tempo, 1f - r2, 1f + r2));
                Refresh();
            }, out rt, 18);
            yy += rh + gap;

            // pitch bend (coup de pouce de 0,35 s), retour au tempo d'origine
            cy = -(yy + rh * 0.5f);
            float b3 = (w - 2f * gap) / 3f;
            Text bt0, bt1, bt2;
            BtnAt(x0 + b3 * 0.5f, cy, b3, rh, "Bend -", () => DjBendPulse(a, d, -1f), out bt0, 18);
            BtnAt(x0 + b3 * 1.5f + gap, cy, b3, rh, "Bend +", () => DjBendPulse(a, d, 1f), out bt1, 18);
            BtnAt(x0 + b3 * 2.5f + 2f * gap, cy, b3, rh, "Tempo 0", () =>
            {
                a.DjTempo(d, 1f);
                djTempoS[d].SetValueWithoutNotify(1f);
                Refresh();
            }, out bt2, 18);
            yy += rh + gap;

            // charger / éjecter
            cy = -(yy + rh * 0.5f);
            float l2 = (w - gap) * 0.5f;
            Text lt0, lt1;
            BtnAt(x0 + l2 * 0.5f, cy, l2, rh, "Charger...", () => { djTarget = d; djPage = 1; djLibPage = 0; Rebuild(); }, out lt0, 19);
            BtnAt(x0 + l2 * 1.5f + gap, cy, l2, rh, "Ejecter", () => { a.DjEject(d); Refresh(); }, out lt1, 19);
            yy += rh + gap;

            // état des boutons : lecture, boucle active, repères posés, tempo affiché
            Color dcol = d == 0 ? DjColA : DjColB;
            refreshers.Add(() =>
            {
                Ar6dofTheme.SetSelected(play, k.playing);
                for (int i = 0; i < 4; i++) Ar6dofTheme.SetSelected(loopB[i], k.loop && Mathf.Approximately(djLoopBeats[d], DjLoops[i]));
                for (int i = 0; i < 4; i++)
                {
                    Ar6dofTheme.SetSelected(hotB[i], k.hot[i] >= 0f);
                    hotT[i].color = k.hot[i] >= 0f ? Ar6dofDjWave.HotColor(i) : hotT[i].color;
                }
                Ar6dofTheme.SetSelected(delB, djDelHot[d]);
                rt.text = "+/- " + Mathf.RoundToInt(DjRange[Mathf.Clamp(k.range, 0, 2)] * 100f) + "%";
                if (djTempoTxt[d] != null) { djTempoTxt[d].text = DjPercent(k.tempo); djTempoTxt[d].color = dcol; }
                if (djTempoS[d] != null)
                {
                    // le moteur a changé le tempo (SYNC) : la plage s'élargit au besoin et le curseur suit, sauf pendant qu'on le tient
                    while (k.range < 2 && Mathf.Abs(k.tempo - 1f) > DjRange[k.range] + 0.0005f) { k.range++; djTempoS[d].minValue = 1f - DjRange[k.range]; djTempoS[d].maxValue = 1f + DjRange[k.range]; }
                    if (Time.unscaledTime - djTempoUser[d] > 0.6f) djTempoS[d].SetValueWithoutNotify(Mathf.Clamp(k.tempo, djTempoS[d].minValue, djTempoS[d].maxValue));
                }
            });
            return yy - top;
        }

        static string DjPercent(float tempo) { return ((tempo - 1f) * 100f).ToString("+0.0;-0.0;0.0", DjInv) + "%"; }

        // ------------------------------------------------------------------ table de mixage au centre : égaliseur, filtre, volume, niveaux, crossfader
        float DjMixerColumn(Ar6dofAudio a, float x0, float top, float w)
        {
            float yy = top;
            const float rh = 40f, gap = 6f;
            float sw = 112f, lw = w - 2f * sw - 2f * 6f;   // curseur A | étiquette | curseur B
            float xa = x0 + sw * 0.5f, xl = x0 + sw + 6f + lw * 0.5f, xb = x0 + w - sw * 0.5f;

            // niveaux
            float cy = -(yy + 24f * 0.5f);
            djMeter[0] = Ar6dofMeterBar.Make(content, new Vector2(xa, cy), new Vector2(sw, 20f));
            djMeter[1] = Ar6dofMeterBar.Make(content, new Vector2(xb, cy), new Vector2(sw, 20f));
            Ar6dofVrUi.Label(content, "A | B", new Vector2(xl, cy), new Vector2(lw, 24f), 16, TextAnchor.MiddleCenter);
            yy += 24f + gap;

            // aigus, medium, graves (dB) : le centre est le neutre ; toucher l'étiquette remet les deux à zéro
            string[] names = { "Aigus", "Medium", "Graves" };
            int[] bands = { 2, 1, 0 };
            for (int r = 0; r < 3; r++)
            {
                int band = bands[r];
                cy = -(yy + rh * 0.5f);
                for (int d = 0; d < 2; d++)
                {
                    int dd = d;
                    var k = a.decks[d];
                    var s = Ar6dofVrUi.MakeSlider(content, new Vector2(d == 0 ? xa : xb, cy), new Vector2(sw, rh - 4f), -24f, 6f, k.eq[band], v => { if (Mathf.Abs(v) < 1.2f) v = 0f; a.DjEq(dd, band, v); });
                    var ss = s;
                    refreshers.Add(() => ss.SetValueWithoutNotify(k.eq[band]));
                }
                Text lt;
                int br = band;
                BtnAt(xl, cy, lw, rh - 4f, names[r], () => { for (int d = 0; d < 2; d++) a.DjEq(d, br, 0f); Refresh(); }, out lt, 15);
                yy += rh + gap;
            }

            // filtre : à gauche passe-bas, à droite passe-haut
            cy = -(yy + rh * 0.5f);
            for (int d = 0; d < 2; d++)
            {
                int dd = d;
                var k = a.decks[d];
                var s = Ar6dofVrUi.MakeSlider(content, new Vector2(d == 0 ? xa : xb, cy), new Vector2(sw, rh - 4f), -1f, 1f, k.filter, v => { if (Mathf.Abs(v) < 0.06f) v = 0f; a.DjFilter(dd, v); });
                var ss = s;
                refreshers.Add(() => ss.SetValueWithoutNotify(k.filter));
            }
            Text ft;
            BtnAt(xl, cy, lw, rh - 4f, "Filtre", () => { for (int d = 0; d < 2; d++) a.DjFilter(d, 0f); Refresh(); }, out ft, 15);
            yy += rh + gap;

            // volume des platines
            cy = -(yy + rh * 0.5f);
            for (int d = 0; d < 2; d++)
            {
                int dd = d;
                var k = a.decks[d];
                var s = Ar6dofVrUi.MakeSlider(content, new Vector2(d == 0 ? xa : xb, cy), new Vector2(sw, rh - 4f), 0f, 1.2f, k.vol, v => { if (Mathf.Abs(v - 1f) < 0.05f) v = 1f; a.DjVol(dd, v); });
                var ss = s;
                refreshers.Add(() => ss.SetValueWithoutNotify(k.vol));
            }
            Text vt;
            BtnAt(xl, cy, lw, rh - 4f, "Volume", () => { for (int d = 0; d < 2; d++) a.DjVol(d, 1f); Refresh(); }, out vt, 15);
            yy += rh + gap;

            // crossfader
            cy = -(yy + 46f * 0.5f);
            Ar6dofVrUi.Label(content, "A", new Vector2(x0 + 14f, cy), new Vector2(28f, 46f), 22, TextAnchor.MiddleCenter);
            Ar6dofVrUi.Label(content, "B", new Vector2(x0 + w - 14f, cy), new Vector2(28f, 46f), 22, TextAnchor.MiddleCenter);
            var xf = Ar6dofVrUi.MakeSlider(content, new Vector2(x0 + w * 0.5f, cy), new Vector2(w - 64f, 42f), 0f, 1f, a.xfade, v =>
            {
                if (Mathf.Abs(v - 0.5f) < 0.03f) v = 0.5f;
                a.DjXfade(v);
            });
            refreshers.Add(() => xf.SetValueWithoutNotify(a.xfade));
            yy += 46f + gap;

            cy = -(yy + 40f * 0.5f);
            Text ct;
            BtnAt(x0 + w * 0.5f, cy, w, 40f, "", () => { a.DjCurve(a.xfCurve == 0 ? 1 : 0); Refresh(); }, out ct, 17);
            Text ctt = ct;
            refreshers.Add(() => ctt.text = Ar6dofLoc.T("Crossfader") + " : " + Ar6dofLoc.T(a.xfCurve == 0 ? "Mix" : "Coupe"));
            yy += 40f + gap;
            return yy - top;
        }

        // ------------------------------------------------------------------ actions
        void DjCue(Ar6dofAudio a, int d)
        {
            var k = a.decks[d];
            if (!k.loaded) return;
            if (k.playing) { a.DjPlay(d, false); a.DjSeek(d, k.cue); }
            else { k.cue = k.pos; k.cueVersion++; }   // à l'arrêt : le repère se pose ici
        }

        void DjPlay(Ar6dofAudio a, int d)
        {
            var k = a.decks[d];
            if (!k.loaded) return;
            if (!k.playing && k.dur > 0f && k.pos >= k.dur - 0.5f) a.DjSeek(d, 0f);
            a.DjPlay(d, !k.playing);
        }

        void DjHot(Ar6dofAudio a, int d, int i)
        {
            var k = a.decks[d];
            if (!k.loaded) return;
            if (djDelHot[d]) { k.hot[i] = -1f; k.cueVersion++; djDelHot[d] = false; Refresh(); return; }
            if (k.hot[i] < 0f) { k.hot[i] = k.pos; k.cueVersion++; }
            else a.DjSeek(d, k.hot[i]);
            Refresh();
        }

        void DjLoopToggle(Ar6dofAudio a, int d, float beats)
        {
            var k = a.decks[d];
            if (!k.loaded) return;
            if (k.loop && Mathf.Approximately(djLoopBeats[d], beats))
            {
                a.DjLoop(d, 0f);
                k.loop = false; k.loopIn = k.loopOut = -1f; k.loopLock = Time.unscaledTime + 0.3f; k.cueVersion++;
                djLoopBeats[d] = 0f;
                Refresh();
                return;
            }
            float bl = k.BeatLen, inT = k.pos;
            if (k.bpm > 0f) inT = Mathf.Max(0f, k.firstBeat + Mathf.Floor((k.pos - k.firstBeat) / bl + 0.5f) * bl);   // comme le moteur : au temps le plus proche
            k.loopIn = inT; k.loopOut = inT + beats * bl;
            k.loop = true; k.loopLock = Time.unscaledTime + 0.3f; k.cueVersion++;
            djLoopBeats[d] = beats;
            a.DjLoop(d, beats);
            Refresh();
        }

        void DjBendPulse(Ar6dofAudio a, int d, float v)
        {
            a.DjBend(d, v);
            djBendEnd[d] = Time.unscaledTime + 0.35f;
        }

        // ------------------------------------------------------------------ à chaque image
        void DjTick(Ar6dofAudio a, bool first)
        {
            if (djPage != 0) return;
            a.WantDj();
            float now = Time.unscaledTime;
            for (int d = 0; d < 2; d++)
            {
                var k = a.decks[d];
                if (djOver[d] != null) djOver[d].Tick(k.pos, now);
                if (djDet[d] != null) djDet[d].Tick(k.pos, now);
                var sc = djScrub[d];
                if (sc != null)
                {
                    float mx = Mathf.Max(1f, k.dur);
                    if (Mathf.Abs(sc.maxValue - mx) > 0.01f) sc.maxValue = mx;
                    if (now - djScrubUser[d] > 0.5f) sc.SetValueWithoutNotify(Mathf.Clamp(k.pos, 0f, mx));
                    if (djScrubDirty[d] && now - djScrubSent[d] > 0.08f) { a.DjSeek(d, djScrubVal[d]); djScrubDirty[d] = false; djScrubSent[d] = now; }
                }
                if (djBendEnd[d] > 0f && now >= djBendEnd[d]) { a.DjBend(d, 0f); djBendEnd[d] = 0f; }
                if (djMeter[d] != null) djMeter[d].SetPeak(k.vu);
            }
            if (first || now >= djNextTitle)
            {
                djNextTitle = now + 0.1f;
                for (int d = 0; d < 2; d++) if (djTitle[d] != null) djTitle[d].text = DjTitleText(a.decks[d], d);
            }
        }

        string DjTitleText(DjDeck k, int d)
        {
            string n = DjName(d);
            if (k.state == 1) return n + "   " + Ar6dofLoc.T("Chargement...") + "  " + k.name;
            if (k.state == 3) return n + "   " + Ar6dofLoc.T("Fichier illisible");
            if (k.state != 2) return n + "   " + Ar6dofLoc.T("Aucune piste : Charger...");
            string bpm = k.bpm > 0f ? (k.bpm * k.tempo).ToString("0.0", DjInv) + " BPM" : Ar6dofLoc.T("BPM inconnu");
            return n + "   " + k.name + "     " + bpm + "     " + DjPercent(k.tempo) + "     " + Clock(k.pos) + " / -" + Clock(Mathf.Max(0f, k.dur - k.pos));
        }

        // ------------------------------------------------------------------ bibliothèque (liste de lecture de l'onglet Musique)
        void DjLibrary(Ar6dofAudio a)
        {
            Tabs(new[] { "Charger dans la platine A", "Charger dans la platine B" }, djTarget, i => djTarget = i);
            int n = a.data.playlist.Count;
            if (n == 0) Para("La liste est vide. Ajoute des musiques (elles sont aussi dans l'onglet Musique).", 46, 19);
            int pages = Mathf.Max(1, (n + DjLibPerPage - 1) / DjLibPerPage);
            djLibPage = Mathf.Clamp(djLibPage, 0, pages - 1);
            for (int i = djLibPage * DjLibPerPage; i < Mathf.Min(n, (djLibPage + 1) * DjLibPerPage); i++)
            {
                int k = i;
                string path = a.data.playlist[k];
                Btn((k + 1) + ".  " + System.IO.Path.GetFileNameWithoutExtension(path), () => { a.DjLoad(djTarget, path); djPage = 0; Rebuild(); }, 46, 21);
            }
            if (pages > 1)
                BtnRow(44f, 21, "<", (Action)(() => { djLibPage = (djLibPage + pages - 1) % pages; Rebuild(); }), Ar6dofLoc.F("Page {0}/{1}", djLibPage + 1, pages), (Action)(() => { }), ">", (Action)(() => { djLibPage = (djLibPage + 1) % pages; Rebuild(); }));
            BtnRow(48f, 20, "Ajouter des musiques", (Action)(() =>
            {
                Browse("Ajouter des musiques", AudioExt, true, files =>
                {
                    foreach (var f in files) if (!a.data.playlist.Contains(f)) a.data.playlist.Add(f);
                    if (a.data.track < 0 && a.data.playlist.Count > 0) a.data.track = 0;
                    a.MarkDirty();
                    Rebuild();
                }, Ar6dofLibrary.Music);
            }), "Retour aux platines", (Action)(() => { djPage = 0; Rebuild(); }));
            Para("Une piste est lue en entier (10 minutes au maximum). L'analyse du tempo prend quelques secondes.", 40, 16);
        }
    }
}
