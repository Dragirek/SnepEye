using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Objets 3D : pose dans le monde des modeles 3D (.obj + .mtl + textures), des stickers (images png / jpg) et des formes simples. Les objets restent a leur place
    /// (repere du suivi 6DoF de la session) et sont enregistres. Ils se deplacent au laser, se reglent (taille, teinte, opacite, rotation, face a moi...) et se suppriment ici.
    /// L'appareil photo les montre dans les photos « avec interfaces » seulement.
    /// </summary>
    public class Ar6dofObjectsApp : Ar6dofAppWindow
    {
        static readonly string[] ModelExt = { ".obj" };
        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg" };
        static readonly Color[] Palette =
        {
            Color.white,
            new Color(0.90f, 0.30f, 0.30f), new Color(0.95f, 0.60f, 0.20f), new Color(0.95f, 0.85f, 0.25f), new Color(0.40f, 0.80f, 0.40f),
            new Color(0.25f, 0.75f, 0.75f), new Color(0.25f, 0.50f, 0.90f), new Color(0.60f, 0.40f, 0.90f), new Color(0.90f, 0.40f, 0.70f),
            new Color(0.50f, 0.50f, 0.52f), new Color(0.12f, 0.12f, 0.14f)
        };
        const int PerPage = 6;

        readonly Ar6dofWorldObjects world = new Ar6dofWorldObjects();
        WorldObj editing;
        int page, editTab, confirmId;
        bool confirmAll;
        string status = "";

        public Ar6dofObjectsApp(Ar6dofDock d) : base(d, "objects", "Objets 3D", "cube", "Modeles 3D, stickers et formes poses dans le monde", 720f)
        {
            world.Load();
        }

        public override void Destroy()
        {
            world.Dispose();
            base.Destroy();
        }

        protected override void OnVisible(bool on)
        {
            if (!on)
            {
                world.SetGrab(false);
                if (world.IsDirty) world.Save();
                editing = null; confirmId = 0; confirmAll = false;
            }
        }

        protected override void OnTick() { world.Tick(); }

        Transform Head()
        {
            if (dock.passthrough != null && dock.passthrough.HeadView != null) return dock.passthrough.HeadView;
            return Camera.main != null ? Camera.main.transform : null;
        }

        /// <summary>Position et cap pour poser un objet devant toi (a 90 cm, a hauteur des yeux).</summary>
        void InFront(int kind, out Vector3 pos, out float yaw)
        {
            var h = Head();
            if (h == null) { pos = new Vector3(0f, 0f, 0.9f); yaw = 0f; return; }
            Vector3 f = h.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            pos = h.position + f * 0.9f;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + (kind == 0 ? 180f : 0f);   // un modele regarde vers +z, la face d'un sticker est vers -z
        }

        void AddNew(int kind, string file)
        {
            Vector3 pos; float yaw;
            InFront(kind, out pos, out yaw);
            var o = world.Add(kind, file, pos, yaw);
            if (o == null) { status = Ar6dofLoc.F("Maximum : {0} objets", Ar6dofWorldObjects.MaxObjects); Rebuild(); return; }
            var it = world.Get(o);
            status = it != null && it.error.Length > 0 ? it.error : "";
            editing = o; editTab = 0; confirmId = 0;
            Rebuild();
        }

        // ------------------------------------------------------------------ liste
        protected override void BuildMain()
        {
            if (editing != null && world.data.items.Contains(editing)) { BuildEditor(editing); return; }
            editing = null;

            Para("Pose dans le monde des modeles 3D (.obj), des images (stickers) et des formes. Ils restent a leur place et sont enregistres.", 56, 18);
            BtnRow(52f, 20,
                "+ Modele 3D", (Action)(() => Browse("Choisir un modele 3D (.obj)", ModelExt, false, f => AddNew(0, f[0]))),
                "+ Sticker", (Action)(() => Browse("Choisir une image", ImageExt, false, f => AddNew(1, f[0]))),
                "+ Forme", (Action)(() => Pick("Choisir une forme", ShapeList(), -1, i => AddNew(2, Ar6dofWorldObjects.ShapeNames[i]))));
            ToggleRow("Deplacer au laser", () => world.grab, v => { world.SetGrab(v); }, 50f, 22);
            Para("Active-le, vise un objet avec le laser et garde le bouton appuye pour le deplacer.", 30, 16);

            var st = Para("", 30, 17);
            refreshers.Add(() => st.text = status.Length > 0 ? Ar6dofLoc.T(status) : "");

            int n = world.data.items.Count;
            Section(Ar6dofLoc.F("Objets : {0}", n));
            if (n == 0) Para("Aucun objet. Utilise un des boutons « + » ci-dessus.", 30, 18);
            int pages = Mathf.Max(1, (n + PerPage - 1) / PerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            for (int i = page * PerPage; i < Mathf.Min(n, (page + 1) * PerPage); i++) ItemRow(world.data.items[i]);
            if (pages > 1)
            {
                BtnRow(48f, 22, "<", (Action)(() => { page = (page + pages - 1) % pages; confirmId = 0; Rebuild(); }),
                                Ar6dofLoc.F("Page {0}/{1}", page + 1, pages), (Action)(() => { }),
                                ">", (Action)(() => { page = (page + 1) % pages; confirmId = 0; Rebuild(); }));
            }
            if (n > 0)
            {
                Text dt;
                var del = BtnT("", () =>
                {
                    if (!confirmAll) { confirmAll = true; Refresh(); return; }
                    world.RemoveAll();
                    confirmAll = false; confirmId = 0; page = 0;
                    Rebuild();
                }, out dt, 48f, 20);
                Text dtt = dt;
                refreshers.Add(() => { dtt.text = Ar6dofLoc.T(confirmAll ? "Confirmer : tout supprimer" : "Tout supprimer"); Ar6dofTheme.SetSelected(del, confirmAll); });
            }
        }

        static List<string> ShapeList()
        {
            var l = new List<string>();
            foreach (var s in Ar6dofWorldObjects.ShapeNames) l.Add(char.ToUpper(s[0]) + s.Substring(1));
            return l;
        }

        void ItemRow(WorldObj o)
        {
            const float h = 56f;
            float cy = RowY(h);
            float x0 = -IW * 0.5f;
            float eyeW = 84f, editW = 140f, delW = 110f;
            float nameW = IW - eyeW - editW - delW - 3 * 8f;
            Text te, td, tx;
            var eye = BtnAt(x0 + eyeW * 0.5f, cy, eyeW, 48f, "Voir", () => { o.visible = !o.visible; world.Apply(o); world.MarkDirty(); Refresh(); }, out te, 19);
            var name = Ar6dofVrUi.Label(content, "", new Vector2(x0 + eyeW + 8f + nameW * 0.5f, cy), new Vector2(nameW, h), 20, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            BtnAt(x0 + eyeW + nameW + 16f + editW * 0.5f, cy, editW, 48f, "Modifier", () => { editing = o; editTab = 0; confirmId = 0; confirmAll = false; Rebuild(); }, out tx, 19);
            var del = BtnAt(x0 + IW - delW * 0.5f, cy, delW, 48f, "", () =>
            {
                if (confirmId != o.id) { confirmId = o.id; confirmAll = false; Refresh(); return; }
                world.Remove(o);
                confirmId = 0;
                Rebuild();
            }, out td, 19);
            Text tdd = td;
            refreshers.Add(() =>
            {
                Ar6dofTheme.SetSelected(eye, o.visible);
                name.text = o.name + (world.Get(o) != null && world.Get(o).error.Length > 0 ? "  (!)" : "");
                tdd.text = Ar6dofLoc.T(confirmId == o.id ? "Sur ?" : "Supprimer");
                Ar6dofTheme.SetSelected(del, confirmId == o.id);
            });
        }

        // ------------------------------------------------------------------ edition
        void Changed(WorldObj o) { world.Apply(o); world.MarkDirty(); }

        void BuildEditor(WorldObj o)
        {
            var it = world.Get(o);
            Lbl(Ar6dofLoc.T(Ar6dofWorldObjects.KindNames[Mathf.Clamp(o.kind, 0, 2)]), 36, 26).MakeAccent();
            float cy = RowY(50f);
            Ar6dofVrTextField.Make(content, new Vector2(0f, cy), new Vector2(IW, 50f), o.name, "Nom", s => { o.name = s; world.MarkDirty(); }, s => { o.name = s; world.MarkDirty(); }, 24);
            if (it != null && it.error.Length > 0) Para(Ar6dofLoc.T(it.error) + " : " + (o.file ?? ""), 56, 17);

            Tabs(new[] { "Aspect", "Position" }, editTab, t => { editTab = t; confirmId = 0; });
            if (editTab == 0) AspectTab(o); else PositionTab(o);

            Space(4f);
            BtnRow(50f, 20,
                "Dupliquer", (Action)(() => { var n = world.Duplicate(o); if (n != null) { editing = n; confirmId = 0; Rebuild(); } }),
                "Devant moi", (Action)(() => { Vector3 p; float yw; InFront(o.kind, out p, out yw); o.px = p.x; o.py = p.y; o.pz = p.z; o.yaw = yw; Changed(o); Refresh(); }));
            Text dt;
            var del = BtnT("", () =>
            {
                if (confirmId != o.id) { confirmId = o.id; Refresh(); return; }
                world.Remove(o);
                editing = null; confirmId = 0;
                Rebuild();
            }, out dt, 50f, 21);
            Text dtt = dt;
            refreshers.Add(() => { dtt.text = Ar6dofLoc.T(confirmId == o.id ? "Confirmer la suppression" : "Supprimer"); Ar6dofTheme.SetSelected(del, confirmId == o.id); });
            Btn("Retour", () => { editing = null; confirmId = 0; Rebuild(); }, 50f, 22);
        }

        void AspectTab(WorldObj o)
        {
            if (o.kind == 2)
            {
                var b = Btn("", () => Pick("Choisir une forme", ShapeList(), Array.IndexOf(Ar6dofWorldObjects.ShapeNames, o.file),
                    i => { o.file = Ar6dofWorldObjects.ShapeNames[i]; world.Reload(o); world.MarkDirty(); }), 50f, 20);
                var bt = b.GetComponentInChildren<Text>();
                refreshers.Add(() => bt.text = Ar6dofLoc.T("Forme : ") + Ar6dofLoc.T(o.file));
            }
            else
            {
                var b = Btn("", () => Browse(o.kind == 0 ? "Choisir un modele 3D (.obj)" : "Choisir une image", o.kind == 0 ? ModelExt : ImageExt, false, f =>
                {
                    Ar6dofWorldObjects.ForgetFile(o.file);
                    o.file = f[0];
                    Ar6dofWorldObjects.ForgetFile(o.file);   // relit aussi un fichier modifie depuis
                    world.Reload(o);
                    world.MarkDirty();
                    var it = world.Get(o);
                    status = it != null && it.error.Length > 0 ? it.error : "";
                    Rebuild();
                }), 50f, 20);
                var bt = b.GetComponentInChildren<Text>();
                refreshers.Add(() => bt.text = string.IsNullOrEmpty(o.file) ? Ar6dofLoc.T("Choisir un fichier...") : Path.GetFileName(o.file));
                Btn("Relire le fichier", () =>
                {
                    Ar6dofWorldObjects.ForgetFile(o.file);
                    world.Reload(o);
                    var it = world.Get(o);
                    status = it != null && it.error.Length > 0 ? it.error : "Fichier relu";
                    Refresh();
                }, 46f, 20);
            }

            // teinte
            float cyc = RowY(46f);
            float sw = (IW - (Palette.Length - 1) * 6f) / Palette.Length;
            for (int i = 0; i < Palette.Length; i++)
            {
                var c = Palette[i];
                var rt = Ar6dofVrUi.Rect(content, "Couleur", new Vector2(-IW * 0.5f + sw * 0.5f + i * (sw + 6f), cyc), new Vector2(sw, 44f));
                var im = rt.gameObject.AddComponent<Image>();
                im.color = c;
                Ar6dofTheme.SetRounded(im, 12);
                var bt = rt.gameObject.AddComponent<Button>();
                bt.targetGraphic = im;
                bt.onClick.AddListener(() => { o.r = c.r; o.g = c.g; o.b = c.b; Changed(o); });
            }

            SliderRow("Taille", 0.02f, 2f, () => o.size, v => { o.size = v; Changed(o); }, v => v < 1f ? Mathf.RoundToInt(v * 100f) + " cm" : Fmt("{0:0.00} m", v), 50f);
            SliderRow("Opacite", 0.1f, 1f, () => o.alpha, v => { o.alpha = v; Changed(o); }, Pct, 50f);
            ToggleRow("Visible", () => o.visible, v => { o.visible = v; Changed(o); }, 48f, 21);
            if (o.kind != 1) ToggleRow("Eclairage", () => o.lit, v => { o.lit = v; Changed(o); }, 48f, 21);
            ToggleRow("Toujours face a moi", () => o.billboard, v => { o.billboard = v; Changed(o); }, 48f, 21);
        }

        void PositionTab(WorldObj o)
        {
            SliderRow("Gauche - droite (X)", -6f, 6f, () => o.px, v => { o.px = v; Changed(o); }, M, 50f);
            SliderRow("Hauteur (Y)", -3f, 4f, () => o.py, v => { o.py = v; Changed(o); }, M, 50f);
            SliderRow("Avant - arriere (Z)", -6f, 6f, () => o.pz, v => { o.pz = v; Changed(o); }, M, 50f);
            SliderRow("Cap", -180f, 180f, () => Wrap(o.yaw), v => { o.yaw = v; Changed(o); }, Deg, 50f);
            SliderRow("Inclinaison", -180f, 180f, () => Wrap(o.pitch), v => { o.pitch = v; Changed(o); }, Deg, 50f);
            SliderRow("Roulis", -180f, 180f, () => Wrap(o.roll), v => { o.roll = v; Changed(o); }, Deg, 50f);
            BtnRow(48f, 20,
                "Plus pres", (Action)(() => Nudge(o, -0.1f)),
                "Plus loin", (Action)(() => Nudge(o, 0.1f)),
                "Cap +90", (Action)(() => { o.yaw = Wrap(o.yaw + 90f); Changed(o); Refresh(); }));
            Para("Plus pres / plus loin : le long de la ligne qui va de ta tete a l'objet.", 30, 16);
        }

        static string M(float v) { return Fmt("{0:0.00} m", v); }
        static string Deg(float v) { return Mathf.RoundToInt(v) + "°"; }
        static float Wrap(float a) { a = Mathf.Repeat(a + 180f, 360f) - 180f; return a; }

        void Nudge(WorldObj o, float dist)
        {
            var h = Head();
            Vector3 p = new Vector3(o.px, o.py, o.pz);
            Vector3 dir = h != null ? p - h.position : Vector3.forward;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            p += dir * dist;
            o.px = p.x; o.py = p.y; o.pz = p.z;
            Changed(o);
            Refresh();
        }
    }
}
