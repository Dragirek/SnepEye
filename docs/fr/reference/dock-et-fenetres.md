[← Retour à l'index](../index.md)

# Dock, placement des fenêtres, dessins par code

- *Réglages > Dock > Position* : **« Assis : dock sous les yeux de (cm) »** (85 cm par défaut, ancien comportement : 55 cm). Le dock assis est plus bas et apparaît en baissant moins la tête.
- Les fenêtres (applications, réglages, menus d'appareils) se placent dans un **endroit libre** : devant toi d'abord, puis de plus en plus sur les côtés, en évitant les écrans et les fenêtres déjà ouvertes ; derrière toi seulement s'il n'y a plus de place. *Menus ici* les replace de la même façon.

## Dessins par code (cadrans, visuels du son, icônes météo, courbes)
Les maillages de `Graphic` personnalisés ne s'affichaient pas dans les menus VR (cadrans de l'Horloge, visuels de la table de mixage, ciel et icônes de la Météo restaient invisibles ; cause non identifiée). Ces dessins passent maintenant par `Ar6dofRasterGfx` (un `RawImage`) : le dessin est calculé en maillage virtuel puis rempli triangle par triangle dans une texture (sur-échantillonnage 2x pour lisser les bords, 0,5x pour le ciel), et redessiné seulement quand il change.
