using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Traduction de l'interface. Les textes du code sont en français (c'est la clé) : T("Reglages") renvoie la traduction dans la langue choisie,
    /// ou le texte français s'il n'y en a pas. Langues fournies : fr, en, es, de, it, pt (voir Ar6dofLangData).
    /// Pour ajouter ou corriger une langue sans toucher au code : fichier %APPDATA%\AR6DoF\lang\xx.txt, une ligne par texte :
    ///     texte français => traduction        (première ligne facultative : "# Nom de la langue")
    /// </summary>
    public static class Ar6dofLoc
    {
        public static event Action Changed;

        static readonly string[] BuiltinCodes = { "fr", "en", "es", "de", "it", "pt" };
        static readonly string[] BuiltinNames = { "Français", "English", "Español", "Deutsch", "Italiano", "Português" };

        static string code = "fr";
        static readonly Dictionary<string, string> map = new Dictionary<string, string>();
        static bool inited;

        public static string Code { get { Init(); return code; } }
        public static string LangFolder { get { return Path.Combine(Ar6dofSettings.FolderPath, "lang"); } }

        static void Init()
        {
            if (inited) return;
            inited = true;
            Ar6dofSettings.Changed += OnSettings;
            Apply(Resolve(Ar6dofSettings.Current.language), false);
        }

        static void OnSettings()
        {
            string want = Resolve(Ar6dofSettings.Current.language);
            if (want != code) Apply(want, true);
        }

        static string Resolve(string language)
        {
            if (!string.IsNullOrEmpty(language)) return language;
            switch (Application.systemLanguage)
            {
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Portuguese: return "pt";
                default: return "en";
            }
        }

        static void Apply(string c, bool notify)
        {
            code = c;
            map.Clear();
            if (c != "fr")
            {
                Parse(Ar6dofLangData.Get(c));
                try
                {
                    string f = Path.Combine(LangFolder, c + ".txt");
                    if (File.Exists(f)) Parse(File.ReadAllText(f));
                }
                catch (Exception) { }
            }
            if (notify)
            {
                var h = Changed;
                if (h != null) h();
            }
        }

        static void Parse(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            foreach (string raw in data.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int i = line.IndexOf(" => ", StringComparison.Ordinal);
                if (i <= 0) continue;
                string src = line.Substring(0, i).Replace("\\n", "\n");
                string dst = line.Substring(i + 4).Replace("\\n", "\n");
                if (dst.Length > 0) map[src] = dst;
            }
        }

        /// <summary>Traduit un texte français de l'interface.</summary>
        public static string T(string fr)
        {
            Init();
            if (code == "fr" || string.IsNullOrEmpty(fr)) return fr;
            string v;
            return map.TryGetValue(fr, out v) ? v : fr;
        }

        /// <summary>Traduit puis remplace {0}, {1}... (pour les textes avec des nombres).</summary>
        public static string F(string fr, params object[] args)
        {
            try { return string.Format(T(fr), args); }
            catch (FormatException) { return fr; }
        }

        /// <summary>Choisit la langue ("" = automatique). Aperçu : enregistré seulement avec Ar6dofSettings.Save().</summary>
        public static void SetLanguage(string language)
        {
            Init();
            Ar6dofSettings.Current.language = language ?? "";
            Ar6dofSettings.Notify();
        }

        /// <summary>Langues disponibles : (code, nom affiché). Les fichiers du dossier lang s'ajoutent à celles fournies.</summary>
        public static List<KeyValuePair<string, string>> Languages()
        {
            var l = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < BuiltinCodes.Length; i++) l.Add(new KeyValuePair<string, string>(BuiltinCodes[i], BuiltinNames[i]));
            try
            {
                if (Directory.Exists(LangFolder))
                    foreach (string f in Directory.GetFiles(LangFolder, "*.txt"))
                    {
                        string c = Path.GetFileNameWithoutExtension(f);
                        bool known = false;
                        foreach (var k in l) if (k.Key == c) known = true;
                        if (known) continue;
                        string name = c;
                        using (var r = new StreamReader(f))
                        {
                            string first = r.ReadLine();
                            if (first != null && first.StartsWith("#")) name = first.TrimStart('#', ' ');
                        }
                        l.Add(new KeyValuePair<string, string>(c, name));
                    }
            }
            catch (Exception) { }
            return l;
        }
    }

    /// <summary>Retraduit un Text quand la langue change (sauf si le code a changé son texte entre-temps : c'est alors un texte dynamique).</summary>
    public class Ar6dofLocText : MonoBehaviour
    {
        public string source = "";
        Text t;
        string shown;

        public static void Attach(Text text, string source)
        {
            if (text == null) return;
            var l = text.GetComponent<Ar6dofLocText>();
            if (l == null) l = text.gameObject.AddComponent<Ar6dofLocText>();
            l.source = source ?? "";
            l.shown = null;
            l.Refresh();
        }

        void OnEnable()
        {
            Ar6dofLoc.Changed += Refresh;
            Refresh();
        }

        void OnDisable() { Ar6dofLoc.Changed -= Refresh; }

        public void Refresh()
        {
            if (t == null) t = GetComponent<Text>();
            if (t == null) return;
            if (shown != null && t.text != shown) return;
            string s = Ar6dofLoc.T(source);
            t.text = s;
            shown = s;
        }
    }
}
