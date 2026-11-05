@echo off
rem Lance ce fichier depuis la racine du depot (le dossier qui contient CMakeLists.txt), apres avoir copie les fichiers du zip.
rem Il retire du depot (git rm) les fichiers obsoletes. Rien n'est perdu : l'historique git les garde.
if not exist CMakeLists.txt ( echo Lance ce fichier depuis la racine du depot. & pause & exit /b 1 )

git rm -q --ignore-unmatch unity/Assets/AR6DoF/Ar6dofCalibApp.cs
git rm -q --ignore-unmatch unity/Assets/AR6DoF/Ar6dofCalibWidgets.cs
git rm -q --ignore-unmatch unity/Assets/AR6DoF/Ar6dofStore.cs
git rm -q --ignore-unmatch unity/Assets/AR6DoF/Ar6dofWidgetMenus.cs
git rm -q --ignore-unmatch unity/Assets/AR6DoF/Ar6dofWindowPanel.cs
git rm -q --ignore-unmatch unity/Assets/AR6DoF/Ar6dofStereoRig.cs
git rm -q --ignore-unmatch unity/camera_model_example.json

rem --- Facultatif (retire le REM au debut des lignes pour les supprimer aussi) :
rem git rm -q --ignore-unmatch tests/vo_sim.py tests/vo_stress.py
rem git rm -q --ignore-unmatch tests/passthrough_ref.py tests/test_passthrough_ref.py

echo.
echo Fait. Verifie avec "git status", puis commit et push. Tu peux supprimer ce fichier nettoyage.bat ensuite.
pause
