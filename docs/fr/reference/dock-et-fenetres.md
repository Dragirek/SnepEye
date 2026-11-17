[← Retour à l'index](../index.md)

# Dock, placement des fenêtres, dessins par code

- *Réglages > Dock > Position* : **« Assis : dock sous les yeux de (cm) »** (85 cm par défaut, ancien comportement : 55 cm). Le dock assis est plus bas et apparaît en baissant moins la tête.
- Les fenêtres (applications, réglages, menus d'appareils) se placent dans un **endroit libre** : devant toi d'abord, puis de plus en plus sur les côtés, en évitant les écrans et les fenêtres déjà ouvertes ; derrière toi seulement s'il n'y a plus de place. *Menus ici* les replace de la même façon.

## Dessins par code (cadrans, visuels du son, icônes météo, courbes)
Les maillages de `Graphic` personnalisés ne s'affichaient pas dans les menus VR (cadrans de l'Horloge, visuels de la table de mixage, ciel et icônes de la Météo restaient invisibles ; cause non identifiée). Ces dessins passent maintenant par `Ar6dofRasterGfx` (un `RawImage`) : le dessin est calculé en maillage virtuel puis rempli triangle par triangle dans une texture (sur-échantillonnage 2x pour lisser les bords, 0,5x pour le ciel), et redessiné seulement quand il change.

## Menus, jeux et réglages sensibles (v82–v86)
- **Texte qui dépasse** : tous les textes des menus se réduisent tout seuls pour tenir dans leur cadre (jusqu'à environ la moitié de leur taille), sans coût tant que rien ne change.
- **Boutons − et +** : à côté des curseurs de réglage (IPD, contrôle précis...), un appui change d'un seul cran, sans avoir à viser le curseur.
- **Jeu et menu liés** : en 3D, le plateau (ou les dés, la machine à sous) est posé **au-dessus** de la fenêtre du jeu, tourné comme elle et à la même échelle : déplacer, réduire ou agrandir la fenêtre avec sa barre du bas fait suivre le jeu. Il n'y a plus de socle à saisir. À l'ouverture, la fenêtre est **baissée de la hauteur du jeu** : l'ensemble menu + jeu est placé comme un menu ordinaire (le haut de l'ensemble juste au-dessus des yeux). **Cacher le menu** (jeux, objets 3D, DVD) réduit la fenêtre à un petit bouton « Montrer le menu » ; le jeu reste.
- **Sons de l'interface** : Paramètres > Interface > Sons (volume, style Doux / Cristal / Rétro, quels événements). Les sons sont fabriqués par le code (aucun fichier audio) et ne coûtent rien quand ils sont désactivés.

- **Menus, écrans et clavier près du dock** : quand le dock apparaît (tête baissée), tout ce qui est ouvert à moins de **1 m** de lui (menus, **écrans virtuels**, clavier) s'efface **en fondu** pour ne pas se superposer, et revient en fondu quand le dock s'efface. Rien n'est fermé ; toucher l'icône dans le dock fait revenir l'élément tout de suite. Un élément masqué n'est ni dessiné ni visé par le laser (un écran masqué n'est plus capturé). Le clavier se rétracte en s'effaçant. Sans suivi (dock toujours visible), rien n'est caché ; ce qui est ouvert pendant que le dock est affiché n'est pas touché.
- **DVD** : l'écran est **lié au menu** comme un jeu 3D : il est posé juste au-dessus, dans la même direction (un grand écran reste à bonne distance), et le suit quand tu déplaces la fenêtre avec sa barre du bas ; le viser au laser déplace aussi la fenêtre. Le menu est compact : un bouton *Écran* qui change de type (plat, ultra-large, incurvé, ancien, portrait), *Retirer l'écran*, un curseur *Largeur*, le logo, la vitesse, la taille, la couleur et le son. Réduire la fenêtre garde l'écran dans le monde (immobile), la fermer (croix, ou retoucher son icône du Hub) le retire.
- **Son** : dans les jeux et le DVD, l'interrupteur de son est un bouton à icône (haut-parleur / haut-parleur barré).
- **Notifications** : le panneau fait partie du dock (pas de barre de fenêtre) : il est posé **dans le plan du dock**, contre le bord haut de la barre, avec la même inclinaison et la même échelle, et le suit à chaque image ; il reste donc dans ton champ de vision tant que le dock y est, et s'efface en fondu avec lui.
- **Clavier** : voir ci-dessus ; il se retire en fondu quand le dock le recouvrirait (à moins de 1 m) et revient avec le dock, ou dès que tu touches son icône ou un champ de texte.
