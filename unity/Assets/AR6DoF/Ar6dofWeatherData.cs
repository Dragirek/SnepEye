using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AR6DoF
{
    // ------------------------------------------------------------------ réponses JSON (JsonUtility : les noms sont ceux de l'API Open-Meteo)
    [Serializable] public class WxCurrent
    {
        public string time = "";
        public float temperature_2m, relative_humidity_2m, apparent_temperature, precipitation, cloud_cover, surface_pressure, wind_speed_10m, wind_direction_10m, wind_gusts_10m;
        public int is_day = 1, weather_code;
    }
    [Serializable] public class WxHourly
    {
        public string[] time = new string[0];
        public float[] temperature_2m = new float[0], precipitation_probability = new float[0], precipitation = new float[0];
        // heure par heure complet (curseur « avancer dans le temps » de la fenetre Meteo)
        public float[] apparent_temperature = new float[0], relative_humidity_2m = new float[0], cloud_cover = new float[0], surface_pressure = new float[0],
                       wind_speed_10m = new float[0], wind_direction_10m = new float[0], wind_gusts_10m = new float[0];
        public int[] weather_code = new int[0], is_day = new int[0];
    }
    [Serializable] public class WxDaily
    {
        public string[] time = new string[0], sunrise = new string[0], sunset = new string[0];
        public int[] weather_code = new int[0];
        public float[] temperature_2m_max = new float[0], temperature_2m_min = new float[0], precipitation_probability_max = new float[0],
                       precipitation_sum = new float[0], uv_index_max = new float[0], wind_speed_10m_max = new float[0];
    }
    [Serializable] public class WxResponse
    {
        public double latitude, longitude;
        public WxCurrent current = new WxCurrent();
        public WxHourly hourly = new WxHourly();
        public WxDaily daily = new WxDaily();
    }

    // source : d'ou vient la position (affichee dans la fenetre) : "windows", "ip", "manual", "search"
    [Serializable] public class WxPlace { public string name = "", country = "", countryCode = "", source = ""; public double lat, lon; public bool valid; }
    // units : -1 selon le pays, 0 métrique, 1 impérial. off : masque de bits des elements caches (Ar6dofWeatherApp.Opt*)
    [Serializable] public class WxPrefs { public WxPlace place = new WxPlace(); public int units = -1; public int off; }

    [Serializable] class GeoItem { public string name = "", country = "", admin1 = "", country_code = ""; public double latitude, longitude; }
    [Serializable] class GeoResp { public GeoItem[] results; }
    [Serializable] class IpWho { public bool success; public double latitude, longitude; public string city = "", country = "", country_code = ""; }
    [Serializable] class IpApi { public double latitude, longitude; public string city = "", country_name = "", country_code = ""; }
    [Serializable] class RevGeo { public string city = "", locality = "", countryName = "", countryCode = ""; }

    // ------------------------------------------------------------------ conditions
    public enum WxCond { Clear, MostlyClear, PartlyCloudy, Overcast, Fog, Drizzle, Rain, HeavyRain, Showers, FreezingRain, Snow, HeavySnow, SnowShowers, Thunder, Hail }

    public static class WxCodes
    {
        /// <summary>Code météo WMO (celui d'Open-Meteo) -> condition.</summary>
        public static WxCond FromCode(int c)
        {
            switch (c)
            {
                case 0: return WxCond.Clear;
                case 1: return WxCond.MostlyClear;
                case 2: return WxCond.PartlyCloudy;
                case 3: return WxCond.Overcast;
                case 45: case 48: return WxCond.Fog;
                case 51: case 53: case 55: return WxCond.Drizzle;
                case 56: case 57: case 66: case 67: return WxCond.FreezingRain;
                case 61: case 63: return WxCond.Rain;
                case 65: return WxCond.HeavyRain;
                case 80: case 81: case 82: return WxCond.Showers;
                case 71: case 73: case 77: return WxCond.Snow;
                case 75: return WxCond.HeavySnow;
                case 85: case 86: return WxCond.SnowShowers;
                case 95: return WxCond.Thunder;
                case 96: case 99: return WxCond.Hail;
                default: return WxCond.PartlyCloudy;
            }
        }

        /// <summary>Texte français (clé de traduction) de la condition.</summary>
        public static string Name(WxCond c, bool day)
        {
            switch (c)
            {
                case WxCond.Clear: return day ? "Ciel degage" : "Nuit claire";
                case WxCond.MostlyClear: return "Plutot degage";
                case WxCond.PartlyCloudy: return "Partiellement nuageux";
                case WxCond.Overcast: return "Couvert";
                case WxCond.Fog: return "Brouillard";
                case WxCond.Drizzle: return "Bruine";
                case WxCond.Rain: return "Pluie";
                case WxCond.HeavyRain: return "Forte pluie";
                case WxCond.Showers: return "Averses";
                case WxCond.FreezingRain: return "Pluie verglacante";
                case WxCond.Snow: return "Neige";
                case WxCond.HeavySnow: return "Forte neige";
                case WxCond.SnowShowers: return "Averses de neige";
                case WxCond.Thunder: return "Orage";
                default: return "Orage et grele";
            }
        }

        public static bool IsSnow(WxCond c) { return c == WxCond.Snow || c == WxCond.HeavySnow || c == WxCond.SnowShowers; }
        public static bool IsRain(WxCond c) { return c == WxCond.Drizzle || c == WxCond.Rain || c == WxCond.HeavyRain || c == WxCond.Showers || c == WxCond.FreezingRain || c == WxCond.Thunder || c == WxCond.Hail; }

        static readonly Dictionary<string, string[]> compass = new Dictionary<string, string[]>
        {
            { "fr", new[] { "N", "NE", "E", "SE", "S", "SO", "O", "NO" } },
            { "en", new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" } },
            { "es", new[] { "N", "NE", "E", "SE", "S", "SO", "O", "NO" } },
            { "de", new[] { "N", "NO", "O", "SO", "S", "SW", "W", "NW" } },
            { "it", new[] { "N", "NE", "E", "SE", "S", "SO", "O", "NO" } },
            { "pt", new[] { "N", "NE", "L", "SE", "S", "SO", "O", "NO" } },
        };

        /// <summary>Direction d'où vient le vent (degrés, 0 = nord) -> « NO », « SW »... dans la langue choisie.</summary>
        public static string Compass(float deg)
        {
            string[] names;
            if (!compass.TryGetValue(Ar6dofLoc.Code, out names)) names = compass["en"];
            int i = Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 45f) % 8;
            return names[i];
        }

        /// <summary>Jour de la semaine abrégé (« lun. », « Mon ») d'une date « 2026-11-05 », dans la langue choisie.</summary>
        public static string DayName(string isoDate, bool today)
        {
            if (today) return Ar6dofLoc.T("Auj.");
            try
            {
                DateTime d = DateTime.ParseExact(isoDate.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                CultureInfo ci;
                try { ci = CultureInfo.GetCultureInfo(Ar6dofLoc.Code); } catch (Exception) { ci = CultureInfo.InvariantCulture; }
                string n = ci.DateTimeFormat.AbbreviatedDayNames[(int)d.DayOfWeek];
                return n.Length > 0 ? char.ToUpper(n[0], ci) + n.Substring(1) : n;
            }
            catch (Exception) { return isoDate; }
        }

        /// <summary>Heure « 07:32 » extraite d'une date ISO « 2026-11-05T07:32 ».</summary>
        public static string Clock(string iso) { return !string.IsNullOrEmpty(iso) && iso.Length >= 16 ? iso.Substring(11, 5) : "--:--"; }
    }

    /// <summary>
    /// Données météo et position, sans aucun coût tant que la fenêtre Météo est fermée : tout passe par des requêtes web (Open-Meteo, sans clé,
    /// valable dans tous les pays) lancées à la demande, sondées une fois par image seulement quand la fenêtre est ouverte (Poll).
    /// Position : « Ma position » demande à Windows (Wi-Fi / GPS, localisation à activer dans Paramètres Windows) puis retombe sur la position
    /// approximative d'après l'adresse IP ; ou recherche d'une ville par son nom.
    /// </summary>
    public class Ar6dofWeatherClient
    {
        public enum Phase { Idle, Locating, Searching, Fetching }

        public Phase phase = Phase.Idle;
        public readonly WxPrefs prefs = new WxPrefs();
        public WxResponse data;                      // dernière météo reçue (null tant qu'il n'y en a pas)
        public float fetchedAt = -9999f;             // Time.realtimeSinceStartup de la dernière réception
        public string error = "";                    // dernier problème (clé de traduction ou texte)
        public string status = "";                   // info (clé de traduction)
        public string hint = "";                     // remarque à garder affichée (ex. localisation Windows refusée)
        public readonly List<WxPlace> results = new List<WxPlace>();
        public bool searched;

        UnityWebRequest req;
        Action<string> onDone;
        Action<string> onFail;
        volatile string winResult;                   // résultat de la localisation Windows (fil secondaire)
        bool winPending;

        static string FilePath { get { return Path.Combine(Ar6dofSettings.FolderPath, "weather.json"); } }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public bool Busy { get { return phase != Phase.Idle; } }
        public bool HasData { get { return data != null && data.current != null && data.daily != null && data.daily.time != null && data.daily.time.Length > 0; } }
        public float AgeSeconds { get { return Time.realtimeSinceStartup - fetchedAt; } }

        // ------------------------------------------------------------------ unités
        static readonly string[] Fahrenheit = { "US", "BS", "BZ", "KY", "PW", "FM", "MH", "LR" };

        /// <summary>Vrai si les unités sont impériales (°F, mph, pouces) : choix de l'utilisateur, sinon selon le pays du lieu.</summary>
        public bool Imperial
        {
            get
            {
                if (prefs.units >= 0) return prefs.units == 1;
                string cc = prefs.place.countryCode;
                if (string.IsNullOrEmpty(cc))
                {
                    try { cc = RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch (Exception) { cc = ""; }
                }
                return Array.IndexOf(Fahrenheit, (cc ?? "").ToUpperInvariant()) >= 0;
            }
        }

        // ------------------------------------------------------------------ fichier
        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var p = JsonUtility.FromJson<WxPrefs>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (p != null && p.place != null) { prefs.place = p.place; prefs.units = p.units; prefs.off = p.off; }
                }
            }
            catch (Exception) { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Ar6dofSettings.FolderPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(prefs, true), Encoding.UTF8);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ requêtes
        void Get(string url, Action<string> done, Action<string> fail)
        {
            Abort();
            try
            {
                req = UnityWebRequest.Get(url);
                req.timeout = 12;
                req.SendWebRequest();
                onDone = done;
                onFail = fail;
            }
            catch (Exception e)
            {
                req = null;
                fail(e.Message);
            }
        }

        /// <summary>Fenêtre fermée : abandonne la requête en cours (la position Windows déjà demandée finira toute seule).</summary>
        public void Cancel()
        {
            Abort();
            phase = Phase.Idle;
            status = "";
        }

        public void Abort()
        {
            if (req != null)
            {
                try { req.Abort(); req.Dispose(); } catch (Exception) { }
                req = null;
            }
        }

        /// <summary>À appeler à chaque image tant que la fenêtre est ouverte.</summary>
        public void Poll()
        {
            if (winPending && winResult != null)
            {
                string r = winResult;
                winResult = null;
                winPending = false;
                OnWindowsLocation(r);
            }
            if (req == null || !req.isDone) return;
            var rq = req;
            req = null;
            var done = onDone;
            var fail = onFail;
            string text = null, err = null;
            if (rq.result == UnityWebRequest.Result.Success) text = rq.downloadHandler.text;
            else err = string.IsNullOrEmpty(rq.error) ? "reseau" : rq.error;
            try { rq.Dispose(); } catch (Exception) { }
            if (text != null) done(text); else fail(err);
        }

        static string N(double v) { return v.ToString("0.####", Inv); }

        // ------------------------------------------------------------------ météo
        /// <summary>Recharge la météo du lieu enregistré (ou cherche d'abord la position approximative s'il n'y en a pas).</summary>
        public void Refresh()
        {
            if (!prefs.place.valid) { UseIp(); return; }
            phase = Phase.Fetching;
            error = "";
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + N(prefs.place.lat) + "&longitude=" + N(prefs.place.lon) +
                "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation,weather_code,cloud_cover,surface_pressure,wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
                "&hourly=temperature_2m,precipitation_probability,precipitation,weather_code,apparent_temperature,relative_humidity_2m,cloud_cover,surface_pressure,wind_speed_10m,wind_direction_10m,wind_gusts_10m,is_day" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,sunrise,sunset,uv_index_max,wind_speed_10m_max" +
                "&timezone=auto&forecast_days=7&wind_speed_unit=kmh&temperature_unit=celsius&precipitation_unit=mm";
            Get(url, text =>
            {
                phase = Phase.Idle;
                try
                {
                    var d = JsonUtility.FromJson<WxResponse>(text);
                    if (d == null || d.current == null || d.daily == null || d.daily.time == null || d.daily.time.Length == 0) { error = "Reponse meteo illisible"; return; }
                    data = d;
                    fetchedAt = Time.realtimeSinceStartup;
                    error = "";
                }
                catch (Exception) { error = "Reponse meteo illisible"; }
            }, err => { phase = Phase.Idle; error = "Meteo indisponible (connexion ?)"; });
        }

        // ------------------------------------------------------------------ position
        void SetPlace(string name, string country, string cc, double lat, double lon, string source)
        {
            prefs.place = new WxPlace { name = name ?? "", country = country ?? "", countryCode = cc ?? "", source = source ?? "", lat = lat, lon = lon, valid = true };
            Save();
        }

        /// <summary>Position approximative d'après l'adresse IP (ville près de chez toi) : marche partout, sans autorisation.</summary>
        public void UseIp()
        {
            phase = Phase.Locating;
            error = "";
            status = "Recherche de ta position...";
            Get("https://ipwho.is/", text =>
            {
                try
                {
                    var r = JsonUtility.FromJson<IpWho>(text);
                    if (r != null && r.success && (r.latitude != 0 || r.longitude != 0)) { SetPlace(r.city, r.country, r.country_code, r.latitude, r.longitude, "ip"); status = ""; Refresh(); return; }
                }
                catch (Exception) { }
                IpFallback();
            }, err => IpFallback());
        }

        void IpFallback()
        {
            Get("https://ipapi.co/json/", text =>
            {
                try
                {
                    var r = JsonUtility.FromJson<IpApi>(text);
                    if (r != null && (r.latitude != 0 || r.longitude != 0)) { SetPlace(r.city, r.country_name, r.country_code, r.latitude, r.longitude, "ip"); status = ""; Refresh(); return; }
                }
                catch (Exception) { }
                phase = Phase.Idle; status = ""; error = "Position introuvable : cherche ta ville par son nom";
            }, err => { phase = Phase.Idle; status = ""; error = "Position introuvable : cherche ta ville par son nom"; });
        }

        /// <summary>Demande sa position à Windows (Wi-Fi / GPS) : un petit PowerShell, lancé seulement ici, sur un autre fil.</summary>
        public void UseWindowsLocation()
        {
            if (winPending) return;
            phase = Phase.Locating;
            error = "";
            status = "Position demandee a Windows...";
            winResult = null;
            hint = "";
            winPending = true;
            Task.Run(() => { winResult = WinLocate(); });
        }

        static string WinLocate()
        {
            try
            {
                const string script =
                    "Add-Type -AssemblyName System.Device; " +
                    "$w = New-Object System.Device.Location.GeoCoordinateWatcher([System.Device.Location.GeoPositionAccuracy]::Default); " +
                    "[void]$w.TryStart($false, [TimeSpan]::FromSeconds(8)); " +
                    "$l = $w.Position.Location; " +
                    "if ($l.IsUnknown) { 'unknown' } else { [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0};{1}', $l.Latitude, $l.Longitude) }";
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } return "timeout"; }
                    return p.StandardOutput.ReadToEnd().Trim();
                }
            }
            catch (Exception) { return "error"; }
        }

        void OnWindowsLocation(string r)
        {
            var parts = (r ?? "").Split(';');
            double lat, lon;
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, Inv, out lat) && double.TryParse(parts[1], NumberStyles.Float, Inv, out lon))
            {
                status = "Position trouvee";
                ResolveAndUse(lat, lon, "windows");
                return;
            }
            // Windows n'a pas pu : localisation coupée dans Paramètres Windows, pas de Wi-Fi/GPS... on prend la position approximative
            UseIp();
            hint = "Localisation Windows indisponible (Parametres Windows > Confidentialite > Localisation) : position approximative utilisee";
        }

        /// <summary>Cherche le nom du lieu de ces coordonnées (géocodage inverse ; en cas d'échec on garde les chiffres), l'enregistre et charge la météo.</summary>
        void ResolveAndUse(double lat, double lon, string source)
        {
            phase = Phase.Locating;
            string lang = Ar6dofLoc.Code;
            Get("https://api.bigdatacloud.net/data/reverse-geocode-client?latitude=" + N(lat) + "&longitude=" + N(lon) + "&localityLanguage=" + lang, text =>
            {
                string name = "", country = "", cc = "";
                try
                {
                    var g = JsonUtility.FromJson<RevGeo>(text);
                    if (g != null) { name = !string.IsNullOrEmpty(g.city) ? g.city : g.locality; country = g.countryName; cc = g.countryCode; }
                }
                catch (Exception) { }
                if (string.IsNullOrEmpty(name)) name = lat.ToString("0.00", Inv) + ", " + lon.ToString("0.00", Inv);
                SetPlace(name, country, cc, lat, lon, source);
                status = "";
                Refresh();
            }, err =>
            {
                SetPlace(lat.ToString("0.00", Inv) + ", " + lon.ToString("0.00", Inv), "", "", lat, lon, source);
                status = "";
                Refresh();
            });
        }

        /// <summary>Position saisie à la main (latitude, longitude en degrés décimaux) : la plus fiable sur un PC fixe, qui n'a pas de GPS.</summary>
        public void UseCoords(double lat, double lon)
        {
            error = "";
            hint = "";
            status = "Recherche de ta position...";
            ResolveAndUse(lat, lon, "manual");
        }

        /// <summary>Lit « 47.39, 0.68 », « 47,39 0,68 », « 47.39;0.68 » (copié depuis une carte...). Vrai si latitude et longitude sont valides.</summary>
        public static bool ParseCoords(string text, out double lat, out double lon)
        {
            lat = lon = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim().Replace("°", " ");
            string[] p = t.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2) p = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2)
            {
                string[] q = t.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (q.Length == 2) p = q;
            }
            if (p.Length != 2) return false;
            string a = p[0].Trim().TrimEnd(',').Replace(',', '.'), b = p[1].Trim().TrimEnd(',').Replace(',', '.');
            if (!double.TryParse(a, NumberStyles.Float, Inv, out lat) || !double.TryParse(b, NumberStyles.Float, Inv, out lon)) return false;
            return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
        }

        // ------------------------------------------------------------------ recherche d'une ville
        public void Search(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2) return;
            phase = Phase.Searching;
            error = "";
            searched = false;
            results.Clear();
            Get("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(query) + "&count=6&format=json&language=" + Ar6dofLoc.Code, text =>
            {
                phase = Phase.Idle;
                searched = true;
                try
                {
                    var g = JsonUtility.FromJson<GeoResp>(text);
                    if (g != null && g.results != null)
                        foreach (var it in g.results)
                        {
                            string n = it.name;
                            if (!string.IsNullOrEmpty(it.admin1) && it.admin1 != it.name) n += ", " + it.admin1;
                            results.Add(new WxPlace { name = n, country = it.country, countryCode = it.country_code, lat = it.latitude, lon = it.longitude, valid = true });
                        }
                }
                catch (Exception) { }
            }, err => { phase = Phase.Idle; searched = true; error = "Recherche impossible (connexion ?)"; });
        }

        public void Pick(WxPlace p)
        {
            if (p == null) return;
            prefs.place = new WxPlace { name = p.name, country = p.country, countryCode = p.countryCode, source = "search", lat = p.lat, lon = p.lon, valid = true };
            Save();
            results.Clear();
            searched = false;
            hint = "";
            Refresh();
        }
    }
}
