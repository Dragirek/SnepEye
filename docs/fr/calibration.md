[← Index](index.md) · [English](../en/calibration.md)

# Guide : calibrer la caméra proprement

La calibration apprend à ton programme **comment ta caméra déforme l'image**, sa focale, et **où sont les deux objectifs l'un par rapport à l'autre**. Sans elle, le suivi dérive et les menus ne sont pas à la bonne distance. Compte **10 à 15 minutes**, une seule fois (à refaire si tu changes ou déplaces une caméra). Tout se fait dans **`ar6dof_app.exe` > page Calibration**, aucune autre application.

## Ce qu'il te faut
- Un **damier noir et blanc** imprimé (une grille de carrés alternés), de préférence en **papier mat** (le papier brillant fait des reflets).
- Une surface **plate et rigide** pour le coller : carton épais, plaque, vitre. **Un papier qui ondule ruine la calibration.**
- Une **règle ou un mètre**.
- De la lumière **uniforme et forte**, sans reflet sur le damier.

## Étape 1 : imprimer et mesurer
1. Imprime un damier à **100 %** (pas « ajuster à la page »). Dans l'application, la liste *Grille (carreaux)* propose 10×7, 9×6, 11×8 et 8×5 : on compte les **carreaux**, pas les coins.
2. **Largeur et hauteur doivent avoir une parité différente** (un nombre pair et un impair, comme 10×7). Sinon l'application refuse la grille.
3. Colle-le à plat, sans bulles ni plis.
4. **Mesure à la règle la largeur de TOUTE la grille** (de bord noir à bord noir, en mm). C'est elle qui donne l'échelle en millimètres : l'application divise par le nombre de carreaux et te montre « Un carreau = ... mm ». Vérifie que ce chiffre te paraît juste.
5. Facultatif : saisis l'**écart entre les objectifs** si tu le connais ; l'application te préviendra si le résultat s'en écarte de plus de 12 %.

## Étape 2 : capturer les vues
1. Clique **▶ Commencer la calibration**. (La page lance le suivi toute seule si besoin.)
2. Tu vois l'aperçu de la caméra avec les coins du damier repérés, et un **contour « fantôme »** : la position à imiter. Un **conseil illustré** t'explique la pose en cours, et une barre de progression montre l'avancement.
3. Place le damier comme le fantôme, **immobile**, **entier** et visible par **les deux caméras**. L'application enregistre quand c'est net et assez différent des vues précédentes (« Vue enregistrée ! »).
4. Il y a **16 poses** : centre de près / de loin, les quatre coins, les quatre bords, incliné vers la gauche / la droite / le haut / le bas, très près, et en diagonale. **Le bouton *Calculer maintenant* s'active à partir de 10 vues** ; fais-les toutes pour un meilleur résultat.

### Les messages et ce qu'ils veulent dire
| Message | Quoi faire |
|---|---|
| Montre la mire à la caméra | Tout le damier doit être visible, sans coin coupé |
| L'autre caméra ne voit pas la mire | Centre-la un peu plus ou recule |
| Trop proche d'une pose déjà prise | Change de place, de distance ou d'angle |
| La mire touche le bord de l'image | Rentre-la un peu |
| Trop loin | Approche le damier |
| Les deux caméras semblent inversées | Clique **Inverser G / D**, puis refais cette pose |
| Pas dans le même sens | Ne retourne pas le damier : garde le même côté devant |

### Pour de meilleures vues
- Bouge **le damier**, la caméra reste fixe (ou l'inverse, mais pas les deux).
- **Tiens-le sans bouger** une seconde : le flou est la première cause de mauvaise note.
- Va **vraiment dans les coins et contre les bords** de l'image : c'est là que la déformation est la plus forte.
- Incline franchement (30° à 45°) pour les poses penchées.

## Étape 3 : calcul et résultat
Le calcul dure en général **10 à 60 secondes** ; ne ferme pas la page. Le résultat donne :
- **Erreur de reprojection** : moins de 0,5 px = très bien.
- **Alignement des deux caméras** : moins de 0,3 px = très bien.
- Une **note sur 100** : 85 et plus excellente, 70 et plus bonne, 50 et plus moyenne (à refaire si possible), en dessous mauvaise (à refaire).
- Des **avertissements en clair** (image pas assez couverte, mire pas plane, caméras inversées...).

Clique **Enregistrer et appliquer** (l'ancien fichier est gardé en `ar6dof_calib.prev.yml`). Si la note est mauvaise : **Refaire la calibration** en suivant les avertissements.

## Étape 4 : vérifier l'échelle à la règle (recommandé)
1. Clique **Vérifier l'échelle avec une règle**.
2. Tiens le damier **de face, immobile**, visible par les deux caméras, à **50 cm ou plus** (plus c'est loin, plus c'est juste).
3. Mesure la distance entre la **vitre des objectifs** et le damier, saisis-la, clique **Mesurer**.
4. Si l'écart est notable, **Corriger l'échelle**. Si l'écart revient, vérifie la taille d'un carreau (étape 1) et mesure de plus loin.

## Bon à savoir
- La calibration sert pour **tous les modes** de la caméra (résolution, cadence) : pas besoin de la refaire en changeant de mode. Le programme apprend en plus le **zoom du mode** avec le gyro de l'IMU : tourne la tête (au moins 50° cumulés) une fois par mode, suivi lancé.
- Tu as déjà une calibration RTAB-Map ? Bouton **Importer RTAB-Map (converti)**.
- Détails techniques : [calibration-technique](reference/calibration-technique.md).
