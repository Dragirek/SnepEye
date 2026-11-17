[← Retour à l'index](../index.md)

# Eye tracking (yeux, clignements)

Deux caméras d'oeil infrarouges (ex. GC0308 320x240 @ 120 fps, avec LED IR) -> position de la pupille et du reflet IR -> angle du regard de chaque oeil
(yaw / pitch par rapport à la tête), clignements, ouverture des paupières -> Unity, pour des animations de fursuit liées aux yeux.
- **Désactivé par défaut** : `ar6dof_probe` pose la question *Eye tracking* ; répondre 0 = rien n'est créé (ni caméra, ni thread, ni socket).
  Côté Unity, tant que l'onglet *Yeux* (réglages en VR) n'est pas activé, aucun port n'est ouvert non plus.
- Réponses 1 : numéros des caméras gauche / droite (ceux de la liste affichée au démarrage), cadence de traitement (60 = très léger, 120 = maximum).
  Un fichier `ar6dof_eye.txt` (créé à côté de l'exe) règle la détection et le lissage ; `ar6dof_eye_cal.txt` garde l'étalonnage.
- Algorithme maison : pupille sombre sous éclairage IR (seuil adaptatif, morphologie, ellipse), reflet IR (glint) pour compenser le glissement du casque,
  carte polynomiale pupille -> angle apprise sur 9 points, lissage One Euro, détection des clignements. Une pupille suivie coûte ~0,3 ms par image.
- **Clignements et ouverture des paupières** : toujours calculés avec le regard (paquet UDP 9883 : un drapeau de clignement par oeil + une ouverture de 0 = fermé à 1 = grand ouvert). Ce n'est pas une mesure de la paupière : l'ouverture se déduit de la taille de la pupille par rapport à sa plus grande taille récente, et la pupille perdue plus de 2 images de suite compte comme un clignement. Le calcul ne coûte que quelques opérations par image (la pupille est de toute façon déjà détectée), donc il n'a pas d'interrupteur à part : le vrai coût est celui de l'eye tracking, qui reste désactivé par défaut. Dans Unity : `Ar6dofEyeTracker.Instance.Gaze` (`blinkL`, `blinkR`, `openL`, `openR`) ; l'onglet *Yeux* des réglages affiche le clignement et le % d'ouverture de chaque oeil.
- `tests/eye_math_test.cpp` (maths, g++) et `tests/eye_pupil_ref.py` (détecteur de référence sur images synthétiques, Python + OpenCV).
- Ports UDP locaux : 9883 regard (44 octets), 9884 commandes, 9885 évènements. Voir `src/eye_math.h`.
