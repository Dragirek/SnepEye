using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Calendrier : liste des evenements a venir, ajout / modification a la main, et SCAN d'une affiche ou d'un programme de convention devant toi.
    /// Le scan ne demarre que quand tu touches « Lancer le scan » ; c'est le MEME scan que celui de l'appareil photo (Ar6dofScanSession) : photo apres 3 s avec la camera
    /// gauche (image corrigee, sans les interfaces), lecture d'un QR code (un QR iCalendar donne directement ses evenements), sinon lecture du texte par l'OCR de Windows
    /// (Ar6dofOcr, dans un autre fil) puis Ar6dofCalParser en tire des evenements que tu relis, corriges et enregistres. Chaque evenement
    /// enregistre sonne quelques minutes avant son debut (10 par defaut, reglable) meme application fermee, et laisse une ligne dans les notifications.
    /// Fermee, la fenetre ne calcule rien ; la photo du scan est temporaire et supprimee des que le texte est lu.
    /// </summary>
    public class Ar6dofCalendarApp : Ar6dofScanWindow
    {
        class ScanItem { public Ar6dofCalEvent ev; public bool keep; }

        const int PerPage = 5;
        int tab, page;
        Ar6dofCalEvent ed, edOrig;     // ed : copie en cours de modification ; edOrig : l'evenement d'origine (null = nouveau ou ligne du scan)
        ScanItem edScan;
        string agendaMsg = "";
        readonly List<ScanItem> found = new List<ScanItem>();
        bool builtFound, showRaw, confirmClear;

        public Ar6dofCalendarApp(Ar6dofDock d) : base(d, "calendar", "Calendrier", "calendar",
            "Evenements avec rappel avant leur debut, et scan d'une affiche ou d'un programme de convention.", 680f) { }

        static Ar6dofCalendarService Svc { get { return Ar6dofCalendarService.Instance; } }

        public override void OnClosed()
        {
            base.OnClosed();   // annule la photo et la lecture, supprime les images temporaires
            found.Clear(); builtFound = false;
            ed = null; edOrig = null; edScan = null;
            agendaMsg = "";
            confirmClear = false; showRaw = false; tab = 0; page = 0;
        }

        protected override void OnTick() { TickScan(); }

        // la lecture est finie : les evenements trouves (QR iCalendar ou texte lu) deviennent des lignes a relire
        protected override void OnScanChanged()
        {
            if (scan.phase != Ar6dofScanSession.Phase.Done) builtFound = false;   // nouveau scan ou annulation : l'ancienne liste ne vaut plus
            else if (!builtFound) BuildFound();
            base.OnScanChanged();
        }

        void BuildFound()
        {
            builtFound = true;
            found.Clear();
            page = 0;
            DateTime now = DateTime.Now;
            foreach (var p in scan.Events(now))
            {
                var ev = new Ar6dofCalEvent { title = p.title, place = p.place, durMin = p.durMin, remind = true };
                ev.Start = p.start;
                found.Add(new ScanItem { ev = ev, keep = p.start > now });   // un horaire deja passe est decoche d'office
            }
        }

        // ------------------------------------------------------------------ fenetre
        protected override void BuildMain()
        {
            var svc = Svc;
            if (svc == null) { Para("Calendrier indisponible.", 40, 20); return; }
            if (ed != null) { BuildEdit(svc); return; }
            Tabs(new[] { "Agenda", "Scanner", "Options" }, tab, t => { tab = t; page = 0; confirmClear = false; });
            if (tab == 0) BuildAgenda(svc);
            else if (tab == 1) BuildScan(svc);
            else BuildOptions(svc);
        }

        string Line(Ar6dofCalEvent e)
        {
            bool h24 = Ar6dofSettings.Current.clock24h;
            return Ar6dofClockFmt.Date(e.Start, 1) + "  " + Ar6dofClockFmt.Time(e.Start, h24, false) + "  " + e.DisplayTitle + (e.remind ? "" : "  " + Ar6dofLoc.T("(sans rappel)"));
        }

        // ------------------------------------------------------------------ agenda
        void BuildAgenda(Ar6dofCalendarService svc)
        {
            if (agendaMsg.Length > 0) Lbl(agendaMsg, 30, 19).MakeAccent();
            Btn("Ajouter un evenement", () => StartEdit(null), 52f, 22);
            var list = svc.Upcoming();
            if (list.Count == 0)
            {
                Para("Aucun evenement. Va dans Scanner pour lire une affiche ou un programme, ou ajoute-en un a la main.", 56, 19);
                return;
            }
            int pages = Mathf.Max(1, (list.Count + PerPage - 1) / PerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            for (int i = page * PerPage; i < Mathf.Min(list.Count, (page + 1) * PerPage); i++)
            {
                var e = list[i];
                Btn(Line(e), () => StartEdit(e), 56f, 21);
            }
            if (pages > 1)
            {
                BtnRow(46f, 22, "<", (Action)(() => { page = (page + pages - 1) % pages; Rebuild(); }),
                                Ar6dofLoc.F("Page {0}/{1}", page + 1, pages), (Action)(() => { }),
                                ">", (Action)(() => { page = (page + 1) % pages; Rebuild(); }));
            }
        }

        // ------------------------------------------------------------------ modification d'un evenement
        void StartEdit(Ar6dofCalEvent orig)
        {
            edOrig = orig;
            edScan = null;
            if (orig == null)
            {
                var t = DateTime.Now.AddHours(1);
                ed = new Ar6dofCalEvent { Start = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0) };
            }
            else ed = orig.Clone();
            Rebuild();
        }

        void StartEditScan(ScanItem it)
        {
            edOrig = null;
            edScan = it;
            ed = it.ev.Clone();
            Rebuild();
        }

        void EndEdit()
        {
            ed = null; edOrig = null; edScan = null;
            Rebuild();
        }

        void BuildEdit(Ar6dofCalendarService svc)
        {
            Section(edOrig == null && edScan == null ? "Nouvel evenement" : "Modifier l'evenement");
            float cy = RowY(52f);
            Ar6dofVrTextField.Make(content, new Vector2(0f, cy), new Vector2(IW, 52f), ed.title, Ar6dofLoc.T("Nom de l'evenement"), v => ed.title = v, v => ed.title = v, 22);
            cy = RowY(52f);
            Ar6dofVrTextField.Make(content, new Vector2(0f, cy), new Vector2(IW, 52f), ed.place, Ar6dofLoc.T("Lieu (facultatif)"), v => ed.place = v, v => ed.place = v, 22);

            var db = BtnRow(52f, 20, "-7 j", (Action)(() => { ed.Start = ed.Start.AddDays(-7); Refresh(); }),
                                     "-1 j", (Action)(() => { ed.Start = ed.Start.AddDays(-1); Refresh(); }),
                                     "", (Action)(() => { }),
                                     "+1 j", (Action)(() => { ed.Start = ed.Start.AddDays(1); Refresh(); }),
                                     "+7 j", (Action)(() => { ed.Start = ed.Start.AddDays(7); Refresh(); }));
            var dateTxt = db[2].GetComponentInChildren<Text>();
            var tb = BtnRow(52f, 20, "-1 h", (Action)(() => { ed.Start = ed.Start.AddHours(-1); Refresh(); }),
                                     "-5 min", (Action)(() => { ed.Start = ed.Start.AddMinutes(-5); Refresh(); }),
                                     "", (Action)(() => { }),
                                     "+5 min", (Action)(() => { ed.Start = ed.Start.AddMinutes(5); Refresh(); }),
                                     "+1 h", (Action)(() => { ed.Start = ed.Start.AddHours(1); Refresh(); }));
            var timeTxt = tb[2].GetComponentInChildren<Text>();
            var ub = BtnRow(52f, 20, "-15 min", (Action)(() => { ed.durMin = Mathf.Max(0, ed.durMin - 15); Refresh(); }),
                                     "", (Action)(() => { }),
                                     "+15 min", (Action)(() => { ed.durMin = Mathf.Min(24 * 60, ed.durMin + 15); Refresh(); }));
            var durTxt = ub[1].GetComponentInChildren<Text>();
            refreshers.Add(() =>
            {
                dateTxt.text = Ar6dofClockFmt.Date(ed.Start, Ar6dofSettings.Current.dateFormat);
                timeTxt.text = Ar6dofClockFmt.Time(ed.Start, Ar6dofSettings.Current.clock24h, false);
                durTxt.text = ed.durMin <= 0 ? Ar6dofLoc.T("Duree inconnue") : Ar6dofLoc.T("Duree") + " " + Ar6dofClockFmt.Span(TimeSpan.FromMinutes(ed.durMin));
            });

            ToggleRow("Rappel avant le debut", () => ed.remind, v => ed.remind = v);
            Lbl(Ar6dofLoc.F("Le rappel sonne {0} min avant (a regler dans Options).", svc.data.leadMin), 28, 17);

            var items = new List<object> { "Enregistrer", (Action)(() => SaveEdit(svc)) };
            if (edOrig != null) { items.Add("Supprimer"); items.Add((Action)(() => { svc.Delete(edOrig); EndEdit(); })); }
            else if (edScan != null) { items.Add("Retirer"); items.Add((Action)(() => { found.Remove(edScan); EndEdit(); })); }
            items.Add("Annuler"); items.Add((Action)EndEdit);
            BtnRow(56f, 22, items.ToArray());
        }

        void SaveEdit(Ar6dofCalendarService svc)
        {
            if (edScan != null) { edScan.ev = ed; edScan.keep = true; }
            else if (edOrig != null)
            {
                bool moved = edOrig.startTicks != ed.startTicks || edOrig.remind != ed.remind;
                edOrig.title = ed.title; edOrig.place = ed.place; edOrig.startTicks = ed.startTicks; edOrig.durMin = ed.durMin; edOrig.remind = ed.remind;
                if (moved) edOrig.reminded = false;   // nouvelle heure : le rappel est de nouveau attendu
                svc.MarkDirty();
            }
            else svc.Add(ed);
            EndEdit();
        }

        // ------------------------------------------------------------------ scan
        void BuildScan(Ar6dofCalendarService svc)
        {
            switch (scan.phase)
            {
                case Ar6dofScanSession.Phase.Idle:
                    Para("Tiens l'affiche, le programme ou un QR code bien en face de toi, lisible et bien eclaire, sans bouger. Touche Lancer : photo dans 3 secondes, lecture du QR code, sinon du texte par Windows, puis tu relis les evenements avant de les enregistrer. Rien ne tourne avant ton clic.", 124, 18);
                    var go = Btn("Lancer le scan", BeginScan, 64f, 26);
                    Ar6dofTheme.SetSelected(go, true);
                    break;
                case Ar6dofScanSession.Phase.Countdown:
                case Ar6dofScanSession.Phase.Reading:
                    BuildScanBusy();
                    break;
                default:
                    if (!builtFound) BuildFound();
                    BuildReview(svc);
                    break;
            }
        }

        void BeginScan()
        {
            found.Clear(); builtFound = false;
            showRaw = false; page = 0;
            StartScan(true);
        }

        void BuildReview(Ar6dofCalendarService svc)
        {
            if (scan.error.Length > 0) Para(scan.error, 80, 19);
            else if (found.Count == 0 && scan.qr != null) { BuildScanResult(false, false); return; }   // un QR qui n'est pas un evenement : on le montre, tu peux le copier
            else if (found.Count == 0) Para("Aucun horaire reconnu. Rapproche-toi, ameliore la lumiere, ou regarde le texte lu ci-dessous.", 56, 19);
            else
            {
                Para(Ar6dofLoc.F("{0} evenement(s) trouve(s). Decoche ceux que tu ne veux pas, touche une ligne pour corriger.", found.Count), 56, 18);
                int pages = Mathf.Max(1, (found.Count + PerPage - 1) / PerPage);
                page = Mathf.Clamp(page, 0, pages - 1);
                for (int i = page * PerPage; i < Mathf.Min(found.Count, (page + 1) * PerPage); i++)
                {
                    var it = found[i];
                    float cy = RowY(56f);
                    Text cl;
                    var cb = BtnAt(-IW * 0.5f + 30f, cy, 60f, 56f, "", () => { it.keep = !it.keep; Refresh(); }, out cl, 22);
                    var clx = cl;
                    var cbx = cb;
                    refreshers.Add(() => { clx.text = it.keep ? "[x]" : "[  ]"; Ar6dofTheme.SetSelected(cbx, it.keep); });
                    Text rl;
                    BtnAt(-IW * 0.5f + 68f + (IW - 68f) * 0.5f, cy, IW - 68f, 56f, Line(it.ev), () => StartEditScan(it), out rl, 20);
                }
                if (pages > 1)
                {
                    BtnRow(46f, 22, "<", (Action)(() => { page = (page + pages - 1) % pages; Rebuild(); }),
                                    Ar6dofLoc.F("Page {0}/{1}", page + 1, pages), (Action)(() => { }),
                                    ">", (Action)(() => { page = (page + 1) % pages; Rebuild(); }));
                }
                Text sl;
                var save = BtnT("", () => SaveScan(svc), out sl, 56f, 24);
                Ar6dofTheme.SetSelected(save, true);
                refreshers.Add(() =>
                {
                    int n = 0;
                    foreach (var f in found) if (f.keep) n++;
                    sl.text = Ar6dofLoc.F("Enregistrer ({0})", n);
                });
            }
            BtnRow(48f, 21, "Nouveau scan", (Action)BeginScan,
                            showRaw ? "Cacher le texte lu" : "Voir le texte lu", (Action)(() => { showRaw = !showRaw; Rebuild(); }));
            if (showRaw)
            {
                var raw = scan.qr != null ? new List<string>(scan.qr.Replace("\r", "").Split('\n')) : scan.lines;
                if (scan.info.Length > 0) Para(scan.info, 28, 15);
                if (raw.Count == 0) Para("Aucun texte lu.", 30, 18);
                else
                {
                    var sb = new StringBuilder();
                    int n = Mathf.Min(raw.Count, 12);
                    for (int i = 0; i < n; i++) { if (i > 0) sb.Append('\n'); sb.Append(raw[i].Length > 70 ? raw[i].Substring(0, 70) : raw[i]); }
                    if (raw.Count > n) sb.Append("\n...");
                    Para(sb.ToString(), 24f * (n + 1) + 6f, 16);
                }
            }
        }

        void SaveScan(Ar6dofCalendarService svc)
        {
            var list = new List<Ar6dofCalEvent>();
            foreach (var f in found) if (f.keep) list.Add(f.ev);
            int added = svc.AddRange(list);
            agendaMsg = added == 0 ? Ar6dofLoc.T("Rien de nouveau a enregistrer") : Ar6dofLoc.F("{0} evenement(s) ajoute(s)", added);
            found.Clear(); builtFound = false;
            AbortScan();
            tab = 0; page = 0;
            Rebuild();
        }

        // ------------------------------------------------------------------ options
        void BuildOptions(Ar6dofCalendarService svc)
        {
            Section("Rappel");
            Lbl("Le rappel sonne avant le debut de l'evenement :", 30, 19);
            var items = new List<object>();
            foreach (int c in Ar6dofCalendarService.LeadChoices)
            {
                int cc = c;
                items.Add(c + " min");
                items.Add((Action)(() => { svc.data.leadMin = cc; svc.MarkDirty(); Rebuild(); }));
            }
            var bs = BtnRow(52f, 21, items.ToArray());
            for (int i = 0; i < bs.Count; i++) Ar6dofTheme.SetSelected(bs[i], Ar6dofCalendarService.LeadChoices[i] == svc.data.leadMin);
            Para("Le rappel passe par la sonnerie de l'horloge et laisse une ligne dans les notifications. Il sonne meme si cette fenetre est fermee.", 56, 18);
            Section("Scan");
            Para("La photo du scan est lue par Windows sur ton PC (rien n'est envoye sur Internet), puis supprimee tout de suite.", 56, 18);
            Section("Donnees");
            Btn(confirmClear ? "Confirmer : tout effacer ?" : "Effacer tous les evenements", () =>
            {
                if (!confirmClear) { confirmClear = true; Rebuild(); return; }
                svc.Clear();
                confirmClear = false;
                agendaMsg = "";
                Rebuild();
            }, 52f, 22);
        }
    }
}
