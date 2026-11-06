using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Memes : l'application est la, mais vide pour l'instant (les premiers memes ont ete retires, d'autres arriveront). Rien n'existe ni ne tourne tant que la fenetre n'est pas ouverte.
    /// </summary>
    public class Ar6dofMemesApp : Ar6dofAppWindow
    {
        public Ar6dofMemesApp(Ar6dofDock d) : base(d, "memes", "Memes", "memes", "Aucun meme pour le moment : de nouveaux arrivent.", 560f) { }

        protected override void BuildMain()
        {
            Space(10f);
            Para("Aucun meme pour le moment. De nouveaux memes a poser dans le monde arriveront bientot.", 70, 22);
        }
    }
}
