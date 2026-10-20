# Unity 6 : casque AR (passthrough + menu flottant) piloté par ar6dof_probe

## Architecture
- `ar6dof_probe.exe` ouvre la caméra (seul à pouvoir le faire), calcule la pose 6DoF et envoie :
  - la **pose** par UDP (port 9876), publiée à la cadence de l'IMU (~200 Hz) et non plus de la caméra ;
  - **l'image brute** par mémoire partagée Windows (`Local\ar6dof_frame`) ;
  - le **modèle de caméra** (`camera_model.json`, écrit à côté de l'exe).
- Unity corrige l'image sur le GPU (distorsion de la caméra, des lentilles, IPD, miroir, rotation, échange G/D),
  rend le menu 3D pour chaque oeil, et compose les deux dans une seule fenêtre (moitié gauche = oeil gauche).

## 1. Installer
Copie `Assets/AR6DoF` dans le dossier `Assets` de ton projet Unity 6 (URP ou pipeline intégré).
Copie `camera_model.json` (écrit par ar6dof_probe à côté de son exe) dans ton projet pour pouvoir le glisser dans l'inspecteur.
(`camera_model_example.json` est un exemple généré avec ta calibration et une base de 60 mm.)

## 2. Monter la scène
1. GameObject vide **Rig** avec : `Ar6dofReceiver`, `Ar6dofHeadRig` (champ Receiver), `Ar6dofPassthrough`.
2. Dans `Ar6dofPassthrough` : glisse `camera_model.json` dans *Camera Model Json* et le fichier `Ar6dofComposite.shader`
   dans *Composite Shader* (nécessaire pour qu'il soit inclus dans le build).
3. Supprime la Main Camera : le casque crée ses propres caméras (une par oeil).
4. Menu : Canvas en **World Space** (échelle ~0.001), sur le calque *Default* ou *UI* (pas *TransparentFX*), puis `Ar6dofFloatingMenu`
   avec le champ *Passthrough* renseigné.
5. Un sol visible à y = 0 pour vérifier l'ancrage.
6. Fenêtre Game de l'éditeur : résolution fixe **2880 x 1440**.

## 3. Lancer (une seule fois à la main, puis tout est automatique)
**Première fois** : lance `ar6dof_probe.exe` normalement, réponds aux questions, fais `g` puis `f` (montage de l'IMU) puis `t`, et quitte avec `q`.
Tes réponses sont sauvegardées dans `ar6dof_config.txt`.

**Ensuite** : ajoute `Ar6dofLauncher` sur un objet de la scène (champ *Exe Path* = chemin d'`ar6dof_probe.exe`, ou place le dossier `ar6dof_probe-win64` à côté de `Assets`).
Au Play, Unity lance ar6dof_probe tout seul (sans fenêtre, tracking démarré si tu l'avais choisi) et l'arrête à la fermeture.

(Procédure manuelle d'origine :)
1. `ar6dof_probe.exe` : port UDP 9876, **partage d'image = 1**, puis `g`, `f`, et `t`.
2. Play dans Unity. **F1** ouvre les réglages. Le haut de l'écran affiche le débit d'images et l'âge de l'image.
3. Pour le casque (build) : lance l'exe Unity avec  
   `-monitor 2 -screen-fullscreen 1 -screen-width 2880 -screen-height 1440`  
   (remplace 2 par le numéro de l'écran du casque).

## Réglages (F1)
| Réglage | Effet |
|---|---|
| FOV affiché | champ de vision de chaque oeil. À accorder à ton optique |
| IPD logiciel | écarte (+) ou rapproche (-) les deux images |
| Décalage X / Y | déplace l'image dans chaque oeil (flux rectangulaire sur écran carré) |
| Lentille k1 / k2 / k3, centre | pré-déformation pour annuler la distorsion des lentilles de l'afficheur |
| Echanger gauche / droite | si les yeux de la caméra sont inversés |
| Miroir X / Y, rotation | corrige une caméra montée de travers ou retournée |
| Côte à côte / haut-bas | disposition du flux stéréo |
| Corriger la distorsion de la caméra | utilise `camera_model.json` (désactivé : champ supposé) |
| Inverser verticalement le rendu | si tout est à l'envers |
| Reprojection de rotation | l'image de la caméra suit la tête à la cadence d'Unity même si la caméra est plus lente (comme le fait un Quest) |
| Délai capture -> écriture | à régler en tournant la tête vite : si l'image « traîne » derrière le menu, augmente ; si elle le devance, diminue |
| Etirer les bords | au lieu de laisser du noir quand la reprojection sort de l'image |

Les réglages sont sauvegardés (bouton *Sauver*) et rechargés au lancement.

## Menu (panneau F1, à droite)
Case **Suivi automatique** : décochée, le menu est FIXE dans la pièce ; cochée, il te suit quand tu t'éloignes.
Le bouton *Recentrer* (ou M) le replace devant toi. Le panneau affiche aussi ta position et la distance au menu (en cm) pour comparer avec une règle.

## Touches
R recentrer · M rappeler le menu · F suivi automatique du menu · F1 réglages

## Conventions et limites
- Pose : X avant, Y gauche, Z haut (main droite) -> Unity (conversion dans `Ar6dofConvert`, vérifiée numériquement).
- Les caméras virtuelles ont l'écart de ta caméra (base de la calibration), pour que le menu soit cohérent avec l'image.
  L'IPD logiciel ne fait que décaler les images.
- La reprojection compense la ROTATION de la tête. Les déplacements (translation) ne sont pas compensés : l'image garde la parallaxe de sa prise de vue.
- La scène 3D est rendue avec une marge (*Scene Overscan*, 1.3 par défaut) : le décalage IPD ne rogne plus le menu.
- Zoom : un FOV affiché plus petit que celui de la caméra laisse de la marge à la reprojection.
- Flux unique côte à côte (ou haut-bas) uniquement. Deux caméras séparées : il faut d'abord les assembler en un seul flux.
- Pas de réglage automatique des lentilles : k1/k2/k3 et le FOV se règlent à l'oeil.


---

# Nouveautés : fenêtres Windows, clavier, télécommande, écran, délai automatique

## Écran du casque
Ajoute `Ar6dofDisplayStarter` sur un objet de la scène. Dans un build, il place la fenêtre du jeu, sans bordure, sur l'écran de
2880x1440 (le casque), même si Windows l'avait ouverte minimisée ou sur l'écran principal. Réglages conseillés
(Project Settings > Player) : *Fullscreen Mode = Windowed*, *Run In Background* coché.

## Délai de la caméra (reprojection)
Le délai entre l'arrivée d'une image et sa prise de vue est **mesuré automatiquement** par ar6dof_probe en comparant la rotation
vue par la caméra à celle du gyro. Il faut tourner la tête quelques secondes au démarrage. Le panneau F1 affiche « delai X ms ».
Les touches `[` et `]` de ar6dof_probe (ou le curseur manuel de F1) le règlent à la main ; `a` dans ar6dof_probe revient à l'automatique.

## Télécommande gyroscopique Bluetooth
1. Flashe `firmware/ar6dof_remote/ar6dof_remote.ino` (ESP32-C3 + BNO085 + 2 boutons). Aucun réglage : pas de WiFi, pas de mot de passe.
2. Active le Bluetooth du PC. **Ne l'appaire pas** dans les paramètres Windows : c'est inutile.
3. Lance ar6dof_probe.exe (via le lanceur Unity ou à la main) : il cherche tout seul « AR6DoF-Remote », s'y connecte
   et se reconnecte si elle est éteinte puis rallumée. Dans le journal : `[ble] telecommande connectee`.
   (`--no-ble` désactive cette écoute.) Le Bluetooth passe par ar6dof_probe : la télécommande ne marche que s'il tourne.
4. Dans Unity : un objet avec `Ar6dofRemote` (port 9878, ne pas changer) et `Ar6dofPointer` (liste *Canvases* = tes menus en World Space).
5. Panneau F1 > Télécommande : *Calibration 1/2* (à plat, immobile) puis *2/2* (pointe relevée de ~30°, immobile). Une seule fois.
6. Recentrer : maintenir les 2 boutons 0,6 s en pointant devant toi. Bouton gauche = clic gauche, bouton droit = clic droit.

## Écrans virtuels (remplace uWindowCapture)
**Tu peux supprimer uWindowCapture** : plus rien n'en dépend. La capture est faite par `ar6dof_screen.dll` (construite en même temps que
ar6dof_probe.exe par GitHub) : DXGI Desktop Duplication, GPU vers GPU, aucune lecture CPU, et aucun travail quand le bureau ne change pas.
1. Copie `ar6dof_screen.dll` dans `Assets/AR6DoF/Plugins/x86_64/` (Unity : sélectionne la DLL, coche *Windows x86_64*).
2. Ajoute `Ar6dofScreen.shader` à *Project Settings > Graphics > Always Included Shaders*, et mets-le dans le champ *Screen Shader* de
   `Ar6dofScreens` (un objet vide de la scène, HORS du Rig). Renseigne aussi *Passthrough* et *Pointer*.
3. **Écran dans l'écran** : quand le casque sera ton seul écran, installe un pilote d'écran virtuel Windows (ex. *Virtual Display Driver*,
   open source signé) réglé en 1920x1080 / 60 Hz : Windows le voit comme un 2e moniteur, tu y glisses tes fenêtres, il est
   capturé et cliquable, et Unity n'est jamais dessus. De plus, `Ar6dofDisplayStarter` exclut la fenêtre du casque de toute capture
   (*Exclude From Capture*), donc même si tu captures l'écran du casque, il n'y a pas d'effet miroir.
4. Dans le casque : barre sous chaque écran = déplacer (bouton **gauche** : tout le groupe collé ; bouton **droit** : cet écran seul, il se
   décolle) ; **coins** = agrandir / réduire cet écran seulement ; bouton **Param.** = source (moniteur), images/s (30, 60, 90), coller aux
   bords, courbure entre écrans, ajouter (3 maximum) / retirer / dissocier, recentrer. Deux bords proches se collent au relâchement.
5. Le clavier virtuel (K / B) se place centré sous l'ensemble des écrans. La disposition est mémorisée.
6. Si l'image est à l'envers : décocher *Flip Y* sur `Ar6dofScreens`. Si les clics tombent à côté : mettre le moniteur capturé à 100 % de mise à l'échelle.
Limites : bureau en SDR (pas HDR), même carte graphique pour Unity et le moniteur capturé, le curseur Windows n'est pas dessiné (le laser sert de curseur).

## Clavier virtuel
Un objet vide avec `Ar6dofKeyboard`. **K** : afficher/cacher, **B** : replacer sous ta vue. Maj, Ctrl et Alt restent actives pour la
touche suivante (Ctrl puis C = copier). Les lettres sont envoyées en Unicode : AZERTY ou QWERTY sans dépendre du clavier de Windows.
Les touches de direction et PgPr/PgSv permettent de faire défiler.
