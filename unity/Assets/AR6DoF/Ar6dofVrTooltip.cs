using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Infobulle au survol (laser pointé dessus) d'un élément de menu VR. À mettre sur l'objet qui porte l'Image cliquable.</summary>
    public class Ar6dofVrTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> text;
        public float offsetY = 82f;
        GameObject tip;
        Text label;

        public void OnPointerEnter(PointerEventData e)
        {
            if (tip == null)
            {
                var rt = Ar6dofVrUi.Rect(transform, "Infobulle", new Vector2(0f, offsetY), new Vector2(300f, 40f));
                var bg = rt.gameObject.AddComponent<Image>();
                bg.raycastTarget = false;
                Ar6dofTheme.Attach(rt.gameObject, Ar6dofRole.Surface);
                label = Ar6dofVrUi.Label(rt, "", Vector2.zero, new Vector2(300f, 40f), 22);
                tip = rt.gameObject;
            }
            string s = text != null ? text() : "";
            label.text = Ar6dofLoc.T(s);
            ((RectTransform)tip.transform).sizeDelta = new Vector2(Mathf.Max(120f, label.text.Length * 14f + 30f), 40f);
            tip.SetActive(s.Length > 0);
        }

        public void OnPointerExit(PointerEventData e) { if (tip != null) tip.SetActive(false); }
    }
}
