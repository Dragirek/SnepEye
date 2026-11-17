[← Retour à l'index](../index.md)

# Dock, placement des fenêtres, dessins par code

- *Réglages > Dock > Position* : **« Assis : dock sous les yeux de (cm) »** (85 cm par défaut, ancien comportement : 55 cm). Le dock assis est plus bas et apparaît en baissant moins la tête.
- Les fenêtres (applications, réglages, menus d'appareils) se placent dans un **endroit libre** : devant toi d'abord, puis de plus en plus sur les côtés, en évitant les écrans et les fenêtres déjà ouvertes ; derrière toi seulement s'il n'y a plus de place. *Menus ici* les replace de la même façon.

## Dessins par code (cadrans, visuels du son, icônes météo, courbes)
Les maillages de `Graphic` personnalisés ne s'affichaient pas dans les menus VR (cadrans de l'Horloge, visuels de la table de mixage, ciel et icônes de la Météo restaient invisibles ; cause non identifiée). Ces dessins passent maintenant par `Ar6dofRasterGfx` (un `RawImage`) : le dessin est calculé en maillage virtuel puis rempli triangle par triangle dans une texture (sur-échantillonnage 2x pour lisser les bords, 0,5x pour le ciel), et redessiné seulement quand il change.

## Menus, jeux et réglages sensibles (v82–v86)
- **Texte qui dépasse** : tous les textes des menus se réduisent tout seuls pour tenir dans leur cadre (jusqu'à environ la moitié de leur taille), sans coût tant que rien ne change.
- **Boutons − et +** : à côté des curseurs de réglage (IPD, contrôle précis...), un appui change d'un seul cran, sans avoir à viser le curseur.
- **Jeu et menu liés** : en 3D, le plateau (ou les dés, la machine à sous) est posé **au-dessus** de la fenêtre du jeu, tourné comme elle et à la même échelle : déplacer, réduire ou agrandir la fenêtre avec sa barre du bas fait suivre le jeu. Il n'y a plus de socle à saisir. **Cacher le menu** (jeux, objets 3D, DVD) réduit la fenêtre à un petit bouton « Montrer le menu » ; le jeu reste.
- **Sons de l'interface** : Paramètres > Interface > Sons (volume, style Doux / Cristal / Rétro, quels événements). Les sons sont fabriqués par le code (aucun fichier audio) et ne coûtent rien quand ils sont désactivés.

- **Menus près du dock** : quand le dock apparaît (tête baissée), les menus ouverts à moins de 50 cm disparaissent (et le clavier, voir plus bas) pour ne pas se superposer ; ils reviennent quand le dock s'efface. Ils ne sont pas fermés ; toucher leur icône dans le dock les fait revenir tout de suite. Sans suivi (dock toujours visible), rien n'est caché ; un menu ouvert pendant que le dock est affiché n'est pas touché.
- **DVD** : réduire la fenêtre garde l'écran dans le monde, la fermer (croix, ou retoucher son icône du Hub) le retire.
- **Son** : dans les jeux et le DVD, l'interrupteur de son est un bouton à icône (haut-parleur / haut-parleur barré).
- **Notifications** : le panneau fait partie du dock (pas de barre de fenêtre) : il reste posé au-dessus de lui, le suit et s'efface avec lui.
- **Clavier** : comme les menus, il est caché quand le dock apparaît et le recouvrirait (à moins de 50 cm) ; il revient avec le dock, ou dès que tu touches son icône ou un champ de texte.
