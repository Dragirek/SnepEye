using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Le dos d'un menu est vide : vu de derriere, un Canvas Unity UI s'affiche normalement a l'envers (miroir). Ce composant le rend
    /// invisible et non cliquable quand la tete est derriere lui. Un seul test de produit scalaire par image et par menu ouvert ;
    /// un menu ferme ou masque (objet inactif) ne coute rien. Une petite marge evite le scintillement quand on passe dans son plan.
    /// </summary>
    [DisallowMultipleComponent]
    public class Ar6dofBackFace : MonoBehaviour
    {
        CanvasGroup group;
        bool hidden;
        float savedAlpha = 1f;
        bool savedBlocks = true;

        /// <summary>Vrai si 'viewPos' est derriere le menu (cote du dos). La face visible est celle qu'on voit en regardant vers +Z du menu.</summary>
        public static bool IsBehind(Transform menu, Vector3 viewPos, float margin = 0.02f)
        {
            return Vector3.Dot(menu.forward, viewPos - menu.position) > margin;
        }

        void OnEnable() { hidden = false; }

        void OnDisable() { Show(); }

        void LateUpdate()
        {
            var head = Ar6dofVrUi.ViewHead;
            if (head == null) return;
            float d = Vector3.Dot(transform.forward, head.position - transform.position);
            if (!hidden && d > 0.02f) Hide();
            else if (hidden && d < -0.02f) Show();
        }

        void Hide()
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            savedAlpha = group.alpha; savedBlocks = group.blocksRaycasts;
            group.alpha = 0f;
            group.blocksRaycasts = false;
            hidden = true;
        }

        void Show()
        {
            if (!hidden) return;
            hidden = false;
            if (group == null) return;
            group.alpha = savedAlpha;
            group.blocksRaycasts = savedBlocks;
        }
    }
}
