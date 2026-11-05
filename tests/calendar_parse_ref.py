#!/usr/bin/env python3
"""Reference of the AR6DoF calendar parser (Ar6dofCalParser.cs): reads the text lines found by OCR on a poster or a convention booklet
and extracts events (title, start, duration). The C# version is a line-by-line port of this file; the tests below check the heuristics.
Run: python3 tests/calendar_parse_ref.py"""
import re, datetime as dt

MONTHS = {
 'janvier':1,'janv':1,'jan':1,'january':1,
 'fevrier':2,'fevr':2,'fev':2,'february':2,'feb':2,
 'mars':3,'mar':3,'march':3,
 'avril':4,'avr':4,'april':4,'apr':4,
 'mai':5,'may':5,
 'juin':6,'june':6,'jun':6,
 'juillet':7,'juil':7,'july':7,'jul':7,
 'aout':8,'august':8,'aug':8,
 'septembre':9,'sept':9,'sep':9,'september':9,
 'octobre':10,'oct':10,'october':10,
 'novembre':11,'nov':11,'november':11,
 'decembre':12,'dec':12,'december':12}
WEEKDAYS = {'lundi':0,'mardi':1,'mercredi':2,'jeudi':3,'vendredi':4,'samedi':5,'dimanche':6,
 'monday':0,'tuesday':1,'wednesday':2,'thursday':3,'friday':4,'saturday':5,'sunday':6}
WD_ABBR = {'lun':0,'mar':1,'mer':2,'jeu':3,'ven':4,'sam':5,'dim':6,'mon':0,'tue':1,'wed':2,'thu':3,'fri':4,'sat':5,'sun':6}

def fold(s):
    out = []
    for c in s:
        c = c.lower()
        if c in 'àáâãä': c = 'a'
        elif c == 'ç': c = 'c'
        elif c in 'èéêë': c = 'e'
        elif c in 'ìíîï': c = 'i'
        elif c == 'ñ': c = 'n'
        elif c in 'òóôõöœ': c = 'o'
        elif c in 'ùúûü': c = 'u'
        elif c in 'ýÿ': c = 'y'
        elif c in '\u00a0\u2009\u202f\t': c = ' '
        elif c in '\u2019\u2018`´': c = "'"
        out.append(c)
    return ''.join(out)

_mon = '|'.join(sorted(MONTHS, key=lambda k: -len(k)))
_wdf = '|'.join(sorted(WEEKDAYS, key=lambda k: -len(k)))
_wda = '|'.join(sorted(WD_ABBR, key=lambda k: -len(k)))

TIME_RE = re.compile(r"(?<![\d:/.,])(\d{1,2})(?:\s*h(?:\s*(\d{2})(?!\d)|(?![a-z]))|:(\d{2})(?!\d)|(?=\s*[ap]\.?m\b))\s*(?:([ap])\.?m\b\.?)?")
SEP_RE = re.compile(r"^\s*(?:-|\u2013|\u2014|a|au|to|until|till|jusqu'?a|->|\u2192)\s*$")
DATE_NUM_RE = re.compile(r"(?<![\d/.:])(\d{1,2})[/.](\d{1,2})(?:[/.](\d{4}|\d{2}))?(?![\d:])")
DATE_TXT_RE = re.compile(r"(?:\b(?:" + _wdf + r"|(?:" + _wda + r")\.?)\s+)?\b(\d{1,2})(?:er|st|nd|rd|th)?\s+(" + _mon + r")\b\.?(?:,?\s+(\d{4}))?")
DATE_EN_RE = re.compile(r"\b(" + _mon + r")\.?\s+(\d{1,2})(?:st|nd|rd|th)?\b(?:,?\s+(\d{4}))?")
WD_RE = re.compile(r"\b(" + _wdf + r")\b|\b(" + _wda + r")(?:\.|(?=\s+\d))")
PLACE_PAREN_RE = re.compile(r"\(([^()]{2,40})\)\s*$")
PLACE_KEY_RE = re.compile(r"(?:^|[,;|\-\u2013\u2014@]\s*)((?:salle|room|hall|scene|stage|amphi|lieu|location|stand|espace)\b.{0,30})$")
PLACE_START_RE = re.compile(r"^(?:salle|room|hall|scene|stage|amphi|lieu|location|stand|espace)\b")

class Ev:
    def __init__(self, title, start, dur=0, place=''):
        self.title, self.start, self.dur, self.place = title, start, dur, place
    def __repr__(self):
        return "%s | %s | %dmin | %s" % (self.start.strftime('%Y-%m-%d %H:%M'), self.title, self.dur, self.place)

def _mk(y, m, d):
    try: return dt.date(y, m, d)
    except ValueError: return None

def _resolve(today, d, m, y):
    if y is None:
        c = _mk(today.year, m, d)
        if c is None: return None
        if (today - c).days > 60:
            c = _mk(today.year + 1, m, d)
        return c
    if y < 100: y += 2000
    return _mk(y, m, d)

def _next_weekday(today, wd):
    return today + dt.timedelta(days=(wd - today.weekday()) % 7)

def find_date(f, today):
    """Returns (date, start, end) of the first date found in the folded line, or None. Ranges are masked later."""
    m = DATE_TXT_RE.search(f)
    if m:
        d = _resolve(today, int(m.group(1)), MONTHS[m.group(2)], int(m.group(3)) if m.group(3) else None)
        if d: return d, m.start(), m.end()
    m = DATE_EN_RE.search(f)
    if m:
        d = _resolve(today, int(m.group(2)), MONTHS[m.group(1)], int(m.group(3)) if m.group(3) else None)
        if d: return d, m.start(), m.end()
    m = DATE_NUM_RE.search(f)
    if m:
        a, b = int(m.group(1)), int(m.group(2))
        if 1 <= a <= 31 and 1 <= b <= 12:
            d = _resolve(today, a, b, int(m.group(3)) if m.group(3) else None)
            if d: return d, m.start(), m.end()
    m = WD_RE.search(f)
    if m:
        w = m.group(1) or m.group(2)
        wd = WEEKDAYS.get(w, WD_ABBR.get(w))
        return _next_weekday(today, wd), m.start(), m.end()
    return None

def find_times(f):
    res = []
    for m in TIME_RE.finditer(f):
        h = int(m.group(1)); mi = int(m.group(2) or m.group(3) or 0)
        ap = m.group(4)
        if ap:
            if h < 1 or h > 12: continue
            if ap == 'p' and h < 12: h += 12
            if ap == 'a' and h == 12: h = 0
        if h > 23 or mi > 59: continue
        res.append((h, mi, m.start(), m.end()))
    return res

def clean_title(s):
    s = re.sub(r"\s+", " ", s)
    s = s.strip(" \t.,;:|-\u2013\u2014\u2022\u00b7*>_\"'[]()/\\")
    return s

def letters(s):
    return sum(1 for c in s if c.isalpha())

def parse(lines, today, max_events=40):
    n = len(lines)
    fl = [fold(l) for l in lines]
    times = [find_times(f) for f in fl]
    # Pass 1: line kinds
    header = [False] * n
    hdate = [None] * n
    for i in range(n):
        if times[i] or len(lines[i]) > 60: continue
        r = find_date(fl[i], today)
        if r: header[i] = True; hdate[i] = r[0]
    def usable(j):
        if j < 0 or j >= n: return False
        if times[j] or header[j] or letters(lines[j]) < 2: return False
        if PLACE_START_RE.match(fl[j].strip()): return False
        return True
    # layout of "time alone on its line": title after (default) or before
    nxt = prv = 0
    for i in range(n):
        if not times[i]: continue
        rest = _strip_ranges(lines[i], fl[i], times[i], find_date(fl[i], today))
        if letters(rest) >= 2: continue
        if usable(i + 1): nxt += 1
        if usable(i - 1): prv += 1
    above = prv > nxt
    used = [False] * n
    ctx = None
    out = []
    for i in range(n):
        if header[i]: ctx = hdate[i]; continue
        if not times[i]: continue
        ts = times[i]
        h, mi, s0, e0 = ts[0]
        dur = 0
        masks = [(s0, e0)]
        if len(ts) > 1:
            h2, mi2, s1, e1 = ts[1]
            if SEP_RE.match(fl[i][e0:s1]):
                d = (h2 * 60 + mi2) - (h * 60 + mi)
                if d <= 0: d += 1440
                dur = d
                masks.append((s1, e1)); masks.append((e0, s1))
        dd = find_date(fl[i], today)
        if dd: masks.append((dd[1], dd[2]))
        for mw in WD_RE.finditer(fl[i]): masks.append((mw.start(), mw.end()))
        day = dd[0] if dd else (ctx if ctx else today)
        txt = list(lines[i])
        for a, b in masks:
            for k in range(a, min(b, len(txt))): txt[k] = ' '
        rest = ''.join(txt)
        place = ''
        mp = PLACE_PAREN_RE.search(rest)
        if mp: place = clean_title(mp.group(1)); rest = rest[:mp.start()]
        else:
            fr = fold(rest)
            mk = PLACE_KEY_RE.search(fr)
            if mk: place = clean_title(rest[mk.start(1):]); rest = rest[:mk.start(1)]
        title = clean_title(rest)
        if letters(title) < 2:
            title = ''
            j = i - 1 if above else i + 1
            if usable(j) and not used[j]:
                title = clean_title(lines[j]); used[j] = True
                # place on the line after the title line
                k = j + 1 if not above else i + 1
                if not place and 0 <= k < n and not times[k] and not header[k] and PLACE_START_RE.match(fl[k].strip()):
                    place = clean_title(lines[k]); used[k] = True
        if not place:
            k = i + 1
            if 0 <= k < n and not times[k] and not header[k] and PLACE_START_RE.match(fl[k].strip()) and not used[k]:
                place = clean_title(lines[k]); used[k] = True
        if letters(title) < 2: title = ''
        start = dt.datetime(day.year, day.month, day.day, h, mi)
        out.append(Ev(title[:80], start, dur, place[:60]))
    # dedupe + sort
    seen = set(); res = []
    for e in sorted(out, key=lambda e: e.start):
        k = (e.start, fold(e.title))
        if k in seen: continue
        seen.add(k); res.append(e)
    return res[:max_events]

def _strip_ranges(line, f, ts, dd):
    txt = list(line)
    for t in ts[:2]:
        for k in range(t[2], min(t[3], len(txt))): txt[k] = ' '
    if dd:
        for k in range(dd[1], min(dd[2], len(txt))): txt[k] = ' '
    for mw in WD_RE.finditer(f):
        for k in range(mw.start(), min(mw.end(), len(txt))): txt[k] = ' '
    return ''.join(txt)

# ---------------------------------------------------------------- tests
def T(lines, today, expect):
    got = parse(lines, today)
    ok = len(got) == len(expect)
    if ok:
        for g, e in zip(got, expect):
            if (g.start.strftime('%Y-%m-%d %H:%M'), g.title, g.dur, g.place) != e: ok = False
    print(('OK  ' if ok else 'FAIL'), '; '.join(repr(g) for g in got) if ok else '\n   got   %s\n   want  %s' % (got, expect))
    return ok

if __name__ == '__main__':
    today = dt.date(2026, 11, 5)   # jeudi
    r = []
    r.append(T(["Samedi 7 novembre", "10h00 - 11h00 Ouverture des portes", "14h30 Concours de costumes (Salle A)", "Dimanche 8 novembre", "11h Photo de groupe"], today,
        [("2026-11-07 10:00","Ouverture des portes",60,""),("2026-11-07 14:30","Concours de costumes",0,"Salle A"),("2026-11-08 11:00","Photo de groupe",0,"")]))
    r.append(T(["SAMEDI", "10:00 - 11:30  Panel fursuit", "Salle B", "15:00  Danse", "DIMANCHE", "9h30 Petit-dejeuner"], today,
        [("2026-11-07 10:00","Panel fursuit",90,"Salle B"),("2026-11-07 15:00","Danse",0,""),("2026-11-08 09:30","Petit-dejeuner",0,"")]))
    r.append(T(["CONCERT DE MINUIT", "Vendredi 6 nov a 22h30", "ATELIER MAQUILLAGE", "Sam. 7/11 - 14 h 15"], today,
        [("2026-11-06 22:30","CONCERT DE MINUIT",0,""),("2026-11-07 14:15","ATELIER MAQUILLAGE",0,"")]))
    r.append(T(["Programme", "14h00", "Defile", "16h00", "Remise des prix", "18h30", "Cloture"], today,
        [("2026-11-05 14:00","Defile",0,""),("2026-11-05 16:00","Remise des prix",0,""),("2026-11-05 18:30","Cloture",0,"")]))
    r.append(T(["Friday November 6", "2:30 PM Dealer's den opens", "11 am - 1 pm Fursuit parade", "12:00 AM Midnight snack"], today,
        [("2026-11-06 00:00","Midnight snack",0,""),("2026-11-06 11:00","Fursuit parade",120,""),("2026-11-06 14:30","Dealer's den opens",0,"")]))
    r.append(T(["Entree 10 hommes max", "Prix 12.50 euros", "Tel 06 12 34 56 78"], today, []))
    r.append(T(["Nuit blanche", "23h - 1h Soiree"], today, [("2026-11-05 23:00","Soiree",120,"")]))
    r.append(T(["12 decembre 2026", "20h Reveillon"], today, [("2026-12-12 20:00","Reveillon",0,"")]))
    r.append(T(["2 janvier", "18h Galette"], today, [("2027-01-02 18:00","Galette",0,"")]))
    r.append(T([], today, []))
    print('ALL OK' if all(r) else 'SOME FAILED')
    raise SystemExit(0 if all(r) else 1)
