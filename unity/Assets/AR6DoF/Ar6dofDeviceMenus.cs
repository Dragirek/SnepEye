using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    [Serializable]
    public class ControlDef
    {
        public string type = "toggle";   // toggle, slider, button, color, rgbcode, text, choice, label
        public string label = "";
        public float min = 0f, max = 255f, value = 0f;
        public string options = "Mode 1,Mode 2,Mode 3";   // choice : valeurs séparées par des virgules (la valeur envoyée = l'indice)
        public string text = "";                          // text : contenu ; rgbcode : "#RRGGBB"
        public float hue, sat, val = 1f;                  // color : roue + luminosité
        public string template = "";                      // vide = modèle par défaut du type (voir Ar6dofPayload)
        public string service = "", chr = "";             // vide = ceux du menu
        public bool withResponse;
        [NonSerialized] public float lastSend;
        [NonSerialized] public bool pending;
    }

    [Serializable]
    public class DeviceMenuDef
    {
        public string customName = "";    // nom choisi par la personne (prioritaire)
        public string deviceName = "";    // nom Bluetooth annoncé par l'appareil
        public string address = "";       // 12 chiffres hexa
        public string icon = "";
        public string service = "", chr = "";
        public bool withResponse;
        public bool open;
        public List<ControlDef> controls = new List<ControlDef>();
    }

    [Serializable] class DeviceFile { public List<DeviceMenuDef> menus = new List<DeviceMenuDef>(); }

    /// <summary>
    /// Menus liés à un appareil Bluetooth (fursuit, bande LED...). Chaque menu est une icône de la barre de tâches ; il affiche l'état de la
    /// connexion, le nom de l'appareil (ou le nom choisi) et des contrôles personnalisables : interrupteur, curseur, bouton, roue de couleur,
    /// code RGB, texte, choix, étiquette. L'icône « roue » du menu ouvre ses paramètres : appareil, envoi, contrôles (ajout, doublon, ordre,
    /// modèle d'envoi). Tout est enregistré dans ar6dof_devices.json, à côté de l'exécutable.
    /// </summary>
    public class Ar6dofDeviceMenus : MonoBehaviour
    {
        public static event Action<string, string, float> Changed;   // (nom du menu, contrôle, valeur) : pour tes propres scripts
        public static void RaiseChanged(string menu, string control, float v) { if (Changed != null) { try { Changed(menu, control, v); } catch (Exception e) { Debug.LogException(e); } } }

        public Ar6dofDock dock;
        public readonly List<DeviceMenu> menus = new List<DeviceMenu>();
        bool dirty;
        float saveAt;

        public static string FilePath()
        {
            string dir;
            try { dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } catch (Exception) { dir = Application.persistentDataPath; }
            return Path.Combine(dir, "ar6dof_devices.json");
        }

        public void Load()
        {
            try
            {
                string p = FilePath();
                if (File.Exists(p))
                {
                    var f = JsonUtility.FromJson<DeviceFile>(File.ReadAllText(p, Encoding.UTF8));
                    if (f != null && f.menus != null) foreach (var d in f.menus) AddMenu(d);
                }
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : menus d'appareils illisibles : " + e.Message); }
        }

        DeviceMenu AddMenu(DeviceMenuDef d)
        {
            var m = new DeviceMenu(this, d);
            menus.Add(m);
            if (dock != null) dock.Adopt(m);
            if (!string.IsNullOrEmpty(d.address) && Ar6dofBle.Instance != null) Ar6dofBle.Instance.Register(d.address);
            return m;
        }

        public DeviceMenu Create()
        {
            var d = new DeviceMenuDef { customName = "", deviceName = "" };
            var m = AddMenu(d);
            m.StartInSettings();
            MarkDirty();
            return m;
        }

        public void Remove(DeviceMenu m)
        {
            if (!string.IsNullOrEmpty(m.def.address))
            {
                bool shared = false;
                foreach (var o in menus) if (o != m && Ar6dofBle.Normalize(o.def.address) == Ar6dofBle.Normalize(m.def.address)) shared = true;
                if (!shared && Ar6dofBle.Instance != null) Ar6dofBle.Instance.Unregister(m.def.address);
            }
            menus.Remove(m);
            m.Destroy();
            MarkDirty();
            if (dock != null) dock.RefreshIcons();
        }

        public void MarkDirty() { dirty = true; saveAt = Time.unscaledTime + 1f; }

        void Update()
        {
            foreach (var m in menus) m.Tick();
            if (dirty && Time.unscaledTime > saveAt) { dirty = false; Save(); }
        }

        void OnApplicationQuit() { if (dirty) Save(); }

        public void Save()
        {
            try
            {
                var f = new DeviceFile();
                foreach (var m in menus) f.menus.Add(m.def);
                File.WriteAllText(FilePath(), JsonUtility.ToJson(f, true), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : sauvegarde des menus d'appareils impossible : " + e.Message); }
        }
    }

    /// <summary>Un menu d'appareil : une "application" de la barre de tâches avec son propre Canvas dans la pièce.</summary>
    public class DeviceMenu : Ar6dofDockApp
    {
        enum Mode { Use, Settings, Controls, Pick, Edit }

        public DeviceMenuDef def;
        public Canvas canvas;
        public Action<Transform> place;
        readonly Ar6dofDeviceMenus owner;
        RectTransform content;
        Mode mode = Mode.Use;
        ControlDef editing;
        bool placed, showUnnamed, confirmDelete;
        float y;
        const float W = 600f, Gap = 8f;
        Text statusText;
        Image statusDot;
        string lastStatus = "";
        int pickSig = -1;
        float pickRefresh;
        Text previewText, titleText;

        public DeviceMenu(Ar6dofDeviceMenus o, DeviceMenuDef d)
        {
            owner = o;
            def = d;
            open = d.open;
            canvas = Ar6dofVrUi.NewCanvas("Menu appareil", new Vector2(W, 300f));
            ((RectTransform)canvas.transform).pivot = new Vector2(0.5f, 1f);   // le haut reste en place quand la hauteur change
            canvas.gameObject.SetActive(false);
            Rebuild();
            Refresh();
        }

        // ------------------------------------------------------------------ Ar6dofDockApp
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(def.customName)) return def.customName;
                if (!string.IsNullOrEmpty(def.deviceName)) return def.deviceName;
                return string.IsNullOrEmpty(def.address) ? "Nouvel appareil" : "Appareil " + def.address.Substring(Math.Max(0, def.address.Length - 4));
            }
        }

        void Refresh()
        {
            title = DisplayName;
            if (!string.IsNullOrEmpty(def.icon)) icon = def.icon;
            else { string t = DisplayName.Replace(" ", ""); icon = t.Length > 3 ? t.Substring(0, 3).ToUpperInvariant() : t.ToUpperInvariant(); }
        }

        public override Transform AnimRoot { get { return canvas != null ? canvas.transform : null; } }
        public override bool HasStatus { get { return true; } }
        public override Color StatusColor { get { return StatusColorOf(); } }
        public override string TooltipText() { return DisplayName + " - " + StatusString(); }

        public override void Activate(bool on)
        {
            if (canvas == null) return;
            canvas.gameObject.SetActive(on);
            def.open = on;
            owner.MarkDirty();
            if (!on) LeavePick();
            else Rebuild();
        }

        public override void BeforeFirstShow() { if (!placed && place != null && canvas != null) { place(canvas.transform); placed = true; } }

        public void StartInSettings() { mode = Mode.Settings; Rebuild(); }

        public void Destroy() { if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject); }

        // ------------------------------------------------------------------ état
        string StatusString()
        {
            if (string.IsNullOrEmpty(def.address)) return "Aucun appareil choisi";
            var ble = Ar6dofBle.Instance;
            if (ble == null) return "Bluetooth indisponible";
            switch (ble.State(def.address))
            {
                case "connected": return "Connecte";
                case "connecting": return "Connexion en cours...";
                case "noprobe": return "ar6dof_probe absent";
                default: return "Deconnecte";
            }
        }

        Color StatusColorOf()
        {
            if (string.IsNullOrEmpty(def.address)) return new Color(0.6f, 0.6f, 0.65f, 1f);
            var ble = Ar6dofBle.Instance;
            string s = ble != null ? ble.State(def.address) : "x";
            if (s == "connected") return new Color(0.3f, 0.9f, 0.4f, 1f);
            if (s == "connecting") return new Color(1f, 0.7f, 0.2f, 1f);
            return new Color(1f, 0.3f, 0.3f, 1f);
        }

        /// <summary>Appelé à chaque image par le gestionnaire.</summary>
        public void Tick()
        {
            if (canvas == null || !canvas.gameObject.activeSelf) { FlushPending(); return; }
            string st = StatusString();
            if (st != lastStatus && statusText != null) { lastStatus = st; statusText.text = st; }
            if (statusDot != null) statusDot.color = StatusColorOf();
            FlushPending();
            if (mode == Mode.Pick && Time.unscaledTime > pickRefresh)
            {
                pickRefresh = Time.unscaledTime + 1f;
                int sig = PickSignature();
                if (sig != pickSig) Rebuild();
            }
        }

        void FlushPending()
        {
            foreach (var c in def.controls)
                if (c.pending && Time.unscaledTime - c.lastSend >= 0.05f) Send(c, true);
        }

        // ------------------------------------------------------------------ envoi
        public bool BuildPayload(ControlDef c, out byte[] data)
        {
            byte r = 0, g = 0, b = 0;
            if (c.type == "color") { Color col = Color.HSVToRGB(Mathf.Clamp01(c.hue), Mathf.Clamp01(c.sat), Mathf.Clamp01(c.val)); r = (byte)Mathf.RoundToInt(col.r * 255f); g = (byte)Mathf.RoundToInt(col.g * 255f); b = (byte)Mathf.RoundToInt(col.b * 255f); }
            else if (c.type == "rgbcode") Ar6dofPayload.ParseRgb(c.text, out r, out g, out b);
            string tpl = string.IsNullOrEmpty(c.template) ? Ar6dofPayload.DefaultTemplate(c.type) : c.template;
            return Ar6dofPayload.Compose(tpl, c.value, r, g, b, c.text, out data);
        }

        void Send(ControlDef c, bool latestWins)
        {
            c.pending = false;
            c.lastSend = Time.unscaledTime;
            byte[] data;
            if (c.type == "label" || !BuildPayload(c, out data)) return;
            string svc = !string.IsNullOrEmpty(c.service) ? c.service : def.service;
            string chr = !string.IsNullOrEmpty(c.chr) ? c.chr : def.chr;
            if (Ar6dofBle.Instance != null) Ar6dofBle.Instance.Write(def.address, svc, chr, data, c.withResponse || def.withResponse, latestWins);
            Ar6dofDeviceMenus.RaiseChanged(DisplayName, c.label, c.value);
        }

        // envoi "continu" (curseur, roue) : au plus 20 par seconde, le dernier état est toujours envoyé
        void SendContinuous(ControlDef c)
        {
            owner.MarkDirty();
            if (Time.unscaledTime - c.lastSend >= 0.05f) Send(c, true);
            else c.pending = true;
        }

        // ------------------------------------------------------------------ construction de la vue
        void Rebuild()
        {
            if (canvas == null) return;
            var cur = Ar6dofVrTextField.Current;
            if (cur != null && cur.transform.IsChildOf(canvas.transform)) cur.Blur();   // valide la saisie en cours avant de reconstruire
            if (content != null) UnityEngine.Object.Destroy(content.gameObject);
            statusText = null; statusDot = null; previewText = null; titleText = null;
            content = Ar6dofVrUi.NewContent(canvas);
            y = 14f;
            Header();
            switch (mode)
            {
                case Mode.Use: UseView(); break;
                case Mode.Settings: SettingsView(); break;
                case Mode.Controls: ControlsView(); break;
                case Mode.Pick: PickView(); break;
                case Mode.Edit: EditView(); break;
            }
            Ar6dofVrUi.FitHeight(canvas, y + 10f);
            pickSig = PickSignature();
            lastStatus = "";
            Tick();
        }

        float RowY(float h) { float cy = -(y + h * 0.5f); y += h + Gap; return cy; }

        Text Lbl(string text, float h = 34f, int fs = 24, TextAnchor a = TextAnchor.MiddleLeft, float x = 0f, float w = W - 60f)
        {
            return Ar6dofVrUi.Label(content, text, new Vector2(x, RowY(h)), new Vector2(w, h), fs, a);
        }

        Button Btn(string text, Action click, float h = 50f, int fs = 24)
        {
            Text t;
            return Ar6dofVrUi.MakeButton(content, text, new Vector2(0f, RowY(h)), new Vector2(W - 60f, h), () => click(), out t, fs);
        }

        // plusieurs boutons sur une même ligne
        void BtnRow(float h, int fs, params object[] items)   // paires (texte, Action)
        {
            float cy = RowY(h);
            int n = items.Length / 2;
            float total = W - 60f, w = (total - (n - 1) * 8f) / n;
            for (int i = 0; i < n; i++)
            {
                string t = (string)items[i * 2];
                Action a = (Action)items[i * 2 + 1];
                Text lab;
                Ar6dofVrUi.MakeButton(content, t, new Vector2(-total * 0.5f + w * 0.5f + i * (w + 8f), cy), new Vector2(w, h), () => a(), out lab, fs);
            }
        }

        Ar6dofVrTextField Field(string value, string placeholder, Action<string> submit, Action<string> changed = null, float h = 46f)
        {
            return Ar6dofVrTextField.Make(content, new Vector2(0f, RowY(h)), new Vector2(W - 60f, h), value, placeholder, changed, submit);
        }

        void Header()
        {
            // ligne d'état (au-dessus du nom) + roue des paramètres
            float cy = RowY(40f);
            var dot = Ar6dofVrUi.Rect(content, "Pastille", new Vector2(-W * 0.5f + 40f, cy), new Vector2(22f, 22f));
            statusDot = dot.gameObject.AddComponent<Image>();
            statusDot.raycastTarget = false;
            statusText = Ar6dofVrUi.Label(content, "", new Vector2(-40f, cy), new Vector2(380f, 36f), 22, TextAnchor.MiddleLeft);
            Text g;
            var gearBtn = Ar6dofVrUi.MakeButton(content, "", new Vector2(W * 0.5f - 52f, cy), new Vector2(64f, 42f), () => { confirmDelete = false; mode = mode == Mode.Use ? Mode.Settings : Mode.Use; LeavePick(); Rebuild(); }, out g, 20);
            var gi = Ar6dofVrUi.Rect(gearBtn.transform, "Roue", Vector2.zero, new Vector2(32f, 32f)).gameObject.AddComponent<Image>();
            gi.sprite = Ar6dofVrUi.GearSprite();
            gi.raycastTarget = false;
            var tip = gearBtn.gameObject.AddComponent<Ar6dofVrTooltip>();
            tip.text = () => mode == Mode.Use ? "Parametres du menu" : "Retour au menu";
            tip.offsetY = 50f;
            titleText = Lbl(DisplayName, 46f, 34, TextAnchor.MiddleCenter);
            if (mode != Mode.Use) Lbl(mode == Mode.Settings ? "Parametres" : mode == Mode.Controls ? "Controles" : mode == Mode.Pick ? "Choisir l'appareil" : "Modifier le controle", 26f, 20, TextAnchor.MiddleCenter).color = Ar6dofVrUi.Accent;
            y += 4f;
        }

        // ------------------------------------------------------------------ vue d'usage
        void UseView()
        {
            if (def.controls.Count == 0) Lbl("Menu vide : ouvre les parametres (roue) pour ajouter des controles.", 60f, 22, TextAnchor.MiddleCenter);
            foreach (var cc in def.controls)
            {
                var c = cc;
                switch (c.type)
                {
                    case "toggle":
                        {
                            Text t = null;
                            Action upd = () => t.text = (c.value > 0.5f ? "[x]  " : "[  ]  ") + c.label;
                            Ar6dofVrUi.MakeButton(content, "", new Vector2(0f, RowY(52f)), new Vector2(W - 60f, 52f), () => { c.value = c.value > 0.5f ? 0f : 1f; upd(); Send(c, false); owner.MarkDirty(); }, out t, 24);
                            upd();
                            break;
                        }
                    case "slider":
                        {
                            Text lbl = Lbl("", 30f, 22);
                            Action<float> upd = v => lbl.text = c.label + " : " + v.ToString("0.##", CultureInfo.InvariantCulture);
                            upd(c.value);
                            Ar6dofVrUi.MakeSlider(content, new Vector2(0f, RowY(44f)), new Vector2(W - 60f, 44f), c.min, Mathf.Max(c.max, c.min + 0.001f), c.value, v => { c.value = v; upd(v); SendContinuous(c); });
                            break;
                        }
                    case "button":
                        {
                            Text t;
                            Ar6dofVrUi.MakeButton(content, c.label, new Vector2(0f, RowY(52f)), new Vector2(W - 60f, 52f), () => { c.value = 1f; Send(c, false); }, out t, 24);
                            break;
                        }
                    case "color": ColorControl(c); break;
                    case "rgbcode": RgbControl(c); break;
                    case "text": TextControl(c); break;
                    case "choice":
                        {
                            string[] opts = c.options.Split(',');
                            Text t = null;
                            Action upd = () => { int i = Mathf.Clamp(Mathf.RoundToInt(c.value), 0, opts.Length - 1); t.text = c.label + " : " + opts[i].Trim(); };
                            Ar6dofVrUi.MakeButton(content, "", new Vector2(0f, RowY(52f)), new Vector2(W - 60f, 52f), () => { c.value = (Mathf.RoundToInt(c.value) + 1) % opts.Length; upd(); Send(c, false); owner.MarkDirty(); }, out t, 24);
                            upd();
                            break;
                        }
                    case "label": Lbl(c.label, 36f, 24, TextAnchor.MiddleCenter); break;
                }
            }
            y += 6f;
        }

        void ColorControl(ControlDef c)
        {
            Lbl(c.label, 30f, 22);
            Text hexLbl = null;
            Action refreshHex = () =>
            {
                Color col = Color.HSVToRGB(Mathf.Clamp01(c.hue), Mathf.Clamp01(c.sat), Mathf.Clamp01(c.val));
                hexLbl.text = "#" + ((byte)Mathf.RoundToInt(col.r * 255f)).ToString("X2") + ((byte)Mathf.RoundToInt(col.g * 255f)).ToString("X2") + ((byte)Mathf.RoundToInt(col.b * 255f)).ToString("X2");
                hexLbl.color = col;
            };
            float cy = RowY(230f);
            Ar6dofVrColorWheel.Make(content, new Vector2(0f, cy), 220f, c.hue, c.sat, (h, s) => { c.hue = h; c.sat = s; refreshHex(); SendContinuous(c); });
            hexLbl = Lbl("", 34f, 28, TextAnchor.MiddleCenter);
            Ar6dofVrUi.MakeSlider(content, new Vector2(0f, RowY(44f)), new Vector2(W - 60f, 44f), 0f, 1f, c.val, v => { c.val = v; refreshHex(); SendContinuous(c); });
            Lbl("luminosite", 24f, 18, TextAnchor.MiddleCenter);
            refreshHex();
        }

        void RgbControl(ControlDef c)
        {
            Lbl(c.label, 30f, 22);
            float cy = RowY(50f);
            Image sw = null;
            Action<string> preview = s => { byte r, g, b; if (Ar6dofPayload.ParseRgb(s, out r, out g, out b)) sw.color = new Color32(r, g, b, 255); };
            var f = Ar6dofVrTextField.Make(content, new Vector2(-70f, cy), new Vector2(W - 60f - 150f, 50f), string.IsNullOrEmpty(c.text) ? "#FFFFFF" : c.text, "#RRGGBB", preview,
                s => { byte r, g, b; if (Ar6dofPayload.ParseRgb(s, out r, out g, out b)) { c.text = "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2"); Send(c, false); owner.MarkDirty(); } });
            f.maxLength = 7;
            var swRt = Ar6dofVrUi.Rect(content, "Apercu", new Vector2(W * 0.5f - 30f - 55f, cy), new Vector2(110f, 50f));
            sw = swRt.gameObject.AddComponent<Image>();
            sw.raycastTarget = false;
            preview(f.text);
        }

        void TextControl(ControlDef c)
        {
            Lbl(c.label, 30f, 22);
            float cy = RowY(50f);
            float fw = W - 60f - 160f;
            var f = Ar6dofVrTextField.Make(content, new Vector2(-W * 0.5f + 30f + fw * 0.5f, cy), new Vector2(fw, 50f), c.text, "texte a envoyer", s => c.text = s, s => { c.text = s; owner.MarkDirty(); });
            Text t;
            Ar6dofVrUi.MakeButton(content, "Envoyer", new Vector2(W * 0.5f - 30f - 75f, cy), new Vector2(150f, 50f), () => { c.text = f.text; Send(c, false); owner.MarkDirty(); }, out t, 22);
        }

        // ------------------------------------------------------------------ paramètres
        void SettingsView()
        {
            // Appareil
            Lbl("Appareil", 30f, 22).color = Ar6dofVrUi.Accent;
            Lbl(string.IsNullOrEmpty(def.address) ? "aucun" : (string.IsNullOrEmpty(def.deviceName) ? "(sans nom)" : def.deviceName) + "  -  " + def.address, 30f, 20);
            BtnRow(50f, 22, "Changer d'appareil", (Action)(() => { mode = Mode.Pick; Rebuild(); }), "Oublier", (Action)(() => ForgetDevice()));
            Lbl("Nom affiche (vide = nom de l'appareil)", 28f, 18);
            Field(def.customName, def.deviceName.Length > 0 ? def.deviceName : "nom du menu", s => { def.customName = s.Trim(); NameChanged(); });
            Lbl("Icone dans la barre (3 lettres)", 28f, 18);
            Field(def.icon, "auto", s => { def.icon = s.Trim(); if (def.icon.Length > 3) def.icon = def.icon.Substring(0, 3); NameChanged(); });

            // Envoi par défaut
            y += 6f;
            Lbl("Envoi par defaut", 30f, 22).color = Ar6dofVrUi.Accent;
            Lbl("Service Bluetooth (UUID, ex. FFE0)", 28f, 18);
            Field(def.service, "UUID du service", s => { def.service = s.Trim(); owner.MarkDirty(); });
            Lbl("Caracteristique (UUID, ex. FFE1)", 28f, 18);
            Field(def.chr, "UUID de la caracteristique", s => { def.chr = s.Trim(); owner.MarkDirty(); });
            Text wr = null;
            Action updWr = () => wr.text = (def.withResponse ? "[x]  " : "[  ]  ") + "Ecriture avec reponse (plus fiable, plus lent)";
            Ar6dofVrUi.MakeButton(content, "", new Vector2(0f, RowY(46f)), new Vector2(W - 60f, 46f), () => { def.withResponse = !def.withResponse; updWr(); owner.MarkDirty(); }, out wr, 20);
            updWr();

            // Contrôles : page à part
            y += 6f;
            Btn("Controles (" + def.controls.Count + ")  >", () => { mode = Mode.Controls; Rebuild(); }, 50f, 24);
            y += 4f;
            BtnRow(50f, 22, confirmDelete ? "Confirmer la suppression" : "Supprimer ce menu", (Action)(() => { if (confirmDelete) owner.Remove(this); else { confirmDelete = true; Rebuild(); } }), "Terminer", (Action)(() => { confirmDelete = false; mode = Mode.Use; Rebuild(); }));
        }

        void ControlsView()
        {
            if (def.controls.Count == 0) Lbl("aucun pour l'instant", 28f, 18);
            for (int i = 0; i < def.controls.Count; i++)
            {
                int idx = i;
                var c = def.controls[i];
                float cy = RowY(46f);
                float x0 = -W * 0.5f + 30f;
                Ar6dofVrUi.Label(content, TypeName(c.type) + " : " + (c.label.Length > 16 ? c.label.Substring(0, 16) + ".." : c.label), new Vector2(x0 + 110f, cy), new Vector2(220f, 46f), 18, TextAnchor.MiddleLeft);
                string[] names = { "Haut", "Bas", "Edit", "Dup", "Suppr" };
                Action[] acts = {
                    () => { if (idx > 0) { var t = def.controls[idx]; def.controls[idx] = def.controls[idx - 1]; def.controls[idx - 1] = t; Changed2(); } },
                    () => { if (idx < def.controls.Count - 1) { var t = def.controls[idx]; def.controls[idx] = def.controls[idx + 1]; def.controls[idx + 1] = t; Changed2(); } },
                    () => { editing = def.controls[idx]; mode = Mode.Edit; Rebuild(); },
                    () => { def.controls.Insert(idx + 1, JsonUtility.FromJson<ControlDef>(JsonUtility.ToJson(def.controls[idx]))); Changed2(); },
                    () => { def.controls.RemoveAt(idx); Changed2(); } };
                for (int k = 0; k < 5; k++)
                {
                    Action a = acts[k];
                    Text lab;
                    Ar6dofVrUi.MakeButton(content, names[k], new Vector2(x0 + 255f + 56f + k * 62f - 15f, cy), new Vector2(58f, 46f), () => a(), out lab, 15);
                }
            }
            y += 4f;
            Lbl("Ajouter un controle", 28f, 18).color = Ar6dofVrUi.Accent;
            string[] types = { "toggle", "slider", "button", "color", "rgbcode", "text", "choice", "label" };
            for (int i = 0; i < types.Length; i += 2)
            {
                string a = types[i], b = types[i + 1];
                BtnRow(46f, 20, "+ " + TypeName(a), (Action)(() => AddControl(a)), "+ " + TypeName(b), (Action)(() => AddControl(b)));
            }
            y += 6f;
            Btn("Retour", () => { mode = Mode.Settings; Rebuild(); }, 50f, 22);
        }

        // changement de nom / d'icône : met à jour la barre sans reconstruire la vue (sinon le champ cliqué juste après serait détruit)
        void NameChanged()
        {
            owner.MarkDirty();
            Refresh();
            if (titleText != null) titleText.text = DisplayName;
            if (owner.dock != null) owner.dock.RefreshIcons();
        }

        void Changed2() { owner.MarkDirty(); Refresh(); if (owner.dock != null) owner.dock.RefreshIcons(); Rebuild(); }

        static string TypeName(string t)
        {
            switch (t)
            {
                case "toggle": return "Interrupteur";
                case "slider": return "Curseur";
                case "button": return "Bouton";
                case "color": return "Roue de couleur";
                case "rgbcode": return "Code RGB";
                case "text": return "Texte";
                case "choice": return "Choix";
                default: return "Etiquette";
            }
        }

        void AddControl(string type)
        {
            var c = new ControlDef { type = type, label = TypeName(type) };
            if (type == "slider") { c.min = 0f; c.max = 255f; c.value = 128f; }
            if (type == "rgbcode") c.text = "#FFFFFF";
            if (type == "choice") c.options = "Mode 1,Mode 2,Mode 3";
            def.controls.Add(c);
            owner.MarkDirty();
            editing = c;
            mode = Mode.Edit;
            Rebuild();
        }

        void ForgetDevice()
        {
            string old = def.address;
            def.address = ""; def.deviceName = "";
            if (!string.IsNullOrEmpty(old) && Ar6dofBle.Instance != null)
            {
                bool shared = false;
                foreach (var m in owner.menus) if (m != this && Ar6dofBle.Normalize(m.def.address) == Ar6dofBle.Normalize(old)) shared = true;
                if (!shared) Ar6dofBle.Instance.Unregister(old);
            }
            Changed2();
        }

        // ------------------------------------------------------------------ choix de l'appareil
        int PickSignature()
        {
            var ble = Ar6dofBle.Instance;
            if (ble == null) return 0;
            int h = ble.devices.Count;
            foreach (var kv in ble.devices) h = h * 31 + kv.Key.GetHashCode() + kv.Value.name.GetHashCode();
            return h * 7 + (showUnnamed ? 1 : 0);
        }

        void LeavePick() { if (Ar6dofBle.Instance != null && Ar6dofBle.Instance.Scanning) Ar6dofBle.Instance.SetScan(false); }

        void PickView()
        {
            var ble = Ar6dofBle.Instance;
            if (ble == null) { Lbl("Bluetooth indisponible dans cette scene (Ar6dofBle).", 40f, 22, TextAnchor.MiddleCenter); Btn("Retour", () => { mode = Mode.Settings; Rebuild(); }); return; }
            if (!ble.Scanning) ble.SetScan(true);
            Lbl(ble.ProbeAlive ? "Appareils Bluetooth a portee (allume le tien) :" : "ar6dof_probe ne repond pas : lance-le (sans --no-ble).", 34f, 20, TextAnchor.MiddleLeft);
            var list = new List<Ar6dofBle.Dev>(ble.devices.Values);
            list.Sort((a, b) => b.rssi.CompareTo(a.rssi));
            int shown = 0;
            foreach (var d0 in list)
            {
                var d = d0;
                if (!showUnnamed && string.IsNullOrEmpty(d.name)) continue;
                if (shown++ >= 9) break;
                string nm = string.IsNullOrEmpty(d.name) ? "(sans nom) " + d.addr : d.name;
                Btn(nm + "   " + d.rssi + " dBm", () => ChooseDevice(d), 46f, 20);
            }
            if (shown == 0) Lbl("recherche en cours...", 40f, 22, TextAnchor.MiddleCenter);
            y += 4f;
            BtnRow(46f, 20, showUnnamed ? "Masquer les sans nom" : "Afficher les sans nom", (Action)(() => { showUnnamed = !showUnnamed; Rebuild(); }), "Retour", (Action)(() => { LeavePick(); mode = Mode.Settings; Rebuild(); }));
        }

        void ChooseDevice(Ar6dofBle.Dev d)
        {
            string old = def.address;
            def.address = d.addr;
            def.deviceName = d.name;
            var ble = Ar6dofBle.Instance;
            if (ble != null)
            {
                ble.Register(d.addr);
                if (!string.IsNullOrEmpty(old) && Ar6dofBle.Normalize(old) != Ar6dofBle.Normalize(d.addr))
                {
                    bool shared = false;
                    foreach (var m in owner.menus) if (m != this && Ar6dofBle.Normalize(m.def.address) == Ar6dofBle.Normalize(old)) shared = true;
                    if (!shared) ble.Unregister(old);
                }
            }
            LeavePick();
            mode = Mode.Settings;
            Changed2();
        }

        // ------------------------------------------------------------------ modifier un contrôle
        void EditView()
        {
            var c = editing;
            if (c == null) { mode = Mode.Controls; Rebuild(); return; }
            Lbl(TypeName(c.type), 30f, 22).color = Ar6dofVrUi.Accent;
            Lbl("Texte affiche", 26f, 18);
            Field(c.label, "nom du controle", s => { c.label = s; owner.MarkDirty(); });
            if (c.type == "slider")
            {
                Lbl("Minimum et maximum", 26f, 18);
                float cy = RowY(46f);
                float w2 = (W - 60f - 8f) * 0.5f;
                Ar6dofVrTextField.Make(content, new Vector2(-w2 * 0.5f - 4f, cy), new Vector2(w2, 46f), c.min.ToString("0.##", CultureInfo.InvariantCulture), "min", null, s => { float v; if (TryNum(s, out v)) { c.min = v; c.value = Mathf.Clamp(c.value, c.min, c.max); owner.MarkDirty(); } });
                Ar6dofVrTextField.Make(content, new Vector2(w2 * 0.5f + 4f, cy), new Vector2(w2, 46f), c.max.ToString("0.##", CultureInfo.InvariantCulture), "max", null, s => { float v; if (TryNum(s, out v)) { c.max = Mathf.Max(v, c.min + 0.001f); c.value = Mathf.Clamp(c.value, c.min, c.max); owner.MarkDirty(); } });
            }
            if (c.type == "choice")
            {
                Lbl("Choix (separes par des virgules) : l'indice 0, 1, 2... est envoye", 26f, 16);
                Field(c.options, "Mode 1,Mode 2", s => { c.options = s; owner.MarkDirty(); });
            }
            if (c.type != "label")
            {
                y += 4f;
                Lbl("Comment l'information est envoyee", 30f, 22).color = Ar6dofVrUi.Accent;
                string tpl = string.IsNullOrEmpty(c.template) ? Ar6dofPayload.DefaultTemplate(c.type) : c.template;
                Btn("Modele pret : " + PresetOf(tpl) + "  (cliquer pour changer)", () =>
                {
                    int i = Array.IndexOf(Ar6dofPayload.PresetTemplates, tpl);
                    c.template = Ar6dofPayload.PresetTemplates[(i + 1) % Ar6dofPayload.PresetTemplates.Length];
                    owner.MarkDirty(); Rebuild();
                }, 46f, 18);
                Lbl("Modele (octets hexa fixes et {v} {v16} {f32} {r} {g} {b} {rgb} {hex} {text} {vtext})", 26f, 15);
                Field(tpl, "ex. 7E 07 {r} {g} {b} EF", s => { c.template = s.Trim(); owner.MarkDirty(); UpdatePreview(c); }, s => { c.template = s.Trim(); UpdatePreview(c); });
                previewText = Lbl("", 28f, 18);
                UpdatePreview(c);
                Lbl("Cible : vide = celle du menu", 26f, 16);
                Field(c.service, "service (UUID)", s => { c.service = s.Trim(); owner.MarkDirty(); });
                Field(c.chr, "caracteristique (UUID)", s => { c.chr = s.Trim(); owner.MarkDirty(); });
                Text wr = null;
                Action upd = () => wr.text = (c.withResponse ? "[x]  " : "[  ]  ") + "Ecriture avec reponse (ce controle)";
                Ar6dofVrUi.MakeButton(content, "", new Vector2(0f, RowY(44f)), new Vector2(W - 60f, 44f), () => { c.withResponse = !c.withResponse; upd(); owner.MarkDirty(); }, out wr, 18);
                upd();
                Btn("Tester l'envoi maintenant", () => Send(c, false), 46f, 20);
            }
            y += 6f;
            BtnRow(50f, 22, "Dupliquer", (Action)(() => { int i = def.controls.IndexOf(c); def.controls.Insert(i + 1, JsonUtility.FromJson<ControlDef>(JsonUtility.ToJson(c))); owner.MarkDirty(); mode = Mode.Controls; Rebuild(); }), "Terminer", (Action)(() => { editing = null; mode = Mode.Controls; Rebuild(); }));
        }

        static bool TryNum(string s, out float v) { return float.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v); }

        static string PresetOf(string tpl)
        {
            int i = Array.IndexOf(Ar6dofPayload.PresetTemplates, tpl);
            return i >= 0 ? Ar6dofPayload.PresetNames[i] : "personnalise";
        }

        void UpdatePreview(ControlDef c)
        {
            if (previewText == null) return;
            byte[] d;
            previewText.text = BuildPayload(c, out d) ? "Octets pour la valeur actuelle : " + Ar6dofPayload.ToHex(d) : "Modele invalide";
        }
    }
}
