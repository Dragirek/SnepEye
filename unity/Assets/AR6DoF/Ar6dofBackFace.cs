using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Le dos d'un menu est vide : vu de derriere, un Canvas Unity UI s'affiche normalement a l'envers (miroir). Ce composant le rend
    /// invisible et non cliquable quand la tete est derriere lui. Un seul test de produit scalaire par image et par menu ouvert ;
    /// un menu ferme ou masque (objet inactif) ne coute rien. Une petite marge evite le scintillement quand on passe dans son plan.
    ///
    /// C'est aussi le seul proprietaire du CanvasGroup du menu : il combine l'opacite imposee par un proprietaire exterieur (Alpha : le panneau des
    /// notifications suit celle du dock) et le fondu « cache par le dock » (SetFaded), sans que ces trois usages se marchent dessus.
    /// </summary>
    [DisallowMultipleComponent]
    public class Ar6dofBackFace : MonoBehaviour
    {
        const float FadeSeconds = 0.3f;

        CanvasGroup group;
        Canvas cv;
        bool hidden;
        float fade = 1f, fadeTarget = 1f;     // fondu « cache par le dock » : 1 = visible, 0 = efface
        float alpha = 1f;                      // opacite imposee de l'exterieur
        bool ownsCanvas;                       // vrai tant qu'un fondu a coupe ou doit rallumer le Canvas

        /// <summary>Vrai si 'viewPos' est derriere le menu (cote du dos). La face visible est celle qu'on voit en regardant vers +Z du menu.</summary>
        public static bool IsBehind(Transform menu, Vector3 viewPos, float margin = 0.02f)
        {
            return Vector3.Dot(menu.forward, viewPos - menu.position) > margin;
        }

        /// <summary>Opacite imposee par un proprietaire exterieur (0..1), multipliee par le fondu. 1 = sans effet.</summary>
        public float Alpha
        {
            get { return alpha; }
            set { value = Mathf.Clamp01(value); if (Mathf.Abs(value - alpha) < 0.002f) return; alpha = value; Apply(); }
        }

        /// <summary>Fond le menu jusqu'a le rendre invisible et inerte (faux : il revient). Le Canvas est coupe a la fin du fondu et rallume au retour.</summary>
        public void SetFaded(bool faded)
        {
            fadeTarget = faded ? 0f : 1f;
            ownsCanvas = true;
            if (!faded) EnableCanvas(true);
            Apply();
        }

        /// <summary>Vrai si le menu est efface (ou en train de l'etre).</summary>
        public bool Faded { get { return fadeTarget < 0.5f; } }

        void EnableCanvas(bool on)
        {
            if (cv == null) cv = GetComponent<Canvas>();
            if (cv != null) cv.enabled = on;
        }

        void OnEnable() { hidden = false; }

        void OnDisable() { hidden = false; if (group != null) Apply(); }

        void LateUpdate()
        {
            var head = Ar6dofVrUi.ViewHead;
            if (head != null)
            {
                float d = Vector3.Dot(transform.forward, head.position - transform.position);
                if (!hidden && d > 0.02f) { hidden = true; Apply(); }
                else if (hidden && d < -0.02f) { hidden = false; Apply(); }
            }
            if (fade != fadeTarget)
            {
                fade = Mathf.MoveTowards(fade, fadeTarget, Time.unscaledDeltaTime / FadeSeconds);
                Apply();
            }
        }

        void Apply()
        {
            bool neutral = !hidden && fade >= 1f && fadeTarget >= 1f && alpha >= 0.998f;
            if (neutral && group == null)
            {
                if (ownsCanvas) { ownsCanvas = false; EnableCanvas(true); }
                return;
            }
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = hidden ? 0f : fade * alpha;
            group.blocksRaycasts = !hidden && fadeTarget > 0.5f && alpha > 0.05f;
            if (ownsCanvas)
            {
                if (fade <= 0.003f && fadeTarget <= 0f) EnableCanvas(false);            // efface : plus dessine ni vise
                else if (fade >= 1f && fadeTarget >= 1f) { ownsCanvas = false; EnableCanvas(true); }
                else EnableCanvas(true);
            }
        }
    }
}
