using System;
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
        public enum Kind { Char, Vk, Shift, Ctrl, Alt, Settings, SetAzerty, SetQwerty, Numpad }

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
        /// <summary>AZERTY ou QWERTY : réglage enregistré (roue du clavier, ou Réglages > Style > Clavier).</summary>
        bool Azerty { get { return Ar6dofSettings.Current.kbAzerty; } }
        public float keySize = 0.045f, gap = 0.006f;
        [Tooltip("Inclinaison du clavier vers toi (degrés)")]
        public float tiltDegrees = 25f;
        [Tooltip("Multiplie la taille des lettres si elles sont trop petites ou trop grandes")]
        public float labelScale = 1f;
        public Shader keyShader;   // vide : AR6DoF/Flat, sinon Sprites/Default
        public KeyCode summonKey = KeyCode.B;

        bool shift, ctrl, alt, visible = true;
        bool builtNumpad;
        float kbMinX, kbMaxX, kbMinY, kbMaxY;
        float nextFx, fxAnimEnd;
        bool fxWasOn;
        // ondes de l'animation "Onde" : une par touche appuyée, elles se cumulent et finissent leur course
        const int MaxRipples = 10;
        readonly Vector2[] rippleOrigin = new Vector2[MaxRipples];
        readonly float[] rippleStart = new float[MaxRipples];
        int rippleCount;
        readonly Vector2[] rpPos = new Vector2[MaxRipples + 1];   // ondes en cours pour l'image dessinée (+1 : onde "de repos" au centre)
        readonly float[] rpAge = new float[MaxRipples + 1];
        Transform keysRoot;
        Font font;
        Shader shader;
        readonly List<Ar6dofKey> keys = new List<Ar6dofKey>();
        bool roundedShader;
        bool settingsOpen, builtAzerty;     // rangée de réglages au-dessus du clavier (roue)
        Vector2 kbHalf = new Vector2(0.4f, 0.12f);   // demi-taille du clavier en mètres (pour les boutons de fenêtre)
        Ar6dofWindowChrome chrome;
        // déplacer / redimensionner
        Ar6dofKeyboardHandle.Kind dragKind;
        float grabDist, startRadius, startScale;
        Vector3 grabOffset;
        bool dragging;

        void OnEnable()
        {
            Ar6dofSettings.Changed += OnStyleChanged;
            Ar6dofLoc.Changed += OnLanguageChanged;
        }

        void OnDisable()
        {
            Ar6dofSettings.Changed -= OnStyleChanged;
            Ar6dofLoc.Changed -= OnLanguageChanged;
        }

        void OnLanguageChanged() { if (shader != null) Build(); }

        void Start()
        {
            font = Ar6dofVrUi.GetFont();
            shader = Ar6dofTheme.RoundedShader;   // touches à coins arrondis et transparentes
            roundedShader = shader != null;
            if (shader == null) shader = keyShader != null ? keyShader : Shader.Find("AR6DoF/Flat");
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
            if (settingsOpen)
                rows.Add(new List<KeyDef> { Mod("AZERTY", Kind.SetAzerty, 2.4f), Mod("QWERTY", Kind.SetQwerty, 2.4f), Mod(Ar6dofLoc.T("Pave num."), Kind.Numpad, 2.8f) });
            if (Azerty)
            {
                r = CharRow("²&é\"'(-è_çà)=", "³1234567890°+"); r.Add(Vk(Ar6dofLoc.T("Retour"), 0x08, 2f)); rows.Add(r);
                r = new List<KeyDef> { Vk("Tab", 0x09, 1.5f) }; r.AddRange(CharRow("azertyuiop^$", "AZERTYUIOP¨£")); rows.Add(r);
                r = CharRow("qsdfghjklmù*", "QSDFGHJKLM%µ"); r.Add(Vk(Ar6dofLoc.T("Entree"), 0x0D, 2f)); rows.Add(r);
                r = new List<KeyDef> { Mod(Ar6dofLoc.T("Maj"), Kind.Shift, 1.6f) }; r.AddRange(CharRow("<wxcvbn,;:!", ">WXCVBN?./§")); rows.Add(r);
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
                new KeyDef { label = Ar6dofLoc.T("Espace"), ch = ' ', shiftCh = ' ', width = 5f, kind = Kind.Char },
                Vk(Ar6dofLoc.T("Echap"), 0x1B, 1.3f), Vk(Ar6dofLoc.T("Suppr"), 0x2E, 1.3f), Vk("<", 0x25), Vk("^", 0x26), Vk("v", 0x28), Vk(">", 0x27),
                Vk(Ar6dofLoc.T("PgPr"), 0x21, 1.2f), Vk(Ar6dofLoc.T("PgSv"), 0x22, 1.2f),
                Mod("", Kind.Settings, 1.4f)   // roue : disposition AZERTY / QWERTY, pavé numérique
            });
            return rows;
        }

        /// <summary>Pavé numérique : 4 colonnes x 5 lignes, aligné sur les 5 lignes du clavier. Les chiffres sont envoyés comme des caractères (marche sans verrouillage numérique).</summary>
        static List<List<KeyDef>> NumpadRows()
        {
            Func<string, KeyDef> c = s => new KeyDef { label = s, ch = s[0], shiftCh = s[0], kind = Kind.Char };
            var rows = new List<List<KeyDef>>();
            rows.Add(new List<KeyDef> { Vk(Ar6dofLoc.T("Retour"), 0x08), c("/"), c("*"), c("-") });
            rows.Add(new List<KeyDef> { c("7"), c("8"), c("9"), c("+") });
            rows.Add(new List<KeyDef> { c("4"), c("5"), c("6"), c(",") });
            rows.Add(new List<KeyDef> { c("1"), c("2"), c("3"), Vk(Ar6dofLoc.T("Entree"), 0x0D) });
            var zero = c("0"); zero.width = 2f;
            rows.Add(new List<KeyDef> { zero, c("."), c("=") });
            return rows;
        }

        static float RowWidth(List<KeyDef> row, float keySize, float gap)
        {
            float w = 0f;
            foreach (var d in row) w += d.width * keySize + (d.width - 1f) * gap + gap;
            return w - gap;
        }

        void Build()
        {
            if (keysRoot != null) Destroy(keysRoot.gameObject);
            keys.Clear();
            keysRoot = new GameObject("Keys").transform;
            keysRoot.SetParent(transform, false);
            keysRoot.localRotation = Quaternion.Euler(tiltDegrees, 0f, 0f);   // penché vers le haut, face à toi
            keysRoot.localScale = Vector3.one * Mathf.Clamp(Ar6dofSettings.Current.keyboardScale, 0.4f, 3f);

            var rows = Layout();
            builtNumpad = Ar6dofSettings.Current.kbNumpad;
            builtAzerty = Azerty;
            var pad = builtNumpad ? NumpadRows() : null;
            float pitch = keySize + gap;
            float top = (rows.Count - 1) * pitch * 0.5f;
            float mainW = 0f;
            foreach (var r0 in rows) mainW = Mathf.Max(mainW, RowWidth(r0, keySize, gap));
            float padW = pad != null ? 4f * keySize + 3f * gap : 0f;
            float padGap = pad != null ? keySize * 0.7f : 0f;
            float maxW = mainW + padGap + padW;                       // largeur totale
            float originX = -maxW * 0.5f;
            kbMinX = kbMinY = 1e9f; kbMaxX = kbMaxY = -1e9f;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                float rowWidth = RowWidth(rows[ri], keySize, gap);
                float x = originX + (mainW - rowWidth) * 0.5f;
                float y = top - ri * pitch;
                foreach (var d in rows[ri])
                {
                    float w = d.width * keySize + (d.width - 1f) * gap;
                    AddKey(d, x + w * 0.5f, y, w, keySize);
                    x += w + gap;
                }
            }
            if (pad != null)
                for (int ri = 0; ri < pad.Count; ri++)
                {
                    float x = originX + mainW + padGap;
                    float y = top - (ri + rows.Count - pad.Count) * pitch;   // aligné sur les 5 dernières rangées (la rangée de réglages est au-dessus)
                    foreach (var d in pad[ri])
                    {
                        float w = d.width * keySize + (d.width - 1f) * gap;
                        AddKey(d, x + w * 0.5f, y, w, keySize);
                        x += w + gap;
                    }
                }
            foreach (var k in keys) k.Refresh();
            RefreshAll();
            keysRoot.gameObject.SetActive(visible);
            // boutons de fenêtre communs à tous les menus : réduire, fermer, redimensionner (coin bas droit) et barre de déplacement
            kbHalf = new Vector2(maxW * 0.5f, ((rows.Count - 1) * pitch + keySize) * 0.5f);
            if (chrome != null) Destroy(chrome.gameObject);
            var kr = keysRoot;
            chrome = Ar6dofWindowChrome.Create("Clavier", kr, Ar6dofElem.Keyboard, () => kbHalf * kr.lossyScale.x, Stow, Stow);
            chrome.canMove = true;
            chrome.moveBegin = (hit, btn) => BeginDrag(Ar6dofKeyboardHandle.Kind.Move, hit);
            chrome.moveDrag = ray => Drag(ray);
            chrome.moveEnd = EndDrag;
            chrome.resizeBegin = hit => BeginDrag(Ar6dofKeyboardHandle.Kind.Resize, hit);
            chrome.resizeDrag = ray => Drag(ray);
            chrome.resizeEnd = EndDrag;
        }

        /// <summary>Réduire / fermer : le clavier rentre dans la barre de tâches.</summary>
        void Stow()
        {
            if (Ar6dofDock.Instance != null) Ar6dofDock.Instance.StowKeyboard(); else SetVisible(false);
        }

        // ------------------------------------------------------------------ style
        void OnStyleChanged()
        {
            if (keysRoot == null) return;
            if (Ar6dofSettings.Current.kbNumpad != builtNumpad || Azerty != builtAzerty) { Build(); return; }   // pavé numérique ajouté ou retiré, ou disposition changée
            keysRoot.localScale = Vector3.one * Mathf.Clamp(Ar6dofSettings.Current.keyboardScale, 0.4f, 3f);
            int corner = Ar6dofSettings.StyleFor(Ar6dofElem.Keyboard).corner;
            foreach (var k in keys)
            {
                if (roundedShader && k.quad != null) Ar6dofTheme.SetRoundedSize(k.quad.sharedMaterial, k.quad.transform.localScale.x, k.quad.transform.localScale.y, corner);
                k.Refresh();
            }
        }

        // ------------------------------------------------------------------ déplacer / redimensionner (poignées sous le clavier)
        Transform HeadT { get { return passthrough != null ? passthrough.HeadView : null; } }

        public void BeginDrag(Ar6dofKeyboardHandle.Kind kind, Vector3 hit)
        {
            dragKind = kind;
            dragging = true;
            if (kind == Ar6dofKeyboardHandle.Kind.Move)
            {
                grabDist = Vector3.Distance(Ar6dofPointer.LastRay.origin, hit);
                grabOffset = transform.position - hit;
            }
            else
            {
                startRadius = Mathf.Max(0.05f, (hit - keysRoot.position).magnitude);
                startScale = keysRoot.localScale.x;
            }
        }

        public void Drag(Ray ray)
        {
            if (!dragging || keysRoot == null) return;
            if (dragKind == Ar6dofKeyboardHandle.Kind.Move)
            {
                Vector3 p = ray.origin + ray.direction * grabDist + grabOffset;
                transform.position = p;
                Transform head = HeadT;
                if (head != null)
                {
                    Vector3 d = p - head.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d.normalized);   // reste face à toi, à la verticale
                }
            }
            else
            {
                var plane = new Plane(keysRoot.forward, keysRoot.position);
                float t;
                if (!plane.Raycast(ray, out t)) return;
                float radius = (ray.GetPoint(t) - keysRoot.position).magnitude;
                float sc = Mathf.Clamp(startScale * radius / startRadius, 0.5f, 2.5f);
                keysRoot.localScale = Vector3.one * sc;
                Ar6dofSettings.Current.keyboardScale = sc;
            }
        }

        public void EndDrag() { dragging = false; }

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
            if (roundedShader) Ar6dofTheme.SetRoundedSize(rend.sharedMaterial, w, h, Ar6dofSettings.StyleFor(Ar6dofElem.Keyboard).corner);

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
            key.pos = new Vector2(cx, cy);
            key.seed = UnityEngine.Random.value;
            if (d.kind == Kind.Settings)
            {   // roue crantée dessinée à la place du texte
                var ig = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ig.name = "Roue";
                var imc = ig.GetComponent<MeshCollider>();
                if (imc != null) Destroy(imc);
                ig.transform.SetParent(keysRoot, false);
                ig.transform.localPosition = new Vector3(cx, cy, -0.004f);
                float isz = Mathf.Min(w, h) * 0.62f;
                ig.transform.localScale = new Vector3(isz, isz, 1f);
                var ish = Shader.Find("Sprites/Default");
                var im = new Material(ish != null ? ish : shader);
                im.mainTexture = Ar6dofIcons.Get("gear");
                var ir = ig.GetComponent<Renderer>();
                ir.sharedMaterial = im;
                ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ir.receiveShadows = false;
                key.icon = ir;
            }
            kbMinX = Mathf.Min(kbMinX, cx); kbMaxX = Mathf.Max(kbMaxX, cx);
            kbMinY = Mathf.Min(kbMinY, cy); kbMaxY = Mathf.Max(kbMaxY, cy);
            key.Refresh();
            keys.Add(key);
        }

        // ------------------------------------------------------------------ animation lumineuse
        /// <summary>Appelé à chaque touche : lance l'onde et le flash de l'animation "Reactif".</summary>
        void NoteKeyPress(KeyDef def)
        {
            foreach (var k in keys)
                if (k.def == def)
                {
                    k.flash = 1f;
                    if (rippleCount == MaxRipples)
                    {   // trop d'ondes en même temps : la plus ancienne disparaît
                        for (int i = 1; i < MaxRipples; i++) { rippleOrigin[i - 1] = rippleOrigin[i]; rippleStart[i - 1] = rippleStart[i]; }
                        rippleCount--;
                    }
                    rippleOrigin[rippleCount] = k.pos;
                    rippleStart[rippleCount] = Time.unscaledTime;
                    rippleCount++;
                    break;
                }
        }

        void Update()
        {
            if (keysRoot == null || !visible) return;
            var d = Ar6dofSettings.Current;
            float now = Time.unscaledTime;
            if (d.kbAnim == 0)
            {
                if (fxWasOn)
                {   // animation coupée : toutes les touches reprennent leur couleur normale
                    fxWasOn = false;
                    foreach (var k in keys) { k.animAmount = 0f; k.flash = 0f; k.Refresh(); }
                }
                return;
            }
            if (now < nextFx) return;
            nextFx = now + 1f / 30f;   // 30 mises à jour par seconde suffisent à l'oeil et coûtent très peu
            fxWasOn = true;

            float t = now * Mathf.Clamp(d.kbAnimSpeed, 0.05f, 6f);
            float strength = Mathf.Clamp01(d.kbAnimStrength);
            float w = Mathf.Max(0.001f, kbMaxX - kbMinX), h = Mathf.Max(0.001f, kbMaxY - kbMinY);
            Vector2 center = new Vector2((kbMinX + kbMaxX) * 0.5f, (kbMinY + kbMaxY) * 0.5f);
            // ondes : vitesse du front (m/s), distance maximale à parcourir, retrait de celles qui ont fini leur course
            float rippleSpeed = 0.28f * Mathf.Max(0.3f, d.kbAnimSpeed);
            float rippleMax = Mathf.Sqrt(w * w + h * h);
            float rippleLife = (rippleMax + 0.15f) / rippleSpeed;
            int rpN = 0;
            if (d.kbAnim == 6)
            {
                int keep = 0;
                for (int i = 0; i < rippleCount; i++)
                {
                    if (now - rippleStart[i] > rippleLife) continue;
                    rippleOrigin[keep] = rippleOrigin[i]; rippleStart[keep] = rippleStart[i]; keep++;
                }
                rippleCount = keep;
                for (int i = 0; i < rippleCount; i++) { rpPos[rpN] = rippleOrigin[i]; rpAge[rpN] = now - rippleStart[i]; rpN++; }
                if (rpN == 0) { rpPos[0] = center; rpAge[0] = Mathf.Repeat(now, rippleLife + 0.6f); rpN = 1; }   // au repos : une onde part du centre en boucle
            }
            else rippleCount = 0;
            foreach (var k in keys)
            {
                float u = (k.pos.x - kbMinX) / w, v = (k.pos.y - kbMinY) / h;   // 0..1 sur le clavier
                float amount = 1f;
                Color col;
                switch (d.kbAnim)
                {
                    case 1:   // vague : la palette défile dans le sens choisi
                    {
                        float p;
                        switch (d.kbAnimDir)
                        {
                            case 1: p = -u; break;
                            case 2: p = -v; break;     // vers le bas
                            case 3: p = v; break;      // vers le haut
                            case 4: p = ((k.pos - center).magnitude / (0.5f * Mathf.Sqrt(w * w + h * h))); break;
                            default: p = u; break;
                        }
                        col = Ar6dofKbFx.Sample(d.kbAnimPalette, p * 0.8f - t * 0.2f);
                        break;
                    }
                    case 2:   // cycle : toutes les touches ensemble
                        col = Ar6dofKbFx.Sample(d.kbAnimPalette, t * 0.12f);
                        break;
                    case 3:   // respiration : fondu doux
                        col = Ar6dofKbFx.Sample(d.kbAnimPalette, t * 0.05f);
                        amount = 0.5f + 0.5f * Mathf.Sin(t * 1.8f);
                        break;
                    case 4:   // réactif : la touche s'allume quand on l'appuie, puis s'éteint
                        k.flash = Mathf.Max(0f, k.flash - (1f / 30f) * 1.4f / Mathf.Max(0.3f, d.kbAnimSpeed));
                        col = Ar6dofKbFx.Sample(d.kbAnimPalette, u * 0.6f + t * 0.1f);
                        amount = k.flash;
                        break;
                    case 5:   // scintillement : chaque touche brille à son tour, au hasard
                    {
                        float s = Mathf.Sin(t * 2.2f + k.seed * 40f);
                        amount = Mathf.Pow(Mathf.Clamp01(s), 6f);
                        col = Ar6dofKbFx.Sample(d.kbAnimPalette, k.seed + t * 0.05f);
                        break;
                    }
                    default:  // 6 onde : un cercle part de chaque touche appuyée ; les ondes se cumulent et vont jusqu'au bout
                    {
                        float keep = 1f, wsum = 0f;
                        Color acc = Color.black;
                        for (int i = 0; i < rpN; i++)
                        {
                            float dist = (k.pos - rpPos[i]).magnitude;
                            float front = rpAge[i] * rippleSpeed;
                            float e = (dist - front) / 0.035f;
                            if (e > 3f || e < -3f) continue;
                            float a = Mathf.Exp(-e * e) * (1f - 0.7f * Mathf.Clamp01(front / rippleMax));
                            keep *= 1f - a;
                            acc += Ar6dofKbFx.Sample(d.kbAnimPalette, dist * 3f - t * 0.1f) * a;
                            wsum += a;
                        }
                        amount = 1f - keep;
                        col = wsum > 0.0001f ? acc / wsum : Ar6dofKbFx.Sample(d.kbAnimPalette, t * 0.1f);
                        col.a = 1f;
                        break;
                    }
                }
                k.animColor = col;
                k.animAmount = Mathf.Clamp01(amount * strength);
                if (d.kbAnim == 4 && k.flash > 0f) k.animAmount = Mathf.Clamp01(Mathf.Max(k.animAmount, k.flash * Mathf.Max(0.5f, strength)));
                k.Refresh();
            }
        }

        // ------------------------------------------------------------------ frappe
        void RefreshAll()
        {
            foreach (var k in keys)
            {
                if (k.def.kind == Kind.Shift) k.active = shift;
                else if (k.def.kind == Kind.Ctrl) k.active = ctrl;
                else if (k.def.kind == Kind.Alt) k.active = alt;
                else if (k.def.kind == Kind.Settings) k.active = settingsOpen;
                else if (k.def.kind == Kind.SetAzerty) k.active = Azerty;
                else if (k.def.kind == Kind.SetQwerty) k.active = !Azerty;
                else if (k.def.kind == Kind.Numpad) k.active = Ar6dofSettings.Current.kbNumpad;
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

        void SetLayout(bool azerty)
        {
            Ar6dofSettings.Current.kbAzerty = azerty;
            Ar6dofSettings.Save();      // le choix de disposition est gardé tout de suite
            Ar6dofSettings.Notify();
        }

        public void Press(KeyDef d)
        {
            NoteKeyPress(d);
            switch (d.kind)
            {
                case Kind.Shift: shift = !shift; RefreshAll(); return;
                case Kind.Ctrl: ctrl = !ctrl; RefreshAll(); return;
                case Kind.Alt: alt = !alt; RefreshAll(); return;
                case Kind.Settings: settingsOpen = !settingsOpen; Build(); return;
                case Kind.SetAzerty: SetLayout(true); return;
                case Kind.SetQwerty: SetLayout(false); return;
                case Kind.Numpad:
                    Ar6dofSettings.Current.kbNumpad = !Ar6dofSettings.Current.kbNumpad;
                    Ar6dofSettings.Save();
                    Ar6dofSettings.Notify();
                    return;
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
