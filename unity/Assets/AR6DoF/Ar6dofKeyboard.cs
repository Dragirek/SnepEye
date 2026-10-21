using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Clavier virtuel AZERTY / QWERTY visé à la télécommande. Les caractères sont envoyés à la fenêtre Windows active (la dernière
    /// cliquée) sans dépendre de la disposition du clavier de Windows. Maj / Ctrl / Alt restent enfoncées jusqu'à la touche suivante.
    /// K : afficher / cacher.  B : replacer le clavier sous ta vue.
    /// </summary>
    public class Ar6dofKeyboard : MonoBehaviour
    {
        public enum Kind { Char, Vk, Shift, Ctrl, Alt, Layout }

        public class KeyDef
        {
            public string label, shiftLabel;
            public char ch, shiftCh;
            public ushort vk;
            public float width = 1f;
            public Kind kind;
        }

        /// <summary>Si renseignés (champ de texte d'un menu VR en cours de saisie), les touches vont ici au lieu d'être envoyées à Windows.</summary>
        public static System.Action<char> CharSink;
        public static System.Action<ushort> VkSink;
        public Ar6dofPassthrough passthrough;
        public bool azerty = true;
        public float keySize = 0.045f, gap = 0.006f;
        [Tooltip("Inclinaison du clavier vers toi (degrés)")]
        public float tiltDegrees = 25f;
        [Tooltip("Multiplie la taille des lettres si elles sont trop petites ou trop grandes")]
        public float labelScale = 1f;
        public Shader keyShader;   // vide : AR6DoF/Flat, sinon Sprites/Default
        public KeyCode summonKey = KeyCode.B;

        bool shift, ctrl, alt, visible = true;
        Transform keysRoot;
        Font font;
        Shader shader;
        readonly List<Ar6dofKey> keys = new List<Ar6dofKey>();

        void Start()
        {
            font = Ar6dofVrUi.GetFont();
            shader = keyShader != null ? keyShader : Shader.Find("AR6DoF/Flat");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) { UnityEngine.Debug.LogError("AR6DoF : aucun shader pour le clavier virtuel : renseigne 'Key Shader' (Ar6dofFlat.shader)."); enabled = false; return; }
            if (passthrough == null) passthrough = FindFirstObjectByType<Ar6dofPassthrough>();
            // Placement : on réutilise la logique du menu flottant (fixe dans la pièce, rappelable)
            var fm = GetComponent<Ar6dofFloatingMenu>();
            if (fm == null) fm = gameObject.AddComponent<Ar6dofFloatingMenu>();
            fm.passthrough = passthrough;
            fm.distance = 0.75f;
            fm.heightOffset = -0.42f;
            fm.minHeightAboveFloor = 0.5f;
            fm.summonKey = summonKey;
            fm.followKey = KeyCode.F9;
            fm.showPanel = false;
            fm.useScreensAnchor = true;
            Build();
        }

        /// <summary>Le clavier s'affiche et se range depuis la barre de tâches (Ar6dofDock).</summary>
        public bool IsVisible { get { return visible; } }

        public void SetVisible(bool v)
        {
            visible = v;
            if (keysRoot != null) keysRoot.gameObject.SetActive(v);
        }

        // ------------------------------------------------------------------ disposition
        static List<KeyDef> CharRow(string normal, string shifted)
        {
            var row = new List<KeyDef>();
            for (int i = 0; i < normal.Length; i++)
                row.Add(new KeyDef { label = normal[i].ToString(), shiftLabel = shifted[i].ToString(), ch = normal[i], shiftCh = shifted[i], kind = Kind.Char });
            return row;
        }

        static KeyDef Vk(string label, ushort vk, float w = 1f) { return new KeyDef { label = label, vk = vk, width = w, kind = Kind.Vk }; }
        static KeyDef Mod(string label, Kind k, float w) { return new KeyDef { label = label, kind = k, width = w }; }

        List<List<KeyDef>> Layout()
        {
            var rows = new List<List<KeyDef>>();
            List<KeyDef> r;
            if (azerty)
            {
                r = CharRow("²&é\"'(-è_çà)=", "³1234567890°+"); r.Add(Vk("Retour", 0x08, 2f)); rows.Add(r);
                r = new List<KeyDef> { Vk("Tab", 0x09, 1.5f) }; r.AddRange(CharRow("azertyuiop^$", "AZERTYUIOP¨£")); rows.Add(r);
                r = CharRow("qsdfghjklmù*", "QSDFGHJKLM%µ"); r.Add(Vk("Entree", 0x0D, 2f)); rows.Add(r);
                r = new List<KeyDef> { Mod("Maj", Kind.Shift, 1.6f) }; r.AddRange(CharRow("<wxcvbn,;:!", ">WXCVBN?./§")); rows.Add(r);
            }
            else
            {
                r = CharRow("`1234567890-=", "~!@#$%^&*()_+"); r.Add(Vk("Back", 0x08, 2f)); rows.Add(r);
                r = new List<KeyDef> { Vk("Tab", 0x09, 1.5f) }; r.AddRange(CharRow("qwertyuiop[]\\", "QWERTYUIOP{}|")); rows.Add(r);
                r = CharRow("asdfghjkl;'", "ASDFGHJKL:\""); r.Add(Vk("Enter", 0x0D, 2.4f)); rows.Add(r);
                r = new List<KeyDef> { Mod("Shift", Kind.Shift, 2.2f) }; r.AddRange(CharRow("zxcvbnm,./", "ZXCVBNM<>?")); rows.Add(r);
            }
            rows.Add(new List<KeyDef>
            {
                Mod("Ctrl", Kind.Ctrl, 1.4f), Mod("Alt", Kind.Alt, 1.2f),
                new KeyDef { label = "Espace", ch = ' ', shiftCh = ' ', width = 5f, kind = Kind.Char },
                Vk("Echap", 0x1B, 1.3f), Vk("Suppr", 0x2E, 1.3f), Vk("<", 0x25), Vk("^", 0x26), Vk("v", 0x28), Vk(">", 0x27),
                Vk("PgPr", 0x21, 1.2f), Vk("PgSv", 0x22, 1.2f),
                Mod(azerty ? "QWERTY" : "AZERTY", Kind.Layout, 1.8f)
            });
            return rows;
        }

        void Build()
        {
            if (keysRoot != null) Destroy(keysRoot.gameObject);
            keys.Clear();
            keysRoot = new GameObject("Keys").transform;
            keysRoot.SetParent(transform, false);
            keysRoot.localRotation = Quaternion.Euler(tiltDegrees, 0f, 0f);   // penché vers le haut, face à toi

            var rows = Layout();
            float pitch = keySize + gap;
            float top = (rows.Count - 1) * pitch * 0.5f;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                float rowWidth = 0f;
                foreach (var d in rows[ri]) rowWidth += d.width * keySize + (d.width - 1f) * gap + gap;
                rowWidth -= gap;
                float x = -rowWidth * 0.5f;
                float y = top - ri * pitch;
                foreach (var d in rows[ri])
                {
                    float w = d.width * keySize + (d.width - 1f) * gap;
                    AddKey(d, x + w * 0.5f, y, w, keySize);
                    x += w + gap;
                }
            }
            keysRoot.gameObject.SetActive(visible);
        }

        void AddKey(KeyDef d, float cx, float cy, float w, float h)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Key_" + d.label;
            Destroy(go.GetComponent<MeshCollider>());
            go.transform.SetParent(keysRoot, false);
            go.transform.localPosition = new Vector3(cx, cy, 0f);
            go.transform.localScale = new Vector3(w, h, 1f);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(1f, 1f, 0.2f);
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = new Material(shader);

            var lg = new GameObject("Label");
            lg.transform.SetParent(keysRoot, false);
            lg.transform.localPosition = new Vector3(cx, cy, -0.004f);
            var tm = lg.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = d.label;
            tm.fontSize = 80;
            tm.characterSize = 0.0028f * labelScale * (d.label.Length > 3 ? 0.7f : 1f);
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            lg.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            var key = go.AddComponent<Ar6dofKey>();
            key.keyboard = this;
            key.def = d;
            key.label = tm;
            key.quad = rend;
            key.Refresh();
            keys.Add(key);
        }

        // ------------------------------------------------------------------ frappe
        void RefreshAll()
        {
            foreach (var k in keys)
            {
                if (k.def.kind == Kind.Shift) k.active = shift;
                else if (k.def.kind == Kind.Ctrl) k.active = ctrl;
                else if (k.def.kind == Kind.Alt) k.active = alt;
                if (k.def.kind == Kind.Char && k.label != null) k.label.text = shift && !string.IsNullOrEmpty(k.def.shiftLabel) ? k.def.shiftLabel : k.def.label;
                k.Refresh();
            }
        }

        void ModsDown()
        {
            if (ctrl) Ar6dofWinInput.Key(0x11, true);
            if (alt) Ar6dofWinInput.Key(0x12, true);
            if (shift) Ar6dofWinInput.Key(0x10, true);
        }

        void ModsUp()
        {
            if (shift) Ar6dofWinInput.Key(0x10, false);
            if (alt) Ar6dofWinInput.Key(0x12, false);
            if (ctrl) Ar6dofWinInput.Key(0x11, false);
        }

        public void Press(KeyDef d)
        {
            switch (d.kind)
            {
                case Kind.Shift: shift = !shift; RefreshAll(); return;
                case Kind.Ctrl: ctrl = !ctrl; RefreshAll(); return;
                case Kind.Alt: alt = !alt; RefreshAll(); return;
                case Kind.Layout: azerty = !azerty; Build(); return;
                case Kind.Vk:
                    if (VkSink != null) { VkSink(d.vk); shift = ctrl = alt = false; RefreshAll(); return; }
                    ModsDown();
                    Ar6dofWinInput.Key(d.vk, true);
                    Ar6dofWinInput.Key(d.vk, false);
                    ModsUp();
                    break;
                case Kind.Char:
                    if (CharSink != null && !ctrl && !alt)
                    {
                        CharSink(shift && d.shiftCh != '\0' ? d.shiftCh : d.ch);
                        shift = false; RefreshAll();
                        return;
                    }
                    if (ctrl || alt)
                    {   // raccourcis (Ctrl+C...) : touche virtuelle de la disposition actuelle de Windows
                        ushort vk = Ar6dofWinInput.VkForChar(d.ch);
                        ModsDown();
                        Ar6dofWinInput.Key(vk, true);
                        Ar6dofWinInput.Key(vk, false);
                        ModsUp();
                    }
                    else
                    {
                        Ar6dofWinInput.TypeChar(shift && d.shiftCh != '\0' ? d.shiftCh : d.ch);
                    }
                    break;
            }
            shift = ctrl = alt = false;   // les modificateurs ne servent que pour la touche suivante
            RefreshAll();
        }
    }
}
