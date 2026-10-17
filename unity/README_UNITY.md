# Unity 6 : casque AR piloté par ar6dof_probe

## 1. Installer
Copie le dossier `Assets/AR6DoF` dans le dossier `Assets` de ton projet Unity 6.
Aucun package supplémentaire n'est nécessaire (les deux systèmes d'entrée, ancien ou nouveau, sont gérés).

## 2. Monter la scène
1. Crée un GameObject vide **Rig**. Ajoute-lui les composants :
   - `Ar6dofReceiver` (port 9876, le même que dans ar6dof_probe)
   - `Ar6dofHeadRig` (glisse le Receiver dans le champ *Receiver*)
2. Mets la **Main Camera** en enfant de Rig, position (0,0,0) et rotation (0,0,0).
   Pour un rendu côte à côte (deux yeux), ajoute `Ar6dofStereoRig` sur Rig et désactive la Main Camera.
3. Crée un Canvas en **World Space** (échelle ~0.001, taille ~1000x600) avec tes boutons, puis ajoute
   `Ar6dofFloatingMenu` sur ce Canvas (glisse la caméra dans *Head* et le Rig dans *Rig*).
4. (Optionnel) `Ar6dofStatusHud` sur n'importe quel objet : affiche l'état dans le coin de l'écran.
5. Ajoute un sol visible (Plane à y = 0) : le sol réel est y = 0 dans Unity.

## 3. Lancer
1. Lance `ar6dof_probe.exe` (port UDP 9876), fais `g` puis `f` (montage de l'IMU), pointe vers une scène riche en détails et appuie sur `t`.
2. Regarde un peu le sol (2 à 4 m devant toi) : la fenêtre "trajectoire" affiche « sol à ... ». `v` verrouille le sol détecté.
3. Lance Play dans Unity.

## Touches dans Unity
| Touche | Action |
|---|---|
| R | recentrer : l'origine et l'avant deviennent ta position et ton regard actuels |
| M | faire apparaître le menu devant toi, maintenant |
| F | activer / désactiver le suivi automatique du menu |

## Conventions
- Paquet UDP de 68 octets, décrit dans `src/pose_packet.h`.
- Repère du tracker : X avant, Y gauche, Z haut (main droite). Conversion vers Unity (X droite, Y haut, Z avant) dans `Ar6dofConvert`.
- Sans sol détecté, Unity suppose une hauteur d'yeux de 1,65 m au recentrage (réglable).

## Limites actuelles
- Le rendu stéréo est basique : pas de correction de distorsion des lentilles, FOV et IPD à régler à la main.
- Pas encore de vidéo des caméras (passthrough) dans Unity.
