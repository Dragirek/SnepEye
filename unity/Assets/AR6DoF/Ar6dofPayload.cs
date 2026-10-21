using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Fabrique les octets envoyés à l'appareil Bluetooth à partir d'un "modèle". Un modèle est une suite de mots séparés par des espaces :
    ///   - un octet fixe en hexadécimal : 7E, 00, FF (ou plusieurs collés : 7E00)
    ///   - {v} un octet = valeur arrondie (0..255)        {v16} deux octets (petit-boutiste)   {v16be} deux octets (gros-boutiste)
    ///   - {f32} nombre flottant 32 bits                  {vtext} la valeur écrite en texte ASCII
    ///   - {r} {g} {b} un octet de couleur, {rgb} les trois, {hex} la couleur en texte "RRGGBB"
    ///   - {text} le texte du contrôle (UTF-8)
    /// Exemple pour une bande LED : "7E 07 05 03 {r} {g} {b} 10 EF".
    /// </summary>
    public static class Ar6dofPayload
    {
        public static readonly string[] PresetNames = { "1 octet", "2 octets (LE)", "2 octets (BE)", "Flottant 32 bits", "RGB (3 octets)", "RGB en texte", "Valeur en texte", "Texte" };
        public static readonly string[] PresetTemplates = { "{v}", "{v16}", "{v16be}", "{f32}", "{r} {g} {b}", "{hex}", "{vtext}", "{text}" };

        public static string DefaultTemplate(string type)
        {
            switch (type)
            {
                case "color":
                case "rgbcode": return "{r} {g} {b}";
                case "text": return "{text}";
                default: return "{v}";
            }
        }

        public static bool Compose(string template, float v, byte r, byte g, byte b, string text, out byte[] data)
        {
            var o = new List<byte>();
            data = null;
            foreach (string tok0 in (template ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = tok0.ToLowerInvariant();
                if (tok.StartsWith("{"))
                {
                    switch (tok)
                    {
                        case "{v}": o.Add((byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255)); break;
                        case "{v16}": { int u = Mathf.Clamp(Mathf.RoundToInt(v), 0, 65535); o.Add((byte)(u & 255)); o.Add((byte)(u >> 8)); break; }
                        case "{v16be}": { int u = Mathf.Clamp(Mathf.RoundToInt(v), 0, 65535); o.Add((byte)(u >> 8)); o.Add((byte)(u & 255)); break; }
                        case "{f32}": { byte[] f = BitConverter.GetBytes(v); if (!BitConverter.IsLittleEndian) Array.Reverse(f); o.AddRange(f); break; }
                        case "{vtext}": o.AddRange(Encoding.ASCII.GetBytes(v.ToString("0.##", CultureInfo.InvariantCulture))); break;
                        case "{text}": o.AddRange(Encoding.UTF8.GetBytes(text ?? "")); break;
                        case "{r}": o.Add(r); break;
                        case "{g}": o.Add(g); break;
                        case "{b}": o.Add(b); break;
                        case "{rgb}": o.Add(r); o.Add(g); o.Add(b); break;
                        case "{hex}": o.AddRange(Encoding.ASCII.GetBytes(r.ToString("X2") + g.ToString("X2") + b.ToString("X2"))); break;
                        default: return false;
                    }
                }
                else
                {
                    if (tok.Length % 2 != 0) return false;
                    for (int i = 0; i < tok.Length; i += 2)
                    {
                        byte bt;
                        if (!byte.TryParse(tok.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bt)) return false;
                        o.Add(bt);
                    }
                }
            }
            if (o.Count == 0) return false;
            data = o.ToArray();
            return true;
        }

        /// <summary>"#FF8800" ou "FF8800" -> couleur.</summary>
        public static bool ParseRgb(string s, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().TrimStart('#');
            if (s.Length != 6) return false;
            return byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                && byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                && byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        public static string ToHex(byte[] d)
        {
            var sb = new StringBuilder();
            foreach (byte x in d) { if (sb.Length > 0) sb.Append(' '); sb.Append(x.ToString("X2")); }
            return sb.ToString();
        }
    }
}
