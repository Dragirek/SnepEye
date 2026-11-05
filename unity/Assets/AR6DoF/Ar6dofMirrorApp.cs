using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Miroir : une caméra libre (derrière la tête, sur la queue, au bout d'un bâton...) affichée dans une fenêtre que tu places où tu veux.
    /// La caméra se règle dans Réglages > Image > Caméras (USB ou flux réseau). Elle ne tourne que tant que cette fenêtre est affichée ;
    /// réduite ou fermée, la caméra s'arrête (et sa LED infrarouge s'éteint). Effet miroir, retournement et taille sont mémorisés.
    /// </summary>
    public class Ar6dofMirrorApp : Ar6dofAppWindow
    {
        Ar6dofCamCfg cur;        // caméra choisie
        Ar6dofCamCfg fedCfg;     // caméra dont cette fenêtre tient la source ouverte
        Ar6dofCamFeed feed;
        bool mir, flip;
        int size;
        RawImage img;
        RectTransform imgRt;
        Text status;
        float builtAspect = 4f / 3f;
        float lastAspectCheck;

        public Ar6dofMirrorApp(Ar6dofDock d) : base(d, "mirror", "Miroir", "mirror", "Une camera libre (derriere ta tete...) dans une fenetre", 760f)
        {
            mir = Get("mir", 1) != 0; flip = Get("flip", 0) != 0; size = Mathf.Clamp(Get("size", 1), 0, 2);
        }

        static int Get(string k, int def) { try { return PlayerPrefs.GetInt("ar6dof_mirror_" + k, def); } catch (Exception) { return def; } }
        static void Put(string k, int v) { try { PlayerPrefs.SetInt("ar6dof_mirror_" + k, v); } catch (Exception) { } }

        static Ar6dofCameras Hub { get { return Ar6dofCameras.Instance; } }

        List<Ar6dofCamCfg> Avail()
        {
            var l = new List<Ar6dofCamCfg>();
            if (Hub != null) foreach (var c in Hub.cams) if (c.enabled) l.Add(c);
            return l;
        }

        // la caméra choisie : celle dont le nom est mémorisé, sinon la première disponible
        void Ensure()
        {
            var l = Avail();
            if (cur != null && l.Contains(cur)) return;
            cur = null;
            string saved = "";
            try { saved = PlayerPrefs.GetString("ar6dof_mirror_cam", ""); } catch (Exception) { }
            foreach (var c in l) if (c.name == saved) { cur = c; break; }
            if (cur == null && l.Count > 0) cur = l[0];
        }

        void Attach()
        {
            Ensure();
            if (cur == null || Hub == null) return;
            if (fedCfg == cur && feed != null) return;
            Detach();
            fedCfg = cur;
            feed = Hub.Acquire(cur);
        }

        void Detach()
        {
            if (fedCfg != null && Hub != null) Hub.Release(fedCfg);
            fedCfg = null;
            feed = null;
        }

        protected override void OnVisible(bool on)
        {
            if (on) Attach(); else Detach();
        }

        public override void OnClosed() { Detach(); }

        public override void Destroy()
        {
            Detach();
            base.Destroy();
        }

        protected override void OnRebuildStart() { img = null; imgRt = null; status = null; }

        void NextCamera()
        {
            var l = Avail();
            if (l.Count == 0) return;
            int i = l.IndexOf(cur);
            Detach();
            cur = l[(i + 1) % l.Count];
            try { PlayerPrefs.SetString("ar6dof_mirror_cam", cur.name); } catch (Exception) { }
            Attach();
            Rebuild();
        }

        // largeur / hauteur de l'image affichée (une moitié seulement pour un flux à deux images, tourné si besoin)
        float AspectNow()
        {
            if (feed == null || !feed.Ready || cur == null) return builtAspect;
            float w = feed.Width, h = feed.Height;
            if (cur.stereo == 1) w *= 0.5f; else if (cur.stereo == 2) h *= 0.5f;
            float a = w / Mathf.Max(1f, h);
            if (cur.rotate % 2 == 1) a = 1f / a;
            return Mathf.Clamp(a, 0.4f, 3f);
        }

        protected override void BuildMain()
        {
            Ensure();
            if (Hub == null || cur == null)
            {
                Para("Aucune camera n'est activee. Ajoute-en une dans Reglages > Image > Cameras (camera USB ou flux reseau), par exemple celle placee derriere ta tete.", 92, 20);
                return;
            }
            if (canvas.gameObject.activeInHierarchy) Attach();

            Text cl;
            BtnT("", NextCamera, out cl, 52f, 22);
            Text cll = cl;
            refreshers.Add(() => cll.text = Ar6dofLoc.T("Camera") + " : " + (cur != null ? cur.name : "-") + "   >");

            builtAspect = AspectNow();
            float wpx = size == 0 ? IW * 0.5f : size == 1 ? IW * 0.75f : IW;
            float hpx = wpx / builtAspect;
            float cy = RowY(hpx);
            // le cadre : un fond sombre derrière l'image, qui reste visible tant que la caméra n'a rien envoyé
            var frame = Ar6dofVrUi.Rect(content, "Cadre miroir", new Vector2(0f, cy), new Vector2(wpx + 8f, hpx + 8f));
            var fi = frame.gameObject.AddComponent<Image>();
            fi.color = new Color(0f, 0f, 0f, 0.85f);
            fi.raycastTarget = false;
            imgRt = Ar6dofVrUi.Rect(frame, "Image miroir", Vector2.zero, cur.rotate % 2 == 1 ? new Vector2(hpx, wpx) : new Vector2(wpx, hpx));
            imgRt.localRotation = Quaternion.Euler(0f, 0f, -90f * cur.rotate);
            img = imgRt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.color = Color.clear;

            status = Lbl("", 30, 19);

            var bb = BtnRow(52f, 21, "", (Action)(() => { mir = !mir; Put("mir", mir ? 1 : 0); Refresh(); }),
                                     "", (Action)(() => { flip = !flip; Put("flip", flip ? 1 : 0); Refresh(); }));
            var t0 = bb[0].GetComponentInChildren<Text>();
            var t1 = bb[1].GetComponentInChildren<Text>();
            var b0 = bb[0]; var b1 = bb[1];
            refreshers.Add(() =>
            {
                t0.text = Ar6dofLoc.T(mir ? "Effet miroir : oui" : "Effet miroir : non");
                t1.text = Ar6dofLoc.T(flip ? "Retourne : oui" : "Retourne : non");
                Ar6dofTheme.SetSelected(b0, mir);
                Ar6dofTheme.SetSelected(b1, flip);
            });

            Lbl("Taille", 30, 20).MakeAccent();
            Tabs(new[] { "Petit", "Moyen", "Grand" }, size, i => { size = i; Put("size", i); });
            Para("Pour regler la camera (source, image, vision nocturne, LED...) : Reglages > Image > Cameras. La camera s'arrete quand cette fenetre est rangee.", 58, 17);
        }

        protected override void OnTick()
        {
            if (Hub == null) return;
            // la camera choisie a ete desactivee ou supprimee : on passe a une autre (ou au message d'aide)
            if (cur != null && (!Hub.cams.Contains(cur) || !cur.enabled))
            {
                Detach();
                cur = null;
                Ensure();
                Attach();
                Rebuild();
                return;
            }
            if (cur == null && Avail().Count > 0) { Ensure(); Attach(); Rebuild(); return; }
            if (img == null || cur == null) return;
            if (feed == null) { Attach(); return; }

            if (status != null) status.text = Hub.StatusOf(cur);

            if (!feed.Ready || feed.Tex == null) { img.color = Color.clear; return; }
            // l'image n'a pas le format prevu : on refait la fenetre une fois
            if (Time.unscaledTime - lastAspectCheck > 0.5f)
            {
                lastAspectCheck = Time.unscaledTime;
                float a = AspectNow();
                if (Mathf.Abs(a / builtAspect - 1f) > 0.03f) { Rebuild(); return; }
            }
            img.texture = feed.Tex;
            // vision nocturne : teinte verte sur l'image grise ; sinon image telle quelle
            img.color = cur.role == Ar6dofCamRole.Night && cur.tint == 0 ? new Color(0.45f, 1f, 0.5f, 1f) : Color.white;

            bool dispH = mir ^ cur.mirrorX, dispV = flip ^ cur.mirrorY;
            bool odd = cur.rotate % 2 == 1;
            bool rawH = odd ? dispV : dispH, rawV = odd ? dispH : dispV;
            rawV ^= feed.FlipY;
            float ux = 0f, uw = 1f, uy = 0f, uh = 1f;
            if (cur.stereo == 1) uw = 0.5f;
            else if (cur.stereo == 2) { uy = 0.5f; uh = 0.5f; }   // moitié haute = oeil gauche
            if (rawH) { ux += uw; uw = -uw; }
            if (rawV) { uy += uh; uh = -uh; }
            img.uvRect = new Rect(ux, uy, uw, uh);
        }
    }
}
