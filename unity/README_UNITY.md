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

Projet Unity 6, URP ou pipeline intégré. **API graphique : Direct3D 11 uniquement** (*Project Settings > Player > Other Settings* : décoche *Auto Graphics API for Windows* et ne garde que Direct3D11 ; ou menu **AR6DoF > Forcer Direct3D11**, puis redémarre Unity). Avec Direct3D 12 (défaut des nouveaux projets Unity 6) ou Vulkan, la capture d'écran fait **planter Unity** au Play. *Project Settings > Player > Active Input Handling* : « Both » ou « Input System Package » ou « Input Manager », au choix.

---

## 3. Première utilisation : l'application `ar6dof_app.exe` (une seule fois)
Lance **`ar6dof_app.exe`** (double-clic, dans le même dossier qu'`ar6dof_probe.exe`) : une vraie fenêtre Windows, sans console ni questions à taper.
- **Langue** : français ou English (liste en bas à droite ; par défaut la langue de Windows).
- **Caméra** : **Type** = *un module double* (une seule caméra qui donne l'image gauche | droite, comme une ELP) ou *deux caméras séparées* (tu choisis la gauche et la droite ; leurs images sont collées côte à côte, et comme elles ne sont pas synchronisées par le matériel, le suivi sera un peu moins précis qu'avec un module). La liste **Mode** est lue **sur la caméra** (modes MJPEG que Windows lui voit : taille et cadence réelles) ; avec deux caméras, seuls les modes communs aux deux sont proposés. Si la caméra ne répond pas (déjà utilisée par le suivi), des modes courants sont affichés à la place : arrête le suivi puis *Actualiser*. Ta taille perso reste possible (entrée *Personnalisé*). **Port de l'IMU** dans une liste (*Actualiser* si tu viens de brancher quelque chose) ; **cases à cocher** pour le reste (partage de l'image, télécommande Bluetooth, démarrage automatique du suivi, lecteur direct Media Foundation, eye tracking avec ses deux caméras...). *Parcourir…* pour le dossier de calibration, et la base réelle entre les objectifs en mm.
- **Enregistrer** écrit `ar6dof_config.txt` (le fichier qu'Unity et `ar6dof_probe --auto` rejouent). **Démarrer le suivi** enregistre puis lance `ar6dof_probe.exe` **sans console ni fenêtres flottantes** ; le même bouton devient **Arrêter**. En bas : le journal du programme et ses statistiques en direct (caméra, suivi, délai, CPU).
- **Montage de l'IMU** (une seule fois) : **un seul bouton**, *Calibrer le montage*. Compte à rebours de 6 s (tu lâches le casque, posé à plat et à l'endroit), bip, mesure moyennée sur 1 s ; puis 5 s pour le pencher vers l'avant (nez en haut), bip, mesure, bip final. Si le casque bouge pendant la mesure, elle attend qu'il soit immobile puis recommence toute seule (3 essais) ; *Annuler* l'arrête. (Commande UDP 9877 : `MOUNT` / `MOUNTX`.) **Sans lancer le suivi** : l'application démarre alors `ar6dof_probe --services`, un mode léger (pas de caméra stéréo, pas de Bluetooth, pas de passthrough, seulement l'IMU) qui s'arrête tout seul quand tu n'en as plus besoin. **Pendant le suivi**, des boutons remplacent les touches du clavier : redémarrer le suivi, verrouiller le sol, cap à zéro, inverser gauche / droite, vérifier la calibration.
- **Aperçus des caméras** dans l'application (pages *Caméra* et *Yeux*) : image stéréo, caméras d'oeil, caméra de bouche, dans la fenêtre même (plus de fenêtres OpenCV flottantes). L'aperçu n'existe que tant que tu le regardes : `ar6dof_probe` ne copie et n'écrit aucune image sans demande (`PV n` en UDP 9877, renouvelé chaque demi-seconde, expire après 3 s ; mémoire partagée `Local\ar6dof_preview`, 800x300).
- **Étalonnage des yeux et de la bouche sans lancer le suivi** : boutons dans les pages *Yeux* et *Bouche*. L'étalonnage des yeux se fait en plein écran (9 points) ; il utilise un champ de vision d'écran approximatif (réglable, 60° par défaut) : celui fait dans Unity, dans le casque, est plus fidèle. Les anciennes touches du clavier et les fenêtres OpenCV (`--no-windows` ne sert plus à rien, il est accepté sans effet) ont été retirées.
- La fenêtre arrête le programme qu'elle a lancé quand tu la fermes (sauf *Laisser tourner quand je ferme cette fenêtre*). Quand Unity lance lui-même `ar6dof_probe.exe` (Launcher), l'application peut rester ouverte : ses boutons d'action fonctionnent aussi.
- **Deux .exe, c'est normal** : `ar6dof_probe.exe` est le **moteur** (caméra, IMU, suivi 6DoF, envoi vers Unity, télécommande) ; `ar6dof_app.exe` est la **fenêtre de contrôle** qui le règle et le lance. Garde-les dans le même dossier. Unity n'a besoin que du moteur (le Launcher le démarre avec la configuration enregistrée).
- Look **Windows 11** : navigation à gauche par pages (*Caméra, Capteurs, Unity, Yeux, Suivi, Avancé*), réglages dans des cartes arrondies, cases à cocher et bouton principal à la **couleur d'accentuation de Windows**. La fenêtre suit le **thème de Windows** (mode sombre ou clair, barre de titre comprise) et change toute seule si tu modifies le réglage pendant qu'elle est ouverte.
- **Suivi démarré tout seul** (case cochée par défaut) : sans suivi, Unity n'a pas de position, mais le dock et les écrans s'affichent quand même (devant toi) après 2 secondes ; le dock reste alors visible en permanence.

> *Eye tracking* : laisse la case **décochée** si tu n'as pas les caméras d'oeil. Rien ne tourne alors.

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
| Launcher | Ar6dofLauncher | Exe Path | chemin complet d'`ar6dof_probe.exe`, ou laisse vide si le dossier `ar6dof_probe-win64` est à côté de `Assets` ; *Restart Running Probe* (coché) : remplace un ar6dof_probe déjà ouvert au lieu de le réutiliser |
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
3. **Plus aucune touche clavier** (R, M, F, F1... désactivées) : un vrai clavier branché sert à taper dans les écrans virtuels. Tout est en VR : boutons du dock et onglets des réglages. Pour réactiver les raccourcis ou les anciens panneaux d'information en haut à gauche de la vue, sélectionne l'objet qui porte **Ar6dofDock** dans Unity : cases **Debug** (panneaux d'info, sans la touche F1) et **Hotkeys** (raccourcis clavier).

---

## 6. La barre de tâches VR (Dock)
**Accrochée à tes pieds** : elle te suit où que tu ailles, reste **invisible** tant que tu regardes devant, puis **apparaît doucement** quand tu penches la tête vers le bas (par défaut dès 35° vers le bas, entièrement visible à 55°). Une fois visible elle ne bouge plus : tu peux la viser tranquillement. Rien ne passe devant tes menus.
Les angles, la distance, la taille et l'inclinaison se règlent dans l'onglet *Dock* des réglages ; le mode « fixe sous les écrans » existe encore (décoche *Dock aux pieds*).
- Rangée du haut = **actions rapides** : **Recentrer** (la tête), **Menus ici** (ramène les menus ouverts devant toi), **Clavier ici**, **Suivi** (le clavier te suit ou non).
- Rangée du bas : une icône par application : **écrans virtuels** (une icône d'écran avec son **numéro** dedans ; les écrans d'un même **cluster** sont côte à côte sur un fond commun, et chacun se cache ou s'affiche séparément), **clavier**, **tes menus d'appareils**, et à droite, à part, la **roue des réglages** : plus grande et séparée par un trait, mais aux couleurs normales (elle n'est pas allumée en permanence ; le trait sous l'icône indique que les réglages sont ouverts).
- **Un clic** sur l'icône affiche l'application, **un second clic** la range. Rangée, elle ne consomme rien. Un trait sous l'icône = affichée.
- Pointer une icône affiche son nom ; une pastille verte / orange / rouge = connecté / en cours / déconnecté.
- **La roue** ouvre les réglages en VR : **5 onglets**, et chaque onglet a ses **sous-pages** en haut de sa page (comme *Couleurs / Forme / Fenetres / Clavier* dans Style) :
  - *Image* : **Affichage** (infos de l'image en direct, FOV, IPD, délai, traitement) · **Optique** (réglage par œil, position, distorsion, flux).
  - *Ecrans* : images/s, collage, angle des clusters, ajouter un écran.
  - *Appareils* : **Tete** (suivi de la tête, **échelle des déplacements**) · **Telecommande** (état, calibration, filtre du rayon) · **Yeux** (eye tracking, étalonnage).
  - *Style* : inchangé.
  - *Interface* : **Dock** (position, taille, clavier) · **Langue** · **Performances** (GPU).
- **Enregistrer / Annuler / Par defaut ne concernent que la page affichée** (pour Style : la sous-page : Couleurs et Forme ensemble, Fenetres, Clavier). Un petit **point** d'accent sur un onglet ou une sous-page = modifications pas encore enregistrées. Les pages sans point d'enregistrement (Ecrans, Yeux) s'appliquent tout de suite et le disent en bas.

### Défilement des menus
Les réglages sont organisés ainsi : un **bandeau fixe en haut** (titre et onglets ; les sous-pages sont en haut de la page), un **bandeau fixe en bas** (état, *Enregistrer*, *Annuler*, *Par defaut*), et entre les deux une page qui défile, faite de **cadres** : chaque cadre regroupe, sous un titre, les réglages qui vont ensemble.
Quand un onglet est plus haut que le panneau, une **barre de défilement** apparaît à droite : **vise-la, maintiens le bouton et glisse la poignée**, ou appuie sur la piste pour y sauter, ou utilise les boutons **^** et **v**. Tu peux aussi appuyer sur une zone vide et **glisser** avec le laser, ou utiliser la **molette** d'une souris.
Le bouton visé par le laser est entouré d'un **contour net** dont la couleur est choisie pour se voir sur n'importe quelle palette.

### Fenêtres : réduire, fermer, redimensionner
Chaque écran et chaque menu a, dans un coin, deux boutons : **Réduire** (range la fenêtre dans la barre, comme un clic sur son icône) et **Fermer** (pour un écran : le retire, avec un second clic de confirmation ; il en reste toujours au moins un, qui est alors rangé). La **même poignée de redimensionnement** (coin bas droit, sous la fenêtre) et la **même barre de déplacement** (une ligne, sous la fenêtre, au milieu) sur **tout** : écrans, clavier, réglages, nouvel écran, menus d'appareils. Vise la barre, maintiens le bouton et bouge : la fenêtre te suit en restant de face (écran : bouton gauche = tout le cluster, bouton droit = cet écran seul).
Style **Windows** (par défaut) ou **Mac**, et coin au choix (haut/bas, gauche/droite ; par défaut en haut à droite) : onglet *Style* > *Fenetres*. La **roue des réglages** d'une fenêtre (écran, clavier) est toujours au même endroit : **juste à droite de la barre de déplacement**, avec le même dessin ; elle s'allume tant que le panneau est ouvert, un second clic le referme. Une fenêtre sans réglages n'a pas de roue.

### Style, langue et paramètres globaux (onglets Style et Interface)
Tout ça est **global** (pas lié à un projet Unity) : fichier `%APPDATA%\AR6DoF\settings.json`.
- **Aperçu puis Enregistrer** : un changement s'applique tout de suite mais n'est gardé que si tu cliques **Enregistrer** (en bas du panneau). **Annuler** revient au dernier enregistrement, **Par defaut** (2 clics) remet les valeurs d'usine, en aperçu aussi.
- **Style**, du plus général au plus fin : (1) la **catégorie** tout en haut (*Couleurs*, *Forme*, *Fenetres*, *Clavier*) ; (2) pour *Couleurs* et *Forme* seulement, **Appliquer a** : *Tout* (style commun à toute l'interface) ou un élément : *Barre* (le dock), *Menus*, *Touches* (le clavier), *Cadres* (les écrans), *Rayon*. Coche **Style à part pour cet élément** pour qu'il ait ses propres réglages (ex. touches rouges, rayon vert) ; sinon il suit le style commun ; (3) les cadres de réglages.
  *Couleurs* : fond, boutons, accent (survol, sélection, rayon), texte ; champs **HEX, RGB et HSV** modifiables (clique un champ : le clavier virtuel s'ouvre), les **12 palettes nommées** (Eclipse, Minuit, Rubis...) et **Palette aléatoire** tout en haut (le plus simple) ; la palette qui correspond à tes couleurs est **marquée** et son nom est écrit (*Style actuel*) ; juste dessous, **Mes styles** : donne un nom à ton réglage actuel et enregistre-le (jusqu'à 12), clique-le pour le réappliquer, **X** pour le supprimer ; puis *Personnaliser* en dessous : anneau de teinte + carré, champs HEX / RGB / HSV, **Couleur aléatoire**. *Forme* : opacité des fonds, coins arrondis, texture de fond (grille, points, carbone, rayures, dégradé, bruit, ou ton image), épaisseur du rayon.
- **Image de fond** (*Style* > *Forme*) : dossier **`Images\AR6DoF`** de Windows (créé tout seul). *Ouvrir le dossier d'images* l'ouvre dans l'explorateur, sur l'écran principal du PC : glisse-y tes `.png` / `.jpg`. La liste n'est relue **qu'en entrant** dans l'onglet *Style* > *Forme* (ou avec *Actualiser la liste*), jamais en continu. Réglages : **opacité**, mode *Etirer*, *Remplir* (recadrée), *Centree* (sans répétition), *Repeter* ou *Miroir*, et **échelle** pour les trois derniers. Les anciens dossiers `%APPDATA%\AR6DoF\pictures` et `textures` sont encore lus.
- **Style d'usine : Eclipse** (noir, gris, jaune). Un ancien `settings.json` resté cyan d'origine passe à Eclipse automatiquement ; si tu avais choisi tes couleurs, rien ne change.
- **Arrondis concentriques** : le panneau a l'arrondi réglé ; chaque cadre ou bandeau à l'intérieur en retire 12 px, et les boutons dedans encore 12 px, donc les coins suivent toujours le coin qui les entoure.
- **Animation du clavier** (*Style* > *Clavier*) : mode, puis (sauf *Aucune*) les couleurs de l'animation, et **en dessous** la vitesse et l'intensité. Le sens n'apparaît que pour *Vague*.
- **Clavier** : mêmes boutons de fenêtre que le reste (réduire, fermer, barre de déplacement, coin de taille). La taille se règle aussi dans l'onglet *Interface*.
- **Langue** : français, English, Español, Deutsch, Italiano, Português, ou *Automatique* (langue de Windows). Pour ajouter ou corriger une langue sans toucher au code : fichier texte `%APPDATA%\AR6DoF\lang\xx.txt` (xx = code de la langue), première ligne `# Nom de la langue` (facultative), puis une ligne par texte : `texte français => traduction` (modèle : les textes français sont ceux du code).

### Réglages du passthrough (onglet Image)
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

Ces réglages ont leur propre fichier, mais ils suivent maintenant les boutons du bas : **Enregistrer** les garde (en même temps que le reste), **Annuler** revient au dernier enregistrement, **Par defaut** les remet à zéro (en aperçu). Les anciens boutons *Sauver / Recharger / Remettre à zéro* de l'onglet, en double, ont disparu.

### Les menus bougent quand tu marches ? Échelle des déplacements
Les menus sont **fixes dans la pièce** (seul le dock te suit, aux pieds, et le clavier si *Suivi : OUI*). Si tu avances d'un mètre et qu'ils semblent reculer de 50 cm, c'est que le suivi sous-estime tes pas : le plus souvent, la **base entre les deux objectifs** (en mm) est mal renseignée dans `ar6dof_app` (60 mm sur une ELP), ou la calibration a été faite avec une mauvaise taille de case. Pour le vérifier : onglet *Appareils*, la ligne *hauteur des yeux* doit donner ta vraie hauteur. Pour corriger sans refaire la calibration : *Appareils* > *Suivi de la tete* > **Mesurer l'echelle** : clique *point de depart*, avance en ligne droite de la distance réglée (par défaut 100 cm, mesure-la au mètre), puis clique *Arrivee*. L'**échelle des déplacements** est réglée toute seule (le curseur permet aussi de la fixer à la main). Elle multiplie les déplacements de la tête (et la hauteur des yeux) ; l'image caméra n'est pas touchée.

---

## 7. Télécommande gyroscopique Bluetooth
1. Flashe `firmware/ar6dof_remote/ar6dof_remote.ino` sur l'ESP32-C3 (câblage et broches : en-tête du fichier). Pas de WiFi, pas de mot de passe.
2. Active le Bluetooth du PC. **Ne l'appaire pas** dans les paramètres Windows. Si « AR6DoF-Remote » y apparaît déjà (essais précédents), **supprime-le** de la liste.
3. Le Bluetooth passe par ar6dof_probe : il cherche « AR6DoF-Remote », se connecte, se reconnecte. Console : `[probe] [ble] telecommande connectee`.
4. Réglages > *Appareils* > *Telecommande* : *Calibrer 1/2* (compte à rebours de 3 s pour avoir le temps de poser la manette) (à plat, immobile) puis *2/2* (pointe relevée de ~30°, immobile). Une seule fois.
5. Recentrer : maintenir les 2 boutons 0,6 s en pointant devant toi. Bouton gauche = clic gauche, bouton droit = clic droit.
6. **Latence et stabilité** (composant `Ar6dofRemote`) : le filtre tourne à chaque paquet (125 Hz) avec l'horloge du capteur. *Filter Min Cutoff Hz* (2) : plus bas = plus stable à l'arrêt. *Filter Beta* (0,15) : plus haut = moins de retard en mouvement. *Predict Ms* (10) : avance le laser de ce temps pour compenser le Bluetooth et l'affichage ; monte-le si le laser traîne, baisse-le s'il dépasse la cible. Dans `Ar6dofPointer`, *Extra Smoothing* reste décoché (il ajouterait du retard).
7. **Portée** : antenne de l'ESP32-C3 dégagée (pas de main, batterie ni fils dessus), dongle Bluetooth sur un port USB 2 ou une rallonge (l'USB 3 brouille le 2,4 GHz). Au démarrage, ar6dof_probe demande à Windows un intervalle de connexion court : `[ble] intervalle de connexion court : accepte`.

## 8. Écrans virtuels
La capture est faite par `ar6dof_screen.dll` (DXGI, GPU vers GPU, aucun travail quand le bureau ne change pas). Icône « écrans » de la barre.
- **Le panneau de l'écran** (roue) s'ouvre **au-dessus de l'écran**, de face comme lui, **sans recouvrir** ses boutons réduire / fermer, et le **suit** (écran déplacé, redimensionné) tant que tu ne l'as pas déplacé à la main avec sa barre.
- **12 écrans au maximum**, par **clusters de 3** (gauche, centre, droite) collés entre eux ; un nouvel écran se colle au premier cluster qui n'est pas complet, sinon il forme un nouveau cluster sur un arc autour de toi. Barre sous un écran = déplacer (bouton gauche : tout le cluster ; bouton droit : cet écran seul, il se décolle). **Coin bas droit** = taille. **Roue** à droite de la barre = panneau de l'écran : source, images/s, ajouter un écran, recentrer (pour retirer ou ranger l'écran : les boutons **-** et **X** de la fenêtre, comme partout ; le panneau n'a plus de bouton Fermer ni Retirer en double). Les lignes qui n'ont pas de sens sont **cachées** : *Angle avec les voisins* (curseur 0-60°, règle tout le cluster) et *Dissocier* n'apparaissent que si l'écran est collé à un autre ; *Coller aux bords* seulement s'il y a 2 écrans ou plus. Réduire / Fermer : voir « Fenêtres » plus haut. **Important** : 12 écrans demandent la nouvelle `ar6dof_screen.dll` (4 emplacements avant) ; avec l'ancienne, le maximum reste 4.
- **Nouvel écran** : le bouton *+ Ajouter un écran* ouvre un choix à deux modes. (1) Un **moniteur réel**, avec sa résolution. (2) Un **écran virtuel logiciel** : une **fenêtre d'application** (navigateur, éditeur, lecteur vidéo...) affichée comme un écran. Tu choisis la fenêtre dans la liste et la taille de sa zone utile (garder, 1280x720, 1920x1080, 2560x1440, 3840x2160) : le programme redimensionne la fenêtre à cette taille.
- **Écran virtuel par fenêtre** : capture via Windows.Graphics.Capture, donc **sans pilote** ni installation. La fenêtre est capturée même si elle est cachée derrière d'autres (pas si elle est réduite : elle est alors remise à l'écran). Un clic sur l'écran virtuel relit la position exacte de la fenêtre ; si autre chose (une autre fenêtre) se trouve à cet endroit, la fenêtre est ramenée devant et activée avant que le clic parte. Si le clic reste sans effet, la fenêtre est sans doute lancée **en administrateur** (Windows interdit d'y envoyer des clics depuis un programme normal) : lance aussi AR6DoF en administrateur. Le bouton source de la roue change la taille (pour une fenêtre) ou de moniteur. Les écrans-fenêtres ne sont pas conservés d'un lancement à l'autre (les fenêtres changent) ; les écrans-moniteurs, si.
- **Vrai second moniteur Windows** (fenêtres qui s'y déplacent comme sur un écran physique) : il faut un pilote d'affichage virtuel signé (ex. *Virtual Display Driver*, open source) installé à part ; il apparaît ensuite dans la liste des moniteurs réels. Non fourni ici.
- Image à l'envers : décocher *Flip Y* sur `Ar6dofScreens`. Clics à côté : mettre le moniteur capturé à 100 % de mise à l'échelle.
- Limites : bureau en SDR, même carte graphique pour Unity et le moniteur capturé, le curseur Windows n'est pas dans la capture : Unity dessine un petit losange noir et blanc à l'endroit visé, sans aucun retard.

## 9. Clavier virtuel
Icône clavier (dessinée, plus de lettres « KB »). AZERTY / QWERTY sans dépendre du clavier Windows (envoi Unicode). La **roue**, à droite de la barre de déplacement du clavier (comme sur les autres fenêtres), ouvre une rangée de réglages au-dessus des touches : *AZERTY*, *QWERTY*, *Pavé num.* (le choix est enregistré tout de suite). Ces trois touches ne sont **jamais animées** (l'animation lumineuse du clavier les laisserait illisibles) : elles prennent les couleurs des fenêtres (style *Menus*), la disposition active est en accent. Mêmes réglages dans *Style* > *Clavier*. Maj / Ctrl / Alt restent actives pour la touche suivante. Un champ de texte d'un menu VR reçoit les lettres à la place de Windows.
Touches arrondies, colorables (onglet *Style* > *Clavier* pour le style ; réglages du clavier dans *Style* > sous-onglet *Clavier*).
- **Pavé numérique** : roue du clavier ou case à cocher, affiché à droite du clavier (chiffres envoyés comme des caractères : pas besoin du verrouillage numérique).
- **Touches de jeu** : W A S D et Z Q S D peuvent être colorées chacune d'une couleur au choix (S et D, communes, prennent le mélange).
- **Animation lumineuse** : *Vague* (sens au choix : droite, gauche, bas, haut, depuis le centre), *Cycle*, *Respiration*, *Réactif* (la touche s'allume quand on l'appuie), *Scintillement*, *Onde* (cercle qui part de la dernière touche). **Vitesse** et **intensité** réglables, 11 palettes de couleurs (arc-en-ciel, océan, feu, néon...). Coût nul quand l'animation est sur *Aucune*. **Déplaçable** : attrape la barre sous le clavier ; **redimensionnable** : petit carré à droite de la barre. Le bouton **Clavier ici** du dock le ramène devant toi.

Animation lumineuse (Style > Clavier) : 7 modes (vague, cycle, respiration, réactif, scintillement, onde...), vitesse, intensité, 11 palettes. En mode **Onde**, chaque touche appuyée lance sa propre onde ; les ondes se cumulent et vont toutes jusqu'au bout. Un pavé numérique s'ajoute en case à cocher.

## 9 bis. Performances du GPU
Le casque n'affiche que de l'interface sans lumière : tout ce qui sert à un jeu 3D est du gaspillage. Menu Unity **AR6DoF > Optimiser les reglages graphiques (GPU)** (déjà appelé par « Creer la scene complete ») :
- asset URP : **MSAA coupé** (textes et coins arrondis se lissent déjà), HDR, texture de profondeur, **copie de l'image (CopyColor)**, lumières, ombres, Forward+ coupés ;
- moteur de rendu : **SSAO**, décalques et amorçage de profondeur coupés (le SSAO était exécuté 3 fois par image : une par oeil et une pour l'écran) ;
- en jeu (Réglages > Système > Performances) : **images par seconde maximum** (Écran / 60 / 90 / 120 / illimité). Sans limite, le GPU dessine à fond ; c'est la cause la plus courante d'un GPU à 85 %. Lissage MSAA x2 et résolution de l'interface (0,5 à 1) au besoin.

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
1. Dans `ar6dof_app.exe`, page *Yeux* : coche *Eye tracking*, choisis les caméras gauche et droite, *Enregistrer*. Tu peux y étalonner et voir les caméras sans lancer le suivi.
2. Roue > onglet **Appareils** > *Eye tracking* > *Eye tracking actif*. L'onglet montre l'état (programme en ligne, cadence, pupille vue ou non).
3. **Etalonner (9 points)** : garde la tête immobile et suis des yeux le point noir au centre de la cible (orange = attends, vert = mesure). A la fin : l'erreur en degrés
   (bon : moins de 1,5) et un point vert qui suit ton regard. Enregistré dans `ar6dof_eye_cal.txt` ; à refaire si le casque bouge sur ta tête.
4. **Recentrer** : regarde droit devant et clique.
5. Pour tes animations : `Ar6dofEyeTracker.Instance.Gaze` à chaque image : `yaw` / `pitch` (degrés, 0 = droit devant, yaw > 0 vers la droite, pitch > 0 vers le haut, par rapport à la tête),
   `yawL/pitchL/yawR/pitchR`, `blinkL/blinkR`, `openL/openR` (0 fermé, 1 ouvert), `valid`, `calibrated`. `GazeDirectionLocal` = direction du regard dans le repère de la tête.
6. Réglages de détection et de lissage : `ar6dof_eye.txt` à côté de l'exe.

---

## 11 bis. Mouth tracking (caméra de bouche) et écrans du costume
Même principe que les yeux : **désactivé par défaut**, une seule caméra (indice `mouthcam` dans `ar6dof_config.txt`, -1 = aucune), rien ne tourne tant qu'il n'est pas activé.
1. Dans l'application : page *Bouche* (numéro de caméra, cadence), *Enregistrer*. Ou dans Unity : roue > **Appareils** > **Bouche** > *Mouth tracking actif*.
2. **Deux méthodes** (la page indique laquelle est utilisée) :
   - **Modèle IA** : place un modèle ONNX Babble sous le nom `babble_model.onnx` à côté de `ar6dof_probe.exe` (il n'est pas fourni : licence non commerciale de Project Babble). 45 paramètres ARKit / VRChat. Étalonnage : *1 - Visage neutre*, puis *2 - Toutes les expressions (8 s)*. Si l'image de l'aperçu n'a pas le menton en bas, règle `rotate` (0, 90, 180, 270) et `flip` dans `ar6dof_mouth.txt`.
   - **Détection classique** (sans modèle) : cavité sombre de la bouche, 4 poses d'étalonnage (neutre, grande ouverture, sourire large, bouche serrée).
3. **Aperçu** (bouton *Voir l'apercu*) : la caméra ne tourne que pendant l'aperçu si le suivi n'était pas activé ; en quittant la page, tout est remis comme avant.
4. Pour tes animations : `Ar6dofMouthTracker.Instance.GetShapes(tableau45)` (indices et noms dans `Ar6dofMouthShapes`, valeurs 0..1, « Left / Right » = côtés de la personne). Avec la détection classique, les paramètres principaux sont déduits (ouverture, sourire, moue, mâchoire). `Mouth` garde `jawOpen`, `wide`, `side` pour l'ancien usage.
5. Ports UDP locaux : 9886 données (paquet `MTH1` 36 octets en classique, `MTH2` 196 octets avec le modèle), 9887 commandes (`MOUTH 1|0`, `MCAL neutral|open|wide|narrow|range`, `MCAL_CLEAR`), 9888 évènements. Réglages : `ar6dof_mouth.txt` (`dark_ratio`, `min_area`, `max_area`, `smooth`, `reactivity`, `roi`, `fourcc`, `model`, `rotate`, `flip`).
6. **Écrans du costume** (page *Bouche* > carte *Ecrans du costume*) : Unity dessine la bouche (lèvres, intérieur, dents, langue) sur des **moniteurs supplémentaires** branchés au PC (écran 0 = celui du casque ; les autres comme dans les paramètres d'affichage de Windows, **à brancher avant de lancer l'application**).
   - *Face : 1 ecran* (toute la bouche) ou *Cotes : 2 ecrans* (A = côté gauche du costume, B = côté droit, chacun une moitié de la bouche).
   - *Forme du costume* : *Plat* (écran de TV), *Museau long* (2 côtés, ouverture en coin comme une vraie mâchoire), *Ecran incurve*, puis réglages fins : taille, étirement horizontal, courbure, ouverture en coin, épaisseur des lèvres, décalages, dessin ou trait, dents, 5 palettes de couleurs.
   - Par écran : numéro d'écran, rotation (quarts de tour, pour un écran monté de travers) et miroir. L'aperçu de la page montre le rendu avec les mêmes réglages, seulement tant que la page est ouverte.
   - Coût nul tant que l'affichage n'est pas activé : aucune caméra, aucun maillage, aucun écran supplémentaire. Activé, il dessine un petit maillage par écran à chaque image.
7. Les aperçus des yeux et de la bouche sont aussi dans le menu Unity (pages *Yeux* et *Bouche*), à la demande : rien n'est envoyé ni copié tant qu'on ne clique pas, et ça s'arrête en quittant la page ou en fermant le menu.

## 11 ter. Store d'applications, audio, soundboard, table de mixage
1. Dans le dock, l'icône **Store** (juste à gauche de la roue des réglages) liste les applications : *Ajouter* la place dans la barre, *Ouvrir* l'affiche, *Retirer* l'enlève. Une application fermée est désactivée (aucun calcul).
2. Place `ar6dof_audio.exe` (livré avec `ar6dof_probe.exe` par le workflow) à côté de `ar6dof_probe.exe`. Rien d'autre à installer : le son passe par WASAPI et Media Foundation, déjà dans Windows.
3. **Voix** : bouton *Moteur audio* (lance l'exe), onglet *General* : choisis le micro et **les enceintes externes** (pas le casque : il garde le son de Windows et de la RA). Onglets *Base* (gain, porte de bruit, compresseur), *Voix* (hauteur, robot, distorsion), *Espace* (écho, réverbération), *Filtres* (coupe-bas / haut, graves / medium / aigus), et des préselections. Le spectre montre le micro et ce qui sort.
4. **Soundboard** : *Ajouter des sons* ouvre un navigateur de fichiers intégré (raccourcis, disques, dossiers ; plusieurs fichiers à la fois). *Edition* : touche un bouton pour régler nom, fichier, couleur, volume, vitesse, panoramique, boucle et taille en cases ; choisis la disposition (grille, liste, flux, cercle), le nombre de colonnes et la taille des boutons. Hors édition, un appui lance le son, sans couper ceux déjà en cours ; *Tout arrêter* les coupe.
5. **Table de mixage** : volumes et muets de la voix, des sons, de la musique et du général avec leurs niveaux, spectre de la sortie ; onglet *Musique* : liste de lecture, lecture / pause, précédent / suivant, barre de position, répéter, aléatoire.
6. Tout est enregistré dans `ar6dof_audio.json` (à côté de `Assets`) et dans les PlayerPrefs (applications installées).
7. Si l'état reste « Le moteur audio ne répond pas » : `ar6dof_audio.exe` absent du dossier d'`ar6dof_probe.exe`, ou ports UDP 9890-9892 déjà pris.

## 11 quater. Appareil photo, objets 3D et stickers
Ces deux applications s'ajoutent depuis le **Store** (aucun nouveau réglage de scène). Le shader des objets est dans `Assets/AR6DoF/Resources/` : Unity l'ajoute tout seul au build, il n'y a rien à relancer dans le constructeur de scène.
1. **Appareil photo** : choisis *Avec interfaces*, *Sans interfaces* ou *Les deux*, le minuteur, la qualité, le format (carré, 4:3, 16:9), *Les deux yeux (3D)* si tu veux les deux images côte à côte. *Prendre la photo* : la fenêtre disparaît une fraction de seconde, puis les PNG sont écrits dans `Images\AR6DoF\Photos` (bouton *Ouvrir le dossier*). La photo reprend l'image de la caméra **sans** la pré-déformation des lentilles du casque (comme une photo normale) ; « sans interfaces » = rien de virtuel du tout (ni fenêtres, ni objets 3D). Si la photo sort à l'envers, active *Retourner la photo* (dépend de ta carte graphique / de l'option *Inverser verticalement le rendu*).
2. **Objets 3D** : *+ Modèle 3D* (`.obj`, avec son `.mtl` et ses textures dans le même dossier), *+ Sticker* (png / jpg), *+ Forme*. L'objet apparaît à 90 cm devant toi. *Déplacer au laser* : vise l'objet et garde le bouton appuyé. *Modifier* : onglet *Aspect* (fichier, teinte, taille, opacité, visible, éclairage, face à moi) et onglet *Position* (X / Y / Z, cap, inclinaison, roulis, plus près / plus loin). Tout est enregistré dans `%APPDATA%\AR6DoF\objects.json`.
3. Les positions sont celles du **monde Unity de la session** (le suivi 6DoF repart de son point de départ à chaque lancement) : pour retrouver tes objets au même endroit, démarre le suivi depuis le même endroit.
4. Modèles : l'axe X est inversé à la lecture (Blender → Unity), la taille est normalisée (la plus grande dimension vaut *Taille*). Fichiers de plus de 300 Mo refusés. Formats `.fbx`, `.glb` : à convertir en `.obj` pour l'instant.

## 12. Dépannage
| Symptôme | Cause / remède |
|---|---|
| Unity **plante au Play** (quelques instants après l'apparition des menus) | API graphique ≠ Direct3D11 : voir étape 2. Si ça persiste, ouvre `%LOCALAPPDATA%\Unity\Editor\Editor-prev.log` et cherche la ligne `Crash!!!` : le nom du module juste dessous (ex. `ar6dof_screen.dll`) indique le coupable. |
| *port UDP 9876 / 9878 ... Une seule utilisation de chaque adresse* | Un ancien `ar6dof_probe.exe` ou un Unity planté tourne encore. Version actuelle : le Launcher arrête tout seul l'ancien exe au Play (case *Restart Running Probe*) et l'exe s'arrête de lui-même quand Unity se ferme. Si ça persiste : Gestionnaire des tâches, termine `ar6dof_probe.exe`, `Unity.exe`, `Unity Bug Reporter` ; vérifie aussi qu'il n'y a pas deux Ar6dofReceiver / Ar6dofRemote dans la scène. Diagnostic : `netstat -ano \| findstr "9876 9878"` donne le numéro du processus qui tient le port. |
| *Can't add script component ... because the script class cannot be found* | une **erreur de compilation** ailleurs bloque tous les scripts : regarde la Console (rouge). Cause fréquente : un fichier en double (`Ar6dofXxx1.cs`) : supprime-le. |
| *Aucun paquet reçu* (StatusHud) | ar6dof_probe ne tourne pas ou `t` pas pressé / tracking pas démarré. Regarde `[probe]` dans la Console. |
| `ar6dof_probe.exe introuvable` | renseigne *Exe Path* du Launcher, ou place `ar6dof_probe-win64` à côté de `Assets`. |
| *ABSENTE : lance l'exe une fois à la main* | fais l'étape 3. |
| Écran noir, passthrough sans image | *Partager l'image = 1* dans ar6dof_probe ; `camera_model.json` renseigné ; shaders ajoutés. |
| Pas d'écrans virtuels | `ar6dof_screen.dll` dans `Assets/AR6DoF/Plugins/x86_64/` ; *Screen Shader* renseigné. |
| Tout est à l'envers | *Inverser verticalement le rendu* (réglages) ; pour les écrans, *Flip Y*. |
| `[ble] ... a l'etape "lecture du service GATT"` | pilote Bluetooth capricieux : supprime « AR6DoF-Remote » des paramètres Bluetooth de Windows, rallume la télécommande. |
| Télécommande non trouvée | firmware flashé ? moniteur série 115200 : « Bluetooth demarre... » ; nRF Connect doit voir « AR6DoF-Remote ». |
| Pas d'eye tracking | case *Eye tracking* cochée dans `ar6dof_app` (ou question *Eye tracking* = 1 dans ar6dof_probe), bonnes caméras gauche / droite, onglet *Appareils* > *Eye tracking* actif. |

## 13. Conventions
- Pose : X avant, Y gauche, Z haut (main droite) -> Unity (conversion dans `Ar6dofConvert`).
- Les caméras virtuelles ont l'écart de ta caméra (base de la calibration) : un objet 3D est cohérent avec l'image. L'IPD logiciel ne fait que décaler les images.
- La reprojection compense la rotation de la tête, pas les déplacements.
- Flux caméra unique côte à côte (ou haut-bas) uniquement.
