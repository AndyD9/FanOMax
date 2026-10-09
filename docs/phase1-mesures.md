# Phase 1 : protocole de mesure

> La sonde est **en lecture seule** : elle n'écrit jamais les PWM et peut tourner pendant que FanControl pilote les ventilateurs.
> Durée totale : environ 1 h, dont 45 min de mesures.

## 0. Préparation (une seule fois)

1. Ouvrir un terminal **en administrateur** (clic droit sur Terminal → « Exécuter en tant qu'administrateur »).
2. Aller dans le dossier du projet :
   ```powershell
   cd $HOME\Documents\DEV\FanControl
   ```
3. Compiler la sonde :
   ```powershell
   dotnet build src\FanOMax.Probe -c Release
   ```
4. Pour les FPS : télécharger **PresentMon** (Intel, open source) depuis la [page des releases](https://github.com/GameTechDev/PresentMon/releases/latest),
   fichier `PresentMon-2.6.0-x64.exe` (≈ 1 Mo, **pas** le `.msi`), et le placer dans `tools\`.

La sonde se lance avec le raccourci **`.\probe`** à la racine du projet (fichier `probe.cmd`), par exemple `.\probe inventory`.

> ⚠️ Taper `inventory` seul ne fonctionne pas : c'est un argument de la sonde, pas une commande Windows. Toujours écrire `.\probe inventory`.
> Le terminal doit être dans le dossier du projet (`cd` ci-dessus) : le raccourci s'y trouve, et les fichiers sont écrits dans `docs\` et `captures\` du dossier courant.

## 1. Inventaire (1 min)

```powershell
.\probe inventory
```

Produit `docs\hardware-inventory.md`. Vérifier :
- la section **Capteurs clés retenus** : aucun ⚠️ ;
- la section **Contrôles de ventilateurs** : un contrôle par ventilateur branché, avec un RPM cohérent ;
- les ventilateurs du **GPU** apparaissent-ils (capteur `Fan` et `Control` sous la RX 6750 XT) ?

## 2. Repos (10 min)

Fermer les applications lourdes, ne rien faire sur le PC.
```powershell
.\probe record --label repos --duration 10m
```

## 3. Charge CPU maximale, ventilateurs à 100 % (15 min)

Objectif : savoir si **70 °C est atteignable** avec ce ventirad.

1. Dans **FanControl**, mettre les ventilateurs CPU et boîtier à **100 %** (courbe fixe ou « Manual »).
2. Lancer l'enregistrement :
   ```powershell
   .\probe record --label cinebench-100pct --duration 15m
   ```
3. Attendre **1 minute** (référence au repos), puis lancer **Cinebench R23/2024 multi-cœur** en boucle (« Minimum test duration : 10 minutes »).
4. Laisser refroidir jusqu'à la fin de l'enregistrement (≈ 4 min de retour au repos).
5. Remettre les courbes habituelles dans FanControl.

## 4. Jeu (20 min)

Avec les courbes FanControl habituelles. Jeu en **plein écran fenêtré** si possible.
```powershell
.\probe record --label jeu --duration 20m --fps
```
Commencer l'enregistrement **avant** de lancer la partie (≈ 1 min au menu ou sur le bureau), pour capter l'échelon de charge.

## 5. Analyse

```powershell
Get-ChildItem captures\*.csv | ForEach-Object { .\probe analyze $_.FullName }
```

Chaque analyse produit un `captures\*.analysis.md` à côté du CSV, avec :
- les statistiques des capteurs clés, ventilateurs et PWM ;
- le **verdict sur la cible de 70 °C** (à lire sur la capture « cinebench-100pct ») ;
- la **réponse thermique** aux échelons de puissance : hausse immédiate, délais T50/T63/T90, °C/W ;
- les FPS (capture « jeu »).

Ensuite, me transmettre `docs\hardware-inventory.md` et les fichiers `captures\*.analysis.md` (le contenu, ou simplement me dire qu'ils sont prêts : je peux les lire dans le dossier).

> Le dossier `captures\` n'est pas versionné (fichiers volumineux, et `lhm-report.txt` peut contenir des numéros de série).

## 6. Balayage de ventilation (optionnel, recommandé)

Objectif : mesurer **l'effet réel des ventilateurs** sur la température CPU à puissance constante (incertain aujourd'hui : 0,085 à 0,153 °C par %).

1. Lancer un jeu stable (une scène qui charge le CPU de façon régulière, autour de 80–90 W) et y rester.
2. Lancer l'enregistrement :
   ```powershell
   .\probe record --label balayage --duration 16m --fps
   ```
3. Dans **FanControl**, mettre les ventilateurs **CPU et boîtier** à une valeur fixe, en changeant toutes les 5 min, sans quitter le jeu :
   - 0 à 5 min : **30 %** ;
   - 5 à 10 min : **60 %** ;
   - 10 à 15 min : **100 %**.
4. Remettre les courbes habituelles.
5. Recalibrer avec toutes les captures :
   ```powershell
   .\probe calibrate (Get-ChildItem captures\*.csv | % FullName)
   ```
   Le rapport `docs\thermal-model.md` indique si l'effet des ventilateurs est identifié, et la ligne de code à reporter dans `StaticThermalModel`.

## En cas de problème

Voir [TROUBLESHOOT.md](../TROUBLESHOOT.md), en particulier §4.1 (capteurs absents), §4.12 (FPS absents) et §4.14 (sonde et FanControl en parallèle).
