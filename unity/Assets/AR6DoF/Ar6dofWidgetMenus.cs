using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Menus personnalisés (curseurs, interrupteurs, boutons) décrits dans le fichier ar6dof_widgets.json, à côté de l'exécutable
    /// (à la racine du projet dans l'éditeur). Chaque menu devient une icône de la barre de tâches. Un exemple est créé au premier lancement.
    /// Chaque changement est envoyé en texte UDP à 127.0.0.1:9880 sous la forme "menu/controle=valeur" (une ligne, valeur 0/1 pour un
    /// interrupteur) et par l'évènement Ar6dofWidgets.Changed : un programme (ou un script Unity) le relaie vers ton appareil Bluetooth.
    /// </summary>
    public static class Ar6dofWidgets
    {
        public const int UdpPort = 9880;
        public static event Action<string, string, float> Changed;

        [Serializable] public class WControl { public string type = "toggle", id = "", label = ""; public float min = 0f, max = 1f, value = 0f; }
        [Serializable] public class WMenu { public string id = "", title = "", icon = ""; public List<WControl> controls = new List<WControl>(); }
        [Serializable] public class WFile { public List<WMenu> menus = new List<WMenu>(); }

        static UdpClient udp;

        public static string FilePath()
        {
            string dir;
            try { dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } catch (Exception) { dir = Application.persistentDataPath; }
            return Path.Combine(dir, "ar6dof_widgets.json");
        }

        public static List<WMenu> Load()
        {
            string path = FilePath();
            try
            {
                if (!File.Exists(path))
                {
                    var ex = new WFile();
                    var m = new WMenu { id = "suit", title = "Fursuit", icon = "FUR" };
                    m.controls.Add(new WControl { type = "toggle", id = "eyes", label = "Yeux LED", value = 1f });
                    m.controls.Add(new WControl { type = "slider", id = "bright", label = "Luminosite", min = 0f, max = 255f, value = 128f });
                    m.controls.Add(new WControl { type = "button", id = "wave", label = "Animation" });
                    ex.menus.Add(m);
                    File.WriteAllText(path, JsonUtility.ToJson(ex, true), new UTF8Encoding(false));
                    Debug.Log("AR6DoF : fichier de menus personnalises cree : " + path);
                }
                var f = JsonUtility.FromJson<WFile>(File.ReadAllText(path, Encoding.UTF8));
                return f != null && f.menus != null ? f.menus : new List<WMenu>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("AR6DoF : menus personnalises illisibles (" + path + ") : " + e.Message);
                return new List<WMenu>();
            }
        }

        public static void Send(string menu, string control, float value)
        {
            try { if (Changed != null) Changed(menu, control, value); } catch (Exception e) { Debug.LogException(e); }
            try
            {
                if (udp == null) udp = new UdpClient();
                byte[] b = Encoding.UTF8.GetBytes(menu + "/" + control + "=" + value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "\n");
                udp.Send(b, b.Length, "127.0.0.1", UdpPort);
            }
            catch (Exception) { }
        }

        /// <summary>Construit le Canvas d'un menu.</summary>
        public static Canvas Build(WMenu menu)
        {
            const float W = 520f;
            float h = 84f;
            foreach (var c in menu.controls) h += c.type == "slider" ? 92f : 62f;
            h += 20f;
            var canvas = Ar6dofVrUi.NewCanvas("Menu " + menu.title, new Vector2(W, h));
            float y = h * 0.5f - 42f;
            Ar6dofVrUi.Label(canvas.transform, menu.title, new Vector2(0f, y), new Vector2(W - 40f, 50f), 32);
            y -= 70f;
            foreach (var c in menu.controls)
            {
                var ctl = c;
                string mid = menu.id;
                if (ctl.type == "slider")
                {
                    Text lbl = Ar6dofVrUi.Label(canvas.transform, "", new Vector2(0f, y + 20f), new Vector2(W - 60f, 34f), 24, TextAnchor.MiddleLeft);
                    Action<float> setLbl = v => lbl.text = ctl.label + " : " + v.ToString("0.##");
                    setLbl(ctl.value);
                    Ar6dofVrUi.MakeSlider(canvas.transform, new Vector2(0f, y - 20f), new Vector2(W - 60f, 44f), ctl.min, ctl.max, ctl.value,
                        v => { ctl.value = v; setLbl(v); Send(mid, ctl.id, v); });
                    y -= 92f;
                }
                else if (ctl.type == "button")
                {
                    Text t;
                    Ar6dofVrUi.MakeButton(canvas.transform, ctl.label, new Vector2(0f, y), new Vector2(W - 60f, 50f), () => Send(mid, ctl.id, 1f), out t);
                    y -= 62f;
                }
                else   // toggle
                {
                    Text t = null;
                    Action refresh = () => t.text = (ctl.value > 0.5f ? "[x]  " : "[  ]  ") + ctl.label;
                    Ar6dofVrUi.MakeButton(canvas.transform, "", new Vector2(0f, y), new Vector2(W - 60f, 50f), () => { ctl.value = ctl.value > 0.5f ? 0f : 1f; refresh(); Send(mid, ctl.id, ctl.value); }, out t);
                    refresh();
                    y -= 62f;
                }
            }
            return canvas;
        }
    }
}
