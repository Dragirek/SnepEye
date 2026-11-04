using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Outils pour n'agir que sur CERTAINS champs d'un objet de réglages (ceux d'une page des réglages VR) :
    /// copier ces champs d'un objet à l'autre (Enregistrer / Annuler / Par defaut d'une seule page) et savoir s'ils diffèrent.
    /// Les champs sont désignés par leur nom ; les objets sérialisables et les listes sont copiés en profondeur.
    /// </summary>
    public static class Ar6dofScope
    {
        static FieldInfo Field(Type t, string name)
        {
            return t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        }

        static object CloneValue(object v)
        {
            if (v == null) return null;
            Type t = v.GetType();
            if (t.IsValueType || t == typeof(string)) return v;
            var list = v as IList;
            if (list != null)
            {
                var nl = (IList)Activator.CreateInstance(t);
                foreach (var it in list) nl.Add(CloneValue(it));
                return nl;
            }
            return JsonUtility.FromJson(JsonUtility.ToJson(v), t);
        }

        /// <summary>Copie les champs 'fields' de src vers dst (copie profonde). src et dst ont le même type.</summary>
        public static void Copy(object src, object dst, string[] fields)
        {
            if (src == null || dst == null || fields == null) return;
            Type t = dst.GetType();
            foreach (string name in fields)
            {
                var fi = Field(t, name);
                if (fi == null) continue;
                fi.SetValue(dst, CloneValue(fi.GetValue(src)));
            }
        }

        static void Append(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            Type t = v.GetType();
            if (t == typeof(string)) { sb.Append('"').Append((string)v).Append('"'); return; }
            if (t.IsPrimitive || t.IsEnum) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            var list = v as IList;
            if (list != null)
            {
                sb.Append('[');
                foreach (var it in list) { Append(sb, it); sb.Append(','); }
                sb.Append(']');
                return;
            }
            sb.Append(JsonUtility.ToJson(v));
        }

        /// <summary>Empreinte (texte) des champs 'fields' : deux objets ont les mêmes valeurs pour ces champs si les empreintes sont égales.</summary>
        public static string Sig(object o, string[] fields)
        {
            if (o == null || fields == null) return "";
            Type t = o.GetType();
            var sb = new StringBuilder();
            foreach (string name in fields)
            {
                var fi = Field(t, name);
                if (fi == null) continue;
                sb.Append(name).Append('=');
                Append(sb, fi.GetValue(o));
                sb.Append(';');
            }
            return sb.ToString();
        }
    }
}
