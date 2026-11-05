using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AR6DoF
{
    /// <summary>Un evenement trouve dans le texte d'une affiche ou d'un programme.</summary>
    public class Ar6dofParsedEvent
    {
        public string title = "", place = "";
        public DateTime start;
        public int durMin;   // 0 = duree inconnue
    }

    /// <summary>
    /// Lit les lignes de texte trouvees par la reconnaissance de texte (OCR) sur une affiche ou un programme de convention et en tire des evenements
    /// (titre, jour, heure, duree, lieu). Pas de Unity ici : du texte entre, des evenements sortent. Port ligne a ligne de tests/calendar_parse_ref.py,
    /// qui contient les cas d'essai. C'est une estimation : l'OCR se trompe parfois, l'utilisateur relit et corrige avant d'enregistrer.
    /// Comprend « 14h30 », « 14:30 », « 14 h », « 2:30 PM », « 14h - 16h », « samedi 7 novembre », « 7/11 », « Nov 7 », un jour seul en entete
    /// (SAMEDI) qui vaut pour les lignes suivantes, un titre sur la ligne de l'heure ou juste apres (ou juste avant), un lieu entre parentheses ou « Salle B ».
    /// </summary>
    public static class Ar6dofCalParser
    {
        static readonly Dictionary<string, int> Months = new Dictionary<string, int>
        {
            {"janvier",1},{"janv",1},{"jan",1},{"january",1},
            {"fevrier",2},{"fevr",2},{"fev",2},{"february",2},{"feb",2},
            {"mars",3},{"mar",3},{"march",3},
            {"avril",4},{"avr",4},{"april",4},{"apr",4},
            {"mai",5},{"may",5},
            {"juin",6},{"june",6},{"jun",6},
            {"juillet",7},{"juil",7},{"july",7},{"jul",7},
            {"aout",8},{"august",8},{"aug",8},
            {"septembre",9},{"sept",9},{"sep",9},{"september",9},
            {"octobre",10},{"oct",10},{"october",10},
            {"novembre",11},{"nov",11},{"november",11},
            {"decembre",12},{"dec",12},{"december",12},
        };
        static readonly Dictionary<string, int> WeekDays = new Dictionary<string, int>
        {
            {"lundi",0},{"mardi",1},{"mercredi",2},{"jeudi",3},{"vendredi",4},{"samedi",5},{"dimanche",6},
            {"monday",0},{"tuesday",1},{"wednesday",2},{"thursday",3},{"friday",4},{"saturday",5},{"sunday",6},
        };
        static readonly Dictionary<string, int> WdAbbr = new Dictionary<string, int>
        {
            {"lun",0},{"mar",1},{"mer",2},{"jeu",3},{"ven",4},{"sam",5},{"dim",6},{"mon",0},{"tue",1},{"wed",2},{"thu",3},{"fri",4},{"sat",5},{"sun",6},
        };

        static string Alt(IEnumerable<string> keys)
        {
            var l = new List<string>(keys);
            l.Sort((a, b) => b.Length != a.Length ? b.Length.CompareTo(a.Length) : string.CompareOrdinal(a, b));   // le plus long d'abord
            return string.Join("|", l.ToArray());
        }

        static readonly string MonAlt = Alt(Months.Keys), WdfAlt = Alt(WeekDays.Keys), WdaAlt = Alt(WdAbbr.Keys);
        const RegexOptions Ro = RegexOptions.CultureInvariant;

        static readonly Regex TimeRe = new Regex(@"(?<![\d:/.,])(\d{1,2})(?:\s*h(?:\s*(\d{2})(?!\d)|(?![a-z]))|:(\d{2})(?!\d)|(?=\s*[ap]\.?m\b))\s*(?:([ap])\.?m\b\.?)?", Ro);
        static readonly Regex SepRe = new Regex(@"^\s*(?:-|\u2013|\u2014|a|au|to|until|till|jusqu'?a|->|\u2192)\s*$", Ro);
        static readonly Regex DateNumRe = new Regex(@"(?<![\d/.:])(\d{1,2})[/.](\d{1,2})(?:[/.](\d{4}|\d{2}))?(?![\d:])", Ro);
        static readonly Regex DateTxtRe = new Regex(@"(?:\b(?:" + WdfAlt + "|(?:" + WdaAlt + @")\.?)\s+)?\b(\d{1,2})(?:er|st|nd|rd|th)?\s+(" + MonAlt + @")\b\.?(?:,?\s+(\d{4}))?", Ro);
        static readonly Regex DateEnRe = new Regex(@"\b(" + MonAlt + @")\.?\s+(\d{1,2})(?:st|nd|rd|th)?\b(?:,?\s+(\d{4}))?", Ro);
        static readonly Regex WdRe = new Regex(@"\b(" + WdfAlt + @")\b|\b(" + WdaAlt + @")(?:\.|(?=\s+\d))", Ro);
        static readonly Regex PlaceParenRe = new Regex(@"\(([^()]{2,40})\)\s*$", Ro);
        static readonly Regex PlaceKeyRe = new Regex(@"(?:^|[,;|\-\u2013\u2014@]\s*)((?:salle|room|hall|scene|stage|amphi|lieu|location|stand|espace)\b.{0,30})$", Ro);
        static readonly Regex PlaceStartRe = new Regex(@"^(?:salle|room|hall|scene|stage|amphi|lieu|location|stand|espace)\b", Ro);
        static readonly Regex SpacesRe = new Regex(@"\s+", Ro);
        static readonly char[] TrimChars = { ' ', '\t', '.', ',', ';', ':', '|', '-', '\u2013', '\u2014', '\u2022', '\u00b7', '*', '>', '_', '"', '\'', '[', ']', '(', ')', '/', '\\' };

        struct TimeHit { public int h, mi, s, e; }
        class DateHit { public DateTime date; public int s, e; }

        /// <summary>Minuscules sans accents, un caractere pour un caractere (les positions restent valables dans le texte d'origine).</summary>
        public static string Fold(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c0 in s)
            {
                char c = char.ToLowerInvariant(c0);
                if ("\u00e0\u00e1\u00e2\u00e3\u00e4".IndexOf(c) >= 0) c = 'a';
                else if (c == '\u00e7') c = 'c';
                else if ("\u00e8\u00e9\u00ea\u00eb".IndexOf(c) >= 0) c = 'e';
                else if ("\u00ec\u00ed\u00ee\u00ef".IndexOf(c) >= 0) c = 'i';
                else if (c == '\u00f1') c = 'n';
                else if ("\u00f2\u00f3\u00f4\u00f5\u00f6\u0153".IndexOf(c) >= 0) c = 'o';
                else if ("\u00f9\u00fa\u00fb\u00fc".IndexOf(c) >= 0) c = 'u';
                else if (c == '\u00fd' || c == '\u00ff') c = 'y';
                else if (c == '\u00a0' || c == '\u2009' || c == '\u202f' || c == '\t') c = ' ';
                else if (c == '\u2019' || c == '\u2018' || c == '`' || c == '\u00b4') c = '\'';
                sb.Append(c);
            }
            return sb.ToString();
        }

        static DateTime? Mk(int y, int m, int d)
        {
            if (y < 1 || y > 9998 || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return null;
            return new DateTime(y, m, d);
        }

        static DateTime? Resolve(DateTime today, int d, int m, int y)
        {
            if (y < 0)
            {
                var c = Mk(today.Year, m, d);
                if (c == null) return null;
                if ((today - c.Value).TotalDays > 60) c = Mk(today.Year + 1, m, d);
                return c;
            }
            if (y < 100) y += 2000;
            return Mk(y, m, d);
        }

        static DateTime NextWeekday(DateTime today, int wd)
        {
            int cur = ((int)today.DayOfWeek + 6) % 7;   // lundi = 0
            return today.AddDays(((wd - cur) % 7 + 7) % 7);
        }

        static int Num(Group g) { return g.Success ? int.Parse(g.Value) : -1; }

        static DateHit FindDate(string f, DateTime today)
        {
            var m = DateTxtRe.Match(f);
            if (m.Success)
            {
                var d = Resolve(today, int.Parse(m.Groups[1].Value), Months[m.Groups[2].Value], Num(m.Groups[3]));
                if (d != null) return new DateHit { date = d.Value, s = m.Index, e = m.Index + m.Length };
            }
            m = DateEnRe.Match(f);
            if (m.Success)
            {
                var d = Resolve(today, int.Parse(m.Groups[2].Value), Months[m.Groups[1].Value], Num(m.Groups[3]));
                if (d != null) return new DateHit { date = d.Value, s = m.Index, e = m.Index + m.Length };
            }
            m = DateNumRe.Match(f);
            if (m.Success)
            {
                int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
                if (a >= 1 && a <= 31 && b >= 1 && b <= 12)
                {
                    var d = Resolve(today, a, b, Num(m.Groups[3]));
                    if (d != null) return new DateHit { date = d.Value, s = m.Index, e = m.Index + m.Length };
                }
            }
            m = WdRe.Match(f);
            if (m.Success)
            {
                string w = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                int wd;
                if (!WeekDays.TryGetValue(w, out wd) && !WdAbbr.TryGetValue(w, out wd)) return null;
                return new DateHit { date = NextWeekday(today, wd), s = m.Index, e = m.Index + m.Length };
            }
            return null;
        }

        static List<TimeHit> FindTimes(string f)
        {
            var res = new List<TimeHit>();
            foreach (Match m in TimeRe.Matches(f))
            {
                int h = int.Parse(m.Groups[1].Value);
                int mi = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : (m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
                if (m.Groups[4].Success)
                {
                    char ap = m.Groups[4].Value[0];
                    if (h < 1 || h > 12) continue;
                    if (ap == 'p' && h < 12) h += 12;
                    if (ap == 'a' && h == 12) h = 0;
                }
                if (h > 23 || mi > 59) continue;
                res.Add(new TimeHit { h = h, mi = mi, s = m.Index, e = m.Index + m.Length });
            }
            return res;
        }

        static string CleanTitle(string s)
        {
            s = SpacesRe.Replace(s, " ");
            return s.Trim(TrimChars);
        }

        static int Letters(string s)
        {
            int n = 0;
            foreach (char c in s) if (char.IsLetter(c)) n++;
            return n;
        }

        static void Blank(char[] txt, int a, int b)
        {
            for (int k = a; k < b && k < txt.Length; k++) txt[k] = ' ';
        }

        static string StripTimesAndDate(string line, string f, List<TimeHit> ts, DateHit dd)
        {
            var txt = line.ToCharArray();
            for (int i = 0; i < ts.Count && i < 2; i++) Blank(txt, ts[i].s, ts[i].e);
            if (dd != null) Blank(txt, dd.s, dd.e);
            foreach (Match mw in WdRe.Matches(f)) Blank(txt, mw.Index, mw.Index + mw.Length);
            return new string(txt);
        }

        public static List<Ar6dofParsedEvent> Parse(IList<string> lines, DateTime todayDt, int maxEvents = 40)
        {
            DateTime today = todayDt.Date;
            int n = lines.Count;
            var fl = new string[n];
            var times = new List<TimeHit>[n];
            var header = new bool[n];
            var hdate = new DateTime[n];
            for (int i = 0; i < n; i++)
            {
                fl[i] = Fold(lines[i]);
                times[i] = FindTimes(fl[i]);
            }
            for (int i = 0; i < n; i++)
            {
                if (times[i].Count > 0 || lines[i].Length > 60) continue;
                var r = FindDate(fl[i], today);
                if (r != null) { header[i] = true; hdate[i] = r.date; }
            }
            Func<int, bool> usable = j =>
            {
                if (j < 0 || j >= n) return false;
                if (times[j].Count > 0 || header[j] || Letters(lines[j]) < 2) return false;
                if (PlaceStartRe.IsMatch(fl[j].Trim())) return false;
                return true;
            };
            // titre seul sur sa ligne par rapport a l'heure : apres l'heure (par defaut) ou avant
            int nxt = 0, prv = 0;
            for (int i = 0; i < n; i++)
            {
                if (times[i].Count == 0) continue;
                string rest0 = StripTimesAndDate(lines[i], fl[i], times[i], FindDate(fl[i], today));
                if (Letters(rest0) >= 2) continue;
                if (usable(i + 1)) nxt++;
                if (usable(i - 1)) prv++;
            }
            bool above = prv > nxt;
            var used = new bool[n];
            bool haveCtx = false;
            DateTime ctx = today;
            var outList = new List<Ar6dofParsedEvent>();
            for (int i = 0; i < n; i++)
            {
                if (header[i]) { ctx = hdate[i]; haveCtx = true; continue; }
                if (times[i].Count == 0) continue;
                var ts = times[i];
                int h = ts[0].h, mi = ts[0].mi;
                int dur = 0;
                var masks = new List<int[]> { new[] { ts[0].s, ts[0].e } };
                if (ts.Count > 1)
                {
                    int s1 = ts[1].s, e0 = ts[0].e;
                    if (s1 >= e0 && SepRe.IsMatch(fl[i].Substring(e0, s1 - e0)))
                    {
                        int d = (ts[1].h * 60 + ts[1].mi) - (h * 60 + mi);
                        if (d <= 0) d += 1440;
                        dur = d;
                        masks.Add(new[] { ts[1].s, ts[1].e });
                        masks.Add(new[] { e0, s1 });
                    }
                }
                var dd = FindDate(fl[i], today);
                if (dd != null) masks.Add(new[] { dd.s, dd.e });
                foreach (Match mw in WdRe.Matches(fl[i])) masks.Add(new[] { mw.Index, mw.Index + mw.Length });
                DateTime day = dd != null ? dd.date : (haveCtx ? ctx : today);
                var txt = lines[i].ToCharArray();
                foreach (var mk0 in masks) Blank(txt, mk0[0], mk0[1]);
                string rest = new string(txt);
                string place = "";
                var mp = PlaceParenRe.Match(rest);
                if (mp.Success)
                {
                    place = CleanTitle(mp.Groups[1].Value);
                    rest = rest.Substring(0, mp.Index);
                }
                else
                {
                    var mk = PlaceKeyRe.Match(Fold(rest));
                    if (mk.Success)
                    {
                        int at = mk.Groups[1].Index;
                        place = CleanTitle(rest.Substring(at));
                        rest = rest.Substring(0, at);
                    }
                }
                string title = CleanTitle(rest);
                if (Letters(title) < 2)
                {
                    title = "";
                    int j = above ? i - 1 : i + 1;
                    if (usable(j) && !used[j])
                    {
                        title = CleanTitle(lines[j]);
                        used[j] = true;
                        int k = !above ? j + 1 : i + 1;
                        if (place.Length == 0 && k >= 0 && k < n && times[k].Count == 0 && !header[k] && PlaceStartRe.IsMatch(fl[k].Trim()))
                        {
                            place = CleanTitle(lines[k]);
                            used[k] = true;
                        }
                    }
                }
                if (place.Length == 0)
                {
                    int k = i + 1;
                    if (k >= 0 && k < n && times[k].Count == 0 && !header[k] && PlaceStartRe.IsMatch(fl[k].Trim()) && !used[k])
                    {
                        place = CleanTitle(lines[k]);
                        used[k] = true;
                    }
                }
                if (Letters(title) < 2) title = "";
                if (title.Length > 80) title = title.Substring(0, 80);
                if (place.Length > 60) place = place.Substring(0, 60);
                outList.Add(new Ar6dofParsedEvent { title = title, place = place, start = day.AddHours(h).AddMinutes(mi), durMin = dur });
            }
            var seen = new HashSet<string>();
            var res = new List<Ar6dofParsedEvent>();
            foreach (var e in outList.OrderBy(x => x.start))   // OrderBy est stable
            {
                string key = e.start.Ticks + "|" + Fold(e.title);
                if (!seen.Add(key)) continue;
                res.Add(e);
                if (res.Count >= maxEvents) break;
            }
            return res;
        }
    }
}
