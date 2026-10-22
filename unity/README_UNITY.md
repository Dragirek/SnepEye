# Unity 6 : installation de A à Z

Tout le casque (passthrough, écrans virtuels, clavier, barre de tâches, télécommande, menus Bluetooth, eye tracking) tient dans **un seul dossier** :
`Assets/AR6DoF` + **un programme** : `ar6dof_probe.exe`. Ce guide part d'une scène vide ; tu n'as pas besoin de ton ancien montage.

---

## 1. Comment les morceaux se parlent

```
 caméra ELP ─┐                                  ┌─ pose 6DoF (UDP 9876) ───────────┐
 IMU BNO085 ─┼─► ar6dof_probe.exe ──────────────┼─ image caméra (mémoire partagée) ┼─► Unity
 télécommande BLE ┤  (seul à parler à la caméra, ├─ télécommande (UDP 9878)         │   (passthrough, écrans,
 appareils BLE ───┤   au Bluetooth, à l'IMU)     ├─ appareils Bluetooth (9881/9882) │    barre de tâches VR)
 caméras d'oeil ──┘                              └─ eye tracking (9883/9884/9885) ──┘
```
Unity lance `ar6dof_probe.exe` tout seul (composant **Launcher**) et l'arrête à la fermeture.

---

## 2. Ce qu'il faut avoir sous la main

| Fichier | D'où il vient | Où le mettre |
|---|---|---|
| dossier `AR6DoF` | ce dépôt, `unity/Assets/AR6DoF` | dans `Assets` de ton projet |
| `ar6dof_probe.exe` + ses DLL + `ar6dof_screen.dll` | build GitHub (onglet *Actions* > artefact) | un dossier `ar6dof_probe-win64`, **à côté** du dossier `Assets` (pas dedans) |
| `ar6dof_screen.dll` | idem | **aussi** copiée dans `Assets/AR6DoF/Plugins/x86_64/` (crée le dossier ; clique la DLL, coche *Windows x86_64*) |
| `camera_model.json` | écrit par ar6dof_probe à côté de son exe au premier lancement | copié dans `Assets` |

Projet Unity 6, URP ou pipeline intégré. *Project Settings > Player > Active Input Handling* : « Both » ou « Input System Package » ou « Input Manager », au choix.

---

## 3. Première utilisation d'ar6dof_probe (une seule fois, à la main)
Lance `ar6dof_probe.exe` (double-clic). Il pose des questions (caméra, port COM de l'IMU, 3200x1200, 60 fps, calibration, base 60 mm...).
Presse Entrée pour garder les valeurs proposées. Ensuite : `g` puis `f` (montage de l'IMU), `t` (tracking), `q` pour quitter.
Tes réponses sont gardées dans `ar6dof_config.txt` ; Unity les rejoue ensuite sans rien demander.

> Question *Eye tracking* : réponds **0** si tu n'as pas les caméras d'oeil. Rien ne tourne alors.

---

## 4. Monter la scène

### Méthode A : en un clic (recommandée)
1. Crée une **scène vide** (*File > New Scene > Basic*), enregistre-la.
2. Menu **AR6DoF > Creer la scene complete**.
3. Une fenêtre liste ce qu'il te reste à faire (en général : renseigner *Exe Path* du Launcher, glisser `camera_model.json` si besoin).

C'est tout : les objets, les composants et tous les champs sont remplis. Le script supprime aussi la *Main Camera* et ajoute les shaders aux *Always Included Shaders*.

### Méthode B : à la main
Crée ces objets **vides** (clic droit > *Create Empty*), tous **à la racine** de la scène, tous en position 0,0,0, avec **exactement ces noms** (le nom n'a pas d'importance technique, c'est juste pour t'y retrouver) :

```
Scène
├── Rig              ← Ar6dofReceiver + Ar6dofHeadRig + Ar6dofPassthrough
│    ├── EyeLeft     (créé tout seul au Play, ne le crée pas)
│    └── EyeRight    (créé tout seul au Play)
├── Pointer          ← Ar6dofRemote + Ar6dofPointer
├── Launcher         ← Ar6dofLauncher
├── Screens          ← Ar6dofScreens
├── Keyboard         ← Ar6dofKeyboard
├── Dock             ← Ar6dofDock
├── DisplayStarter   ← Ar6dofDisplayStarter
└── StatusHud        ← Ar6dofStatusHud   (facultatif : ligne de diagnostic en haut de l'écran)
```
Ajoute chaque composant avec *Add Component* (tape son nom). Puis remplis les champs ainsi (tout ce qui n'est pas listé reste par défaut ou vide) :

| Objet | Composant | Champ à remplir | Avec quoi |
|---|---|---|---|
| Rig | Ar6dofHeadRig | Receiver | le composant Ar6dofReceiver du même objet (glisse-le) |
| Rig | Ar6dofPassthrough | Receiver | idem |
| | | Head Rig | le composant Ar6dofHeadRig du même objet |
| | | Camera Model Json | `camera_model.json` |
| | | Composite Shader | `Ar6dofComposite.shader` |
| Pointer | Ar6dofPointer | Remote | l'Ar6dofRemote du même objet |
| | | Passthrough | le composant Ar6dofPassthrough (objet Rig) |
| | | Canvases | **laisse vide** (les menus s'ajoutent tout seuls) |
| Launcher | Ar6dofLauncher | Exe Path | chemin complet d'`ar6dof_probe.exe`, ou laisse vide si le dossier `ar6dof_probe-win64` est à côté de `Assets` |
| Screens | Ar6dofScreens | Passthrough / Rig / Pointer | Passthrough (Rig), Ar6dofHeadRig (Rig), Ar6dofPointer (Pointer) |
| | | Screen Shader | `Ar6dofScreen.shader` |
| Keyboard | Ar6dofKeyboard | Passthrough, Key Shader | Passthrough (Rig), `Ar6dofFlat.shader` |
| Dock | Ar6dofDock | Passthrough, Rig, Pointer, Keyboard, Launcher | les composants correspondants (si tu en oublies, il les cherche seul) |
| StatusHud | Ar6dofStatusHud | Receiver, Rig | Ar6dofReceiver et Ar6dofHeadRig (objet Rig) |

Puis : **supprime la Main Camera**, et ajoute `Ar6dofComposite.shader`, `Ar6dofScreen.shader`, `Ar6dofFlat.shader` dans
*Project Settings > Graphics > Always Included Shaders* (indispensable pour les builds). Coche *Player > Resolution and Presentation > Run In Background*.

### Ce qu'il ne faut PAS ajouter (ou qu'il faut supprimer)
- **Canvas** : inutile. Il n'y a aucun Canvas à créer : tous les menus (barre de tâches, réglages, clavier, appareils Bluetooth, étalonnage des yeux) sont fabriqués par le code en « World Space » et branchés au laser tout seuls. Un Canvas de ton ancien montage ne sert à rien : supprime-le.
- **EventSystem** : créé automatiquement par le pointeur. Supprime le tien (ou garde-le, ça ne gêne pas).
- **Main Camera** : supprime-la. Le passthrough crée une caméra par oeil.
- **Ar6dofFloatingMenu, Ar6dofStereoRig, Ar6dofScreenPanel, Ar6dofBle, Ar6dofDeviceMenus, Ar6dofEyeTracker, Ar6dofEyeCalibration** : ajoutés automatiquement quand il le faut (ou obsolètes). Ne les ajoute pas.
- `Ar6dofWindowPanel.cs` et `Ar6dofWidgetMenus.cs` sont des fichiers vides d'anciennes versions : tu peux les supprimer. Idem pour toute copie « ...1.cs » ou « ... (1).cs » (erreur *already contains a definition*).
- **uWindowCapture** : plus utilisé, supprime-le.

---

## 5. Lancer

1. Unity : **Play**. Le Launcher démarre `ar6dof_probe.exe` (rien à faire), les caméras d'oeil du casque apparaissent, la barre de tâches se pose sous les écrans.
   Dans la Console, les lignes `[probe] ...` sont celles d'ar6dof_probe (journal complet : `ar6dof_probe.log` à côté de l'exe).
2. Fenêtre *Game* en **2880 x 1440** (*Free Aspect > +* ). Sur le casque (build) : crée un build Windows et lance-le avec  
   `-monitor 2 -screen-fullscreen 1 -screen-width 2880 -screen-height 1440` (2 = numéro de l'écran du casque). `Ar6dofDisplayStarter` place la fenêtre tout seul et l'exclut des captures d'écran.
   *Player > Fullscreen Mode = Windowed*, *Run In Background* coché.
3. Touches : **F1** réglages détaillés du passthrough · **R** recentrer · **B** replacer le clavier · **K** clavier on/off.

---

## 6. La barre de tâches VR (Dock)
Fixe dans la pièce, sous les écrans. Une icône par application : **écrans virtuels**, **clavier**, **tes menus d'appareils**, et la **roue** à droite (réglages).
- **Un clic** sur l'icône affiche l'application, **un second clic** la range. Rangée, elle ne consomme rien. Un trait sous l'icône = affichée.
- Pointer une icône affiche son nom ; une pastille verte / orange / rouge = connecté / en cours / déconnecté.
- **La roue** ouvre les réglages en VR : onglets *Passthrough* (FOV, IPD, délai, calibration, reprojection...), *Ecrans* (images/s, collage, courbure, ajouter un écran),
  *Systeme* (recentrer barre / clavier / tracking, verrouiller le sol, cap à zéro) et *Yeux* (eye tracking, voir plus bas).

### Réglages du passthrough (onglet Passthrough ou F1)
| Réglage | Effet |
|---|---|
| FOV affiché | champ de vision de chaque oeil. À accorder à ton optique |
| IPD logiciel | écarte (+) ou rapproche (-) les deux images |
| Décalage X / Y | déplace l'image dans chaque oeil |
| Lentille k1 / k2 / k3, centre | pré-déformation pour annuler la distorsion des lentilles de l'afficheur |
| Echanger gauche / droite | si les yeux de la caméra sont inversés |
| Miroir X / Y, rotation | corrige une caméra montée de travers ou retournée |
| Corriger la distorsion de la caméra | utilise `camera_model.json` |
| Inverser verticalement le rendu | si tout est à l'envers |
| Reprojection de rotation | l'image suit la tête à la cadence d'Unity même si la caméra est plus lente |
| Délai capture -> écriture | mesuré automatiquement (tourne la tête quelques secondes au démarrage) ; réglable à la main |
| Etirer les bords | au lieu de laisser du noir quand la reprojection sort de l'image |

Bouton *Sauver ces réglages* pour les garder.

---

## 7. Télécommande gyroscopique Bluetooth
1. Flashe `firmware/ar6dof_remote/ar6dof_remote.ino` sur l'ESP32-C3 (câblage et broches : en-tête du fichier). Pas de WiFi, pas de mot de passe.
2. Active le Bluetooth du PC. **Ne l'appaire pas** dans les paramètres Windows. Si « AR6DoF-Remote » y apparaît déjà (essais précédents), **supprime-le** de la liste.
3. Le Bluetooth passe par ar6dof_probe : il cherche « AR6DoF-Remote », se connecte, se reconnecte. Console : `[probe] [ble] telecommande connectee`.
4. Réglages > *Télécommande* (panneau F1) : *Calibration 1/2* (à plat, immobile) puis *2/2* (pointe relevée de ~30°, immobile). Une seule fois.
5. Recentrer : maintenir les 2 boutons 0,6 s en pointant devant toi. Bouton gauche = clic gauche, bouton droit = clic droit.

## 8. Écrans virtuels
La capture est faite par `ar6dof_screen.dll` (DXGI, GPU vers GPU, aucun travail quand le bureau ne change pas). Icône « écrans » de la barre.
- Barre sous un écran = déplacer (bouton gauche : tout le groupe ; bouton droit : cet écran seul). **Coins** = taille. Bouton **Param.** = moniteur capturé, images/s, collage, courbure, ajouter (3 max) / retirer.
- **Écran dans l'écran** : installe un pilote d'écran virtuel Windows (ex. *Virtual Display Driver*) en 1920x1080 / 60 Hz, glisse tes fenêtres dessus, c'est lui qu'on capture.
- Image à l'envers : décocher *Flip Y* sur `Ar6dofScreens`. Clics à côté : mettre le moniteur capturé à 100 % de mise à l'échelle.
- Limites : bureau en SDR, même carte graphique pour Unity et le moniteur capturé, le curseur Windows n'est pas dessiné (le laser fait office de curseur).

## 9. Clavier virtuel
Icône « KB ». AZERTY / QWERTY sans dépendre du clavier Windows (envoi Unicode). Maj / Ctrl / Alt restent actives pour la touche suivante. Un champ de texte d'un menu VR reçoit les lettres à la place de Windows.

## 10. Menus d'appareils Bluetooth (fursuit, LED...)
Le bouton **+** de la barre crée un menu lié à un appareil (ar6dof_probe doit tourner, sans `--no-ble`).
1. **Changer d'appareil** liste les appareils à portée ; clique le tien. Il est enregistré et se reconnecte tout seul (`ar6dof_devices.json`).
2. **Envoi par défaut** : *Service* et *Caractéristique* (UUID courts comme `FFE0` / `FFE1`, ou complets ; voir la doc de l'appareil ou nRF Connect).
3. **Contrôles** : Interrupteur, Curseur, Bouton, Roue de couleur, Code RGB, Texte, Choix, Etiquette. Haut / Bas / Edit / Dup / Suppr.
4. **Modèle d'octets** (écran *Edit*) : modèles prêts (1 octet, 2 octets, flottant, RGB, texte...) ou le tien : octets hexa + `{v}`, `{v16}`, `{v16be}`, `{f32}`, `{r} {g} {b}`, `{rgb}`, `{hex}`, `{text}`, `{vtext}`.
   Exemple : `7E 07 05 {r} {g} {b} 00 EF`. *Tester l'envoi maintenant* envoie les octets.
5. Pour tes scripts : évènement C# `Ar6dofDeviceMenus.Changed(menu, controle, valeur)`.

## 11. Eye tracking (2 caméras d'oeil infrarouges)
**Désactivé par défaut** : aucun port, aucun thread, caméras arrêtées.
1. Dans ar6dof_probe (à la main, une fois) : *Eye tracking* = **1**, puis les numéros des caméras gauche et droite (ceux de la liste affichée au démarrage).
2. Roue > onglet **Yeux** > *Eye tracking actif*. L'onglet montre l'état (programme en ligne, cadence, pupille vue ou non).
3. **Etalonner (9 points)** : garde la tête immobile et suis des yeux le point noir au centre de la cible (orange = attends, vert = mesure). A la fin : l'erreur en degrés
   (bon : moins de 1,5) et un point vert qui suit ton regard. Enregistré dans `ar6dof_eye_cal.txt` ; à refaire si le casque bouge sur ta tête.
4. **Recentrer** : regarde droit devant et clique.
5. Pour tes animations : `Ar6dofEyeTracker.Instance.Gaze` à chaque image : `yaw` / `pitch` (degrés, 0 = droit devant, yaw > 0 vers la droite, pitch > 0 vers le haut, par rapport à la tête),
   `yawL/pitchL/yawR/pitchR`, `blinkL/blinkR`, `openL/openR` (0 fermé, 1 ouvert), `valid`, `calibrated`. `GazeDirectionLocal` = direction du regard dans le repère de la tête.
6. Réglages de détection et de lissage : `ar6dof_eye.txt` à côté de l'exe.

---

## 12. Dépannage
| Symptôme | Cause / remède |
|---|---|
| *Can't add script component ... because the script class cannot be found* | une **erreur de compilation** ailleurs bloque tous les scripts : regarde la Console (rouge). Cause fréquente : un fichier en double (`Ar6dofXxx1.cs`) : supprime-le. |
| *Aucun paquet reçu* (StatusHud) | ar6dof_probe ne tourne pas ou `t` pas pressé / tracking pas démarré. Regarde `[probe]` dans la Console. |
| `ar6dof_probe.exe introuvable` | renseigne *Exe Path* du Launcher, ou place `ar6dof_probe-win64` à côté de `Assets`. |
| *ABSENTE : lance l'exe une fois à la main* | fais l'étape 3. |
| Écran noir, passthrough sans image | *Partager l'image = 1* dans ar6dof_probe ; `camera_model.json` renseigné ; shaders ajoutés. |
| Pas d'écrans virtuels | `ar6dof_screen.dll` dans `Assets/AR6DoF/Plugins/x86_64/` ; *Screen Shader* renseigné. |
| Tout est à l'envers | *Inverser verticalement le rendu* (réglages) ; pour les écrans, *Flip Y*. |
| `[ble] ... a l'etape "lecture du service GATT"` | pilote Bluetooth capricieux : supprime « AR6DoF-Remote » des paramètres Bluetooth de Windows, rallume la télécommande. |
| Télécommande non trouvée | firmware flashé ? moniteur série 115200 : « Bluetooth demarre... » ; nRF Connect doit voir « AR6DoF-Remote ». |
| Pas d'eye tracking | question *Eye tracking* = 1 dans ar6dof_probe, bons numéros de caméra, onglet *Yeux* actif. |

## 13. Conventions
- Pose : X avant, Y gauche, Z haut (main droite) -> Unity (conversion dans `Ar6dofConvert`).
- Les caméras virtuelles ont l'écart de ta caméra (base de la calibration) : un objet 3D est cohérent avec l'image. L'IPD logiciel ne fait que décaler les images.
- La reprojection compense la rotation de la tête, pas les déplacements.
- Flux caméra unique côte à côte (ou haut-bas) uniquement.
