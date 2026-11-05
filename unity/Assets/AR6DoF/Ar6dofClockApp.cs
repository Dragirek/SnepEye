using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Application « Horloge » : l'heure avec 6 cadrans, un chronomètre (3 visuels, tours), un minuteur (5 visuels) et des alarmes
    /// (nom modifiable, heure en 12 h avec AM / PM ou en 24 h, jours de répétition, répéter, 3 sonneries). Les alarmes et le minuteur sont gérés par
    /// Ar6dofClockService, qui continue de fonctionner fenêtre fermée : seule la fenêtre ne consomme rien quand elle est rangée.
    /// </summary>
    public class Ar6dofClockApp : Ar6dofAppWindow
    {
        static readonly string[] TabNames = { "Horloge", "Chrono", "Minuteur", "Alarmes" };
        static readonly string[] StopwatchStyles = { "Numerique", "Cadran", "Anneau" };
        static readonly int[] SnoozeValues = { 1, 3, 5, 10, 15, 30 };
        const int MaxAlarms = 12;

        int tab;
        Ar6dofAlarm editing;

        // éléments vivants de la fenêtre courante (oubliés à chaque reconstruction)
        Ar6dofClockFace face;
        Ar6dofSweepDial dial;
        Ar6dofProgressGfx prog;
        Text bigTime, dateTxt, swTxt, tmTxt, swStartLbl, tmStartLbl, nextTxt;
        int lastStamp = -1;
        float nextText;
        int seenLaps = -1, seenAlarmSig = -1;
        Ar6dofClockService.TimerPhase seenPhase;
        string lastSw = "", lastTm = "";

        public Ar6dofClockApp(Ar6dofDock d) : base(d, "clock", "Horloge", "clock", "Heure avec plein de cadrans, chronometre, minuteur et alarmes.", 660f) { }

        static Ar6dofClockService Svc { get { return Ar6dofClockService.Instance; } }

        protected override void OnRebuildStart()
        {
            face = null; dial = null; prog = null;
            bigTime = dateTxt = swTxt = tmTxt = swStartLbl = tmStartLbl = nextTxt = null;
            lastStamp = -1; lastSw = ""; lastTm = "";
            var s = Svc;
            seenLaps = s != null ? s.laps.Count : -1;
            seenPhase = s != null ? s.Phase : Ar6dofClockService.TimerPhase.Idle;
            seenAlarmSig = s != null ? AlarmSig(s) : -1;
        }

        protected override void OnVisible(bool on) { if (!on) editing = null; }

        // ------------------------------------------------------------------ éléments communs
        /// <summary>Choix dans une liste : [&lt;] texte [&gt;]. Un clic reconstruit la fenêtre.</summary>
        void CycleBtns(string label, int count, Func<int> get, Action<int> set, Func<int, string> valueOf)
        {
            float cy = RowY(50f);
            Text mid, t;
            BtnAt(-IW * 0.5f + 40f, cy, 80f, 50f, "<", () => { set((get() + count - 1) % count); Rebuild(); }, out t, 26);
            BtnAt(IW * 0.5f - 40f, cy, 80f, 50f, ">", () => { set((get() + 1) % count); Rebuild(); }, out t, 26);
            var mb = BtnAt(0f, cy, IW - 176f, 50f, "", () => { }, out mid, 23);
            mb.interactable = false;
            mid.text = Ar6dofLoc.T(label) + " : " + valueOf(get());
        }

        /// <summary>Trois boutons sur une ligne (le texte du premier peut changer ensuite).</summary>
        void Row3(float h, int fs, string a, Action ca, out Text la, string b, Action cb, string c, Action cc)
        {
            float cy = RowY(h);
            float w = (IW - 16f) / 3f;
            Text t;
            BtnAt(-IW * 0.5f + w * 0.5f, cy, w, h, a, ca, out la, fs);
            BtnAt(0f, cy, w, h, b, cb, out t, fs);
            BtnAt(IW * 0.5f - w * 0.5f, cy, w, h, c, cc, out t, fs);
        }

        RectTransform GfxBox(float size, out float cy)
        {
            cy = RowY(size);
            return Ar6dofVrUi.Rect(content, "Dessin", new Vector2(0f, cy), new Vector2(size, size));
        }

        static void SaveClockFmt()
        {
            Ar6dofSettings.SaveFields(new[] { "clock24h", "clockSeconds" });
            Ar6dofSettings.Notify();
        }

        // ------------------------------------------------------------------ fenêtre
        protected override void BuildMain()
        {
            var svc = Svc;
            if (svc == null) { Lbl("Horloge indisponible", 34, 22); return; }
            if (editing != null && !svc.data.alarms.Contains(editing)) editing = null;
            if (tab == 3 && editing != null) { BuildEditor(svc); return; }
            Tabs(TabNames, tab, i => { tab = i; editing = null; });
            Space(6f);
            switch (tab)
            {
                case 0: BuildClock(svc); break;
                case 1: BuildStopwatch(svc); break;
                case 2: BuildTimer(svc); break;
                default: BuildAlarms(svc); break;
            }
        }

        // ------------------------------------------------------------------ horloge
        void BuildClock(Ar6dofClockService svc)
        {
            var st = Ar6dofSettings.Current;
            float cy;
            var box = GfxBox(250f, out cy);
            face = box.gameObject.AddComponent<Ar6dofClockFace>();
            face.raycastTarget = false;
            face.Configure(svc.data.faceStyle, st.clockSeconds, 10);
            bigTime = Lbl("", 76f, 62, TextAnchor.MiddleCenter);
            bigTime.fontStyle = FontStyle.Bold;
            dateTxt = Lbl("", 40f, 27, TextAnchor.MiddleCenter);
            Space(4f);
            CycleBtns("Cadran", Ar6dofClockFace.Styles, () => svc.data.faceStyle, v => { svc.data.faceStyle = v; svc.MarkDirty(); }, i => Ar6dofLoc.T(Ar6dofClockFace.StyleNames[i]));
            CycleBtns("Format de la date", Ar6dofClockFmt.DateFormats.Length, () => Ar6dofSettings.Current.dateFormat, v => { Ar6dofSettings.Current.dateFormat = v; Ar6dofSettings.SaveFields(new[] { "dateFormat" }); Ar6dofSettings.Notify(); }, i => Ar6dofClockFmt.Date(DateTime.Now, i));
            ToggleRow("Format 24 h", () => Ar6dofSettings.Current.clock24h, v => { Ar6dofSettings.Current.clock24h = v; SaveClockFmt(); });
            ToggleRow("Afficher les secondes", () => Ar6dofSettings.Current.clockSeconds, v => { Ar6dofSettings.Current.clockSeconds = v; SaveClockFmt(); if (face != null) face.Configure(svc.data.faceStyle, v, 10); lastStamp = -1; });
            Para("Pour afficher l'heure, la date et la batterie sur la barre de taches : Reglages > Dock.", 40f, 17);
        }

        // ------------------------------------------------------------------ chronomètre
        void BuildStopwatch(Ar6dofClockService svc)
        {
            CycleBtns("Visuel", StopwatchStyles.Length, () => svc.data.stopwatchStyle, v => { svc.data.stopwatchStyle = v; svc.MarkDirty(); }, i => Ar6dofLoc.T(StopwatchStyles[i]));
            int style = svc.data.stopwatchStyle;
            float cy;
            if (style == 1)
            {
                var box = GfxBox(250f, out cy);
                dial = box.gameObject.AddComponent<Ar6dofSweepDial>();
                dial.raycastTarget = false;
                swTxt = Lbl("", 70f, 54, TextAnchor.MiddleCenter);
            }
            else if (style == 2)
            {
                var box = GfxBox(250f, out cy);
                prog = box.gameObject.AddComponent<Ar6dofProgressGfx>();
                prog.raycastTarget = false;
                prog.SetStyle(0);
                swTxt = Ar6dofVrUi.Label(content, "", new Vector2(0f, cy), new Vector2(230f, 70f), 46, TextAnchor.MiddleCenter);
            }
            else
            {
                swTxt = Lbl("", 130f, 88, TextAnchor.MiddleCenter);
            }
            swTxt.fontStyle = FontStyle.Bold;
            Space(2f);
            Row3(60f, 24, svc.SwRunning ? "Pause" : "Demarrer", () => { svc.SwToggle(); Rebuild(); }, out swStartLbl,
                 "Tour", () => { svc.SwLap(); Rebuild(); },
                 "Remise a zero", () => { svc.SwReset(); Rebuild(); });
            var laps = svc.laps;
            int shown = 0;
            for (int i = laps.Count - 1; i >= 0 && shown < 6; i--, shown++)
            {
                double prev = i > 0 ? laps[i - 1] : 0.0;
                Lbl(Ar6dofLoc.F("Tour {0}", i + 1) + "     " + Ar6dofClockFmt.Stopwatch(laps[i]) + "     +" + Ar6dofClockFmt.Stopwatch(laps[i] - prev), 30f, 22);
            }
            if (laps.Count == 0) Para("Touche Tour pour noter un temps sans arreter le chronometre.", 40f, 17);
        }

        // ------------------------------------------------------------------ minuteur
        void BuildTimer(Ar6dofClockService svc)
        {
            var phase = svc.Phase;
            CycleBtns("Visuel", Ar6dofProgressGfx.Styles, () => svc.data.timerStyle, v => { svc.data.timerStyle = v; svc.MarkDirty(); }, i => Ar6dofLoc.T(Ar6dofProgressGfx.StyleNames[i]));
            int style = svc.data.timerStyle;
            float cy;
            var box = GfxBox(250f, out cy);
            prog = box.gameObject.AddComponent<Ar6dofProgressGfx>();
            prog.raycastTarget = false;
            prog.SetStyle(style);
            if (style == 0 || style == 2)
            {
                tmTxt = Ar6dofVrUi.Label(content, "", new Vector2(0f, cy), new Vector2(230f, 70f), 50, TextAnchor.MiddleCenter);
            }
            else
            {
                tmTxt = Lbl("", 70f, 54, TextAnchor.MiddleCenter);
            }
            tmTxt.fontStyle = FontStyle.Bold;
            Space(2f);

            if (phase == Ar6dofClockService.TimerPhase.Idle)
            {
                BtnRow(48f, 21, "1 min", (Action)(() => SetTimer(svc, 60)), "3 min", (Action)(() => SetTimer(svc, 180)), "5 min", (Action)(() => SetTimer(svc, 300)),
                                "10 min", (Action)(() => SetTimer(svc, 600)), "15 min", (Action)(() => SetTimer(svc, 900)), "30 min", (Action)(() => SetTimer(svc, 1800)));
                BtnRow(48f, 21, "-10 min", (Action)(() => SetTimer(svc, svc.data.timerSeconds - 600)), "-1 min", (Action)(() => SetTimer(svc, svc.data.timerSeconds - 60)),
                                "-10 s", (Action)(() => SetTimer(svc, svc.data.timerSeconds - 10)), "+10 s", (Action)(() => SetTimer(svc, svc.data.timerSeconds + 10)),
                                "+1 min", (Action)(() => SetTimer(svc, svc.data.timerSeconds + 60)), "+10 min", (Action)(() => SetTimer(svc, svc.data.timerSeconds + 600)));
                CycleBtns("Sonnerie", 3, () => svc.data.timerTone, v => { svc.data.timerTone = v; svc.MarkDirty(); svc.TestTone(v); }, i => Ar6dofLoc.T(Ar6dofClockService.ToneNames[i]));
            }
            Row2(60f, 26, phase == Ar6dofClockService.TimerPhase.Running ? "Pause" : phase == Ar6dofClockService.TimerPhase.Paused ? "Reprendre" : "Demarrer",
                 () => { svc.TimerStartPause(); Rebuild(); }, out tmStartLbl,
                 "Reinitialiser", () => { svc.TimerReset(); Rebuild(); });
            if (phase == Ar6dofClockService.TimerPhase.Idle) Para("Le minuteur sonne meme si tu fermes cette fenetre.", 36f, 17);
        }

        void Row2(float h, int fs, string a, Action ca, out Text la, string b, Action cb)
        {
            float cy = RowY(h);
            float w = (IW - 8f) * 0.5f;
            Text t;
            BtnAt(-IW * 0.5f + w * 0.5f, cy, w, h, a, ca, out la, fs);
            BtnAt(IW * 0.5f - w * 0.5f, cy, w, h, b, cb, out t, fs);
        }

        void SetTimer(Ar6dofClockService svc, int sec)
        {
            svc.SetTimerSeconds(sec);
            Rebuild();
        }

        // ------------------------------------------------------------------ alarmes
        static int AlarmSig(Ar6dofClockService s)
        {
            int h = s.data.alarms.Count;
            foreach (var a in s.data.alarms) h = h * 31 + (a.enabled ? 1 : 0) + a.hour * 3 + a.minute * 7 + a.days * 11 + (a.name ?? "").GetHashCode();
            return h;
        }

        string DaysText(Ar6dofAlarm a)
        {
            if (a.days == 0) return Ar6dofLoc.T("Une fois");
            if (a.days == 127) return Ar6dofLoc.T("Tous les jours");
            string s = "";
            for (int i = 0; i < 7; i++) if (a.RepeatsOn(i)) s += (s.Length > 0 ? " " : "") + Ar6dofClockFmt.DayShort(i);
            return s;
        }

        void BuildAlarms(Ar6dofClockService svc)
        {
            nextTxt = Lbl("", 34f, 21);
            refreshers.Add(UpdateNext);
            bool h24 = Ar6dofSettings.Current.clock24h;
            int n = 0;
            foreach (var a in svc.data.alarms)
            {
                if (n++ >= MaxAlarms) break;
                var al = a;
                float cy = RowY(62f);
                string txt = Ar6dofClockFmt.HourMinute(a.hour, a.minute, h24) + "    " + a.DisplayName + "    " + DaysText(a);
                Text t;
                BtnAt(-(IW * 0.5f) + (IW - 136f) * 0.5f, cy, IW - 136f, 62f, txt, () => { editing = al; Rebuild(); }, out t, 24);
                t.alignment = TextAnchor.MiddleLeft;
                t.rectTransform.sizeDelta = new Vector2(IW - 160f, 62f);
                Text ot;
                var ob = BtnAt(IW * 0.5f - 64f, cy, 128f, 62f, "", () => { al.enabled = !al.enabled; svc.MarkDirty(); Rebuild(); }, out ot, 24);
                ot.text = Ar6dofLoc.T(a.enabled ? "Active" : "Coupee");
                Ar6dofTheme.SetSelected(ob, a.enabled);
            }
            if (svc.data.alarms.Count == 0) Para("Aucune alarme. Touche « Nouvelle alarme ».", 36f, 19);
            if (svc.data.alarms.Count < MaxAlarms)
                Btn("+  Nouvelle alarme", () => { editing = svc.AddAlarm(); Rebuild(); }, 56f, 25);
            else
                Para("Nombre maximum d'alarmes atteint.", 34f, 18);
            SliderRow("Volume des sonneries", 0f, 1f, () => svc.data.volume, v => { svc.data.volume = v; svc.MarkDirty(); }, Pct);
            Para("Les alarmes sonnent meme si cette fenetre est fermee, tant que l'application tourne.", 40f, 17);
        }

        void UpdateNext()
        {
            if (nextTxt == null) return;
            var svc = Svc;
            if (svc == null) return;
            DateTime now = DateTime.Now;
            DateTime? best = null;
            Ar6dofAlarm who = null;
            foreach (var a in svc.data.alarms)
            {
                var n = a.Next(now);
                if (n.HasValue && (!best.HasValue || n.Value < best.Value)) { best = n; who = a; }
            }
            if (!best.HasValue) { nextTxt.text = Ar6dofLoc.T("Aucune alarme active"); return; }
            string day = best.Value.Date == now.Date ? Ar6dofLoc.T("aujourd'hui") : best.Value.Date == now.Date.AddDays(1) ? Ar6dofLoc.T("demain") : Ar6dofClockFmt.Date(best.Value, 1);
            nextTxt.text = Ar6dofLoc.F("Prochaine : {0} {1} (dans {2})", day, Ar6dofClockFmt.HourMinute(best.Value.Hour, best.Value.Minute, Ar6dofSettings.Current.clock24h), Ar6dofClockFmt.Span(best.Value - now));
        }

        // ------------------------------------------------------------------ édition d'une alarme
        void BuildEditor(Ar6dofClockService svc)
        {
            var a = editing;
            bool h24 = Ar6dofSettings.Current.clock24h;
            Lbl("Modifier l'alarme", 34f, 24, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            float ny = RowY(50f);
            Ar6dofVrTextField.Make(content, new Vector2(0f, ny), new Vector2(IW, 50f), a.name, Ar6dofLoc.T("Nom de l'alarme"), s => { a.name = s; }, s => { a.name = s; svc.MarkDirty(); }, 24);

            var big = Lbl("", 96f, 78, TextAnchor.MiddleCenter);
            big.fontStyle = FontStyle.Bold;
            refreshers.Add(() => big.text = Ar6dofClockFmt.HourMinute(a.hour, a.minute, Ar6dofSettings.Current.clock24h));

            BtnRow(54f, 22, "-1 h", (Action)(() => { a.hour = (a.hour + 23) % 24; Touch(svc); }), "+1 h", (Action)(() => { a.hour = (a.hour + 1) % 24; Touch(svc); }),
                            "-5 min", (Action)(() => AddMinutes(a, -5, svc)), "-1 min", (Action)(() => AddMinutes(a, -1, svc)),
                            "+1 min", (Action)(() => AddMinutes(a, 1, svc)), "+5 min", (Action)(() => AddMinutes(a, 5, svc)));
            if (!h24)
            {
                Text amLbl;
                BtnT("", () => { a.hour = (a.hour + 12) % 24; Touch(svc); }, out amLbl, 52f, 24);
                refreshers.Add(() => amLbl.text = Ar6dofLoc.T("Matin / apres-midi") + " : " + (a.hour < 12 ? "AM" : "PM"));
            }

            Lbl("Repeter les jours (aucun : une seule fois)", 30f, 20);
            var items = new object[14];
            for (int i = 0; i < 7; i++)
            {
                int k = i;
                items[2 * i] = Ar6dofClockFmt.DayShort(i);
                items[2 * i + 1] = (Action)(() => { a.days ^= 1 << k; svc.MarkDirty(); Refresh(); });
            }
            var dayBtns = BtnRow(52f, 20, items);
            refreshers.Add(() => { for (int i = 0; i < dayBtns.Count; i++) Ar6dofTheme.SetSelected(dayBtns[i], a.RepeatsOn(i)); });

            Text snLbl;
            BtnT("", () => { int idx = Array.IndexOf(SnoozeValues, a.snooze); a.snooze = SnoozeValues[(idx + 1) % SnoozeValues.Length]; svc.MarkDirty(); Refresh(); }, out snLbl, 50f, 22);
            refreshers.Add(() => snLbl.text = Ar6dofLoc.T("Repeter apres") + " : " + a.snooze + " min");

            Text toneLbl;
            BtnT("", () => { a.tone = (a.tone + 1) % 3; svc.MarkDirty(); svc.TestTone(a.tone); Refresh(); }, out toneLbl, 50f, 22);
            refreshers.Add(() => toneLbl.text = Ar6dofLoc.T("Sonnerie") + " : " + Ar6dofLoc.T(Ar6dofClockService.ToneNames[Mathf.Clamp(a.tone, 0, 2)]));

            ToggleRow("Alarme activee", () => a.enabled, v => { a.enabled = v; svc.MarkDirty(); });
            BtnRow(56f, 24, "Terminer", (Action)(() => { editing = null; svc.MarkDirty(); Rebuild(); }),
                            "Supprimer", (Action)(() => { svc.DeleteAlarm(a); editing = null; Rebuild(); }));
        }

        void Touch(Ar6dofClockService svc) { svc.MarkDirty(); Refresh(); }

        void AddMinutes(Ar6dofAlarm a, int delta, Ar6dofClockService svc)
        {
            int total = ((a.hour * 60 + a.minute + delta) % 1440 + 1440) % 1440;
            a.hour = total / 60; a.minute = total % 60;
            Touch(svc);
        }

        // ------------------------------------------------------------------ à chaque image (fenêtre ouverte seulement)
        protected override void OnTick()
        {
            var svc = Svc;
            if (svc == null) return;
            DateTime now = DateTime.Now;
            switch (tab)
            {
                case 0:
                {
                    if (face == null) break;
                    face.SetTime(now.TimeOfDay.TotalSeconds);
                    int stamp = now.Hour * 3600 + now.Minute * 60 + now.Second;
                    if (stamp != lastStamp)
                    {
                        lastStamp = stamp;
                        var st = Ar6dofSettings.Current;
                        if (bigTime != null) bigTime.text = Ar6dofClockFmt.Time(now, st.clock24h, st.clockSeconds);
                        if (dateTxt != null) dateTxt.text = Ar6dofClockFmt.Date(now, st.dateFormat);
                    }
                    break;
                }
                case 1:
                {
                    if (svc.laps.Count != seenLaps) { Rebuild(); return; }
                    if (swTxt == null) break;
                    if (Time.unscaledTime < nextText && svc.SwRunning) break;
                    nextText = Time.unscaledTime + 0.04f;
                    double e = svc.SwElapsed;
                    string s = Ar6dofClockFmt.Stopwatch(e);
                    if (s != lastSw) { lastSw = s; swTxt.text = s; }
                    if (dial != null) dial.SetElapsed(e);
                    if (prog != null) prog.SetRemaining((float)(e % 60.0 / 60.0), false);
                    break;
                }
                case 2:
                {
                    if (svc.Phase != seenPhase) { Rebuild(); return; }
                    if (prog == null || tmTxt == null) break;
                    double left = svc.TimerLeft;
                    double total = Math.Max(1, svc.data.timerSeconds);
                    bool done = svc.Phase == Ar6dofClockService.TimerPhase.Done;
                    prog.SetRemaining((float)(left / total), done);
                    string s = done ? Ar6dofLoc.T("Temps ecoule") : Ar6dofClockFmt.Countdown(left);
                    if (s != lastTm) { lastTm = s; tmTxt.text = s; }
                    break;
                }
                default:
                    if (editing == null && AlarmSig(svc) != seenAlarmSig) Rebuild();
                    break;
            }
        }
    }
}
