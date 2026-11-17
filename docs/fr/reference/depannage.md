[← Retour à l'index](../index.md)

# Application de contrôle et dépannage

Le **Journal** (messages d'`ar6dof_probe`) a sa propre page. La page **Suivi** affiche la vue du suivi pendant que le suivi tourne : trajectoire vue de dessus, flèche de cap, hauteur dans le temps, nombre de points suivis, temps de calcul, sol. Dans **Capteurs**, le graphique de l'IMU montre le casque **et la caméra en fil de fer** (elle doit regarder devant quand tu regardes devant). Dans la liste des modes de la caméra, la plus haute résolution et la plus haute cadence sont signalées « recommandé » (suivi précis / passthrough fluide) ; une cadence élevée demande beaucoup de lumière (exposition courte), sinon le suivi décroche : l'image sombre reçoit un gain avant le suivi. Le lecteur direct Media Foundation est activé par défaut pour une première configuration (repli automatique sur OpenCV).

## IMU à 0 Hz (« paquets ok=0 ko=0 »)
`ar6dof_probe --services` rouvre le port série de l'IMU si rien n'arrive pendant 3 s (jusqu'à 6 essais, avec une impulsion DTR), et réessaie l'ouverture du port pendant 5 s au démarrage. Le nombre de réouvertures figure dans le journal (« aucun octet reçu (port rouvert N fois) : carte branchée ? firmware flashé ? autre programme sur le port ? »). La cause exacte du blocage d'origine n'a pas pu être reproduite : si l'IMU reste à 0 Hz, envoie le *Journal*.

## Application de contrôle : noms de ports, calibration, Actualiser
Les ports série portent le nom que Windows leur donne (Gestionnaire de périphériques), par exemple *Silicon Labs CP210x USB to UART Bridge (COM3)* ; l'USB natif d'un ESP32 (C3, S3...) est annoncé *ESP32 (USB natif) (COM5)*. Le nom USB de l'ESP32-C3 lui-même n'est pas modifiable (son USB est fixe), d'où ce repérage par l'identifiant Espressif. La configuration garde toujours l'identifiant `COMn`. Windows n'est interrogé qu'au démarrage et sur *Actualiser* : aucun coût en fonctionnement. Chaque liste de caméras (caméra gauche et droite, yeux, bouche) et de ports a son bouton *Actualiser*. La **calibration de la caméra** se fait dans la page **Calibration** (le dossier des calibrations RTAB-Map et la base réelle restent dans la page Caméra) ; le pilote caméra (MSMF/DirectShow) et le lecteur direct sont dans **Avancé**. La calibration du montage de l'IMU demande 3 s avant chaque pose.

## Unity : chemin de la sonde
Renseigner une fois *Launcher > Exe Path* (ou laisser `ar6dof_probe-win64` à côté du projet) suffit : le chemin est retenu, et *AR6DoF > Vérifier l'installation* utilise ce même chemin pour chercher `camera_model.json` et `ar6dof_screen.dll`. Maintenir une icône du dock 1 s fait apparaître l'écran près de toi.
