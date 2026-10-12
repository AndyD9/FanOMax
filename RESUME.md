# FanOMax : résumé pour reprendre le projet

> État au **2026-10-11, 11:30**, après une **réinstallation de Windows** (2026-10-10). Document de passage de relais entre sessions : à lire en premier.
> Détails : [PLAN.md](PLAN.md) (plan, décisions), [TROUBLESHOOT.md](TROUBLESHOOT.md) (dépannage, journal des incidents), `docs/`.
> Repo : https://github.com/AndyD9/FanOMax (branche `main`). Git installé le 2026-10-11 ; la copie locale (venue du zip de `main`) est rattachée au dépôt.

---

## 1. Le projet en bref

Outil Windows qui remplace **FanControl** pour piloter les ventilateurs avec :
- une **anticipation par modèle** : la ventilation se calcule depuis la puissance CPU/GPU (`T ≈ a + b·P − c·Ventilo%`) ;
- un **PI lent** qui corrige l'erreur résiduelle, sans terme dérivé ;
- une **sécurité** systématique : retour au BIOS en cas de problème.

**Stack :** .NET 10, LibreHardwareMonitorLib 0.9.6 (driver PawnIO), service Windows (SYSTEM), Serilog, xUnit. Interface Avalonia 12 + ScottPlot : **première version du tableau de bord faite** (lecture des fichiers du service, en attendant l'IPC).

**Machine cible (la même qu'avant la réinstallation) :** Ryzen 7 5800X + ventirad, ASRock B550 Pro4 (Nuvoton NCT6796D-R), Radeon RX 6750 XT. Session Windows **`PACORCS`** (avant : `andyd`), projet dans `C:\Users\PACORCS\Documents\DEV\FanOMax`.

**Réglages CPU : Hydra n'est plus là, remplacé par AMD Ryzen Master** (voir §7). Les réglages d'Hydra sont remis à l'identique, donc le modèle CPU reste valable :
- Curve Optimizer : **C0 −16, C1 −29, C2 −28, C3 à C7 −30**. La recherche automatique de Ryzen Master avait proposé −30 partout, valeur plancher jugée non fiable ;
- limites PBO : **PPT 142 W / EDC 168 A / TDC 114 A / THM limit 80 °C** (docs/phase1-resultats.md §2) ;
- validés par un test de Ryzen Master de près de 3 h le 2026-10-10 (§3).

## 2. Où on en est

| Phase | État |
|---|---|
| 0 Mise en place | ✅ |
| 1 Sonde et mesures | ✅ (docs/phase1-resultats.md) |
| 2 Moteur de régulation | ✅ (docs/phase2-regulation.md) |
| 3 Service, mode fantôme | ✅ (docs/phase3-service.md), 4 incidents corrigés |
| **7 Bascule (avancée avant 4–6)** | 🔄 **en cours : FanOMax pilote le groupe CPU, réglage « jeu » (étape 3b, 73 °C, profil Silence) depuis le 2026-10-10 à 22:45** (docs/phase7-bascule.md) |
| 6 Interface : tableau de bord v1 (avancé) | ✅ v1 (`.\dashboard`) : lit `shadow/*.csv` + `live.json` + le journal Système de Windows |
| 4 IPC, 5 Historique SQLite, reste de la 6 | ⏳ à faire |
| 8 Finitions | 🔄 interface installée par utilisateur avec démarrage automatique (`scripts\install-app.ps1`) |

### Situation actuelle de la machine (après réinstallation)
- .NET SDK 10.0.401 installé (winget), PawnIO installé. **FanControl ne tourne pas.**
- Service `FanOMax` **installé et en marche** (version du 2026-10-10 à 11:26, avec les fréquences et tensions du CPU dans `live.json`), en **mode Active** sur le groupe CPU depuis le **2026-10-10 à 16:04** (config = `docs/phase7/etape-3-groupe-cpu.json`) :
  - 4 sorties écrites à chaque cycle : Fan #1 ventirad, Fan #4 avant, Fan #2 haut et Fan #7 arrière à 75 % de la consigne ;
  - cible 69 °C ; groupe GPU désactivé ;
  - vérifié : démarrage en douceur (34 % gardés 25 s), puis descente au minimum de 25 % au repos.
- **Depuis le 2026-10-10 à 22:45 : `docs/phase7/etape-3b-groupe-cpu-jeu.json`** (cible 73 °C, profil Silence), car trop bruyant en jeu à 69 °C. Mesuré sur ~1 h de jeu à ~85 W, avant → après :
  - ventilation moyenne 52 → **41 %**, P90 67 → **51 %**, maximum 94 → **69 %**, temps au-dessus de 60 % : 20 → **1 %** ;
  - température moyenne 69 → 73 °C, maximum 74 → 78,3 °C (jamais 80 °C, régime limité jamais déclenché) ;
  - le plancher de protection (74 °C → 40 %) bascule souvent Normal ↔ Protection autour de la cible (193 fois), mais n'ajoute pas 1 % : simple bruit dans le journal.
- **Démarrage rapide de Windows actif** : désactivé le 2026-10-11, puis **réactivé à la demande de l'utilisateur** (`HiberbootEnabled` = 1, vu le 2026-10-12). Des « Arrêts non terminés » peuvent donc revenir, voir l'incident ci-dessous.
- **Interface installée** dans `%LOCALAPPDATA%\Programs\FanOMax\App` (raccourci du menu Démarrer), lancée à l'ouverture de session **dans la zone de notification** (2026-10-12) : icône colorée selon l'état, infobulle avec température et ventilation, clic = tableau de bord. Fermer la fenêtre ne ferme pas l'interface (menu de l'icône → « Quitter l'interface »).
- Avant le 16:04, le service tournait en mode Shadow avec la configuration générée automatiquement. Le hub avant (Fan #4) y manquait, faute de régime remonté.
- **Copier une configuration** : terminal **administrateur**. En cmd : `copy /Y docs\phase7\<fichier>.json C:\ProgramData\FanOMax\config.json` ; en PowerShell : `Copy-Item … -Force`. Un terminal non administrateur échoue sans rien changer.
- Disque `C:` : **767 Go libres** (le problème de disque plein est réglé).
- Sécurité basée sur la virtualisation (VBS) active, à cause d'Hyper-V. Ryzen Master tourne malgré tout.

### « Arrêts brutaux » des 2026-10-10 et 2026-10-11 : des arrêts ratés, pas des plantages
- Deux Kernel-Power 41 (redémarrages à 09:06 le 10/10 et 10:43 le 11/10), dernières mesures de FanOMax à 06:17:59 et 00:01:53 (68 °C, ~50 W, juste après la fin du jeu).
- Même signature les deux fois : **pas d'écran bleu** (`BugcheckCode` 0), **`SleepInProgress` = 6** (un arrêt était en cours), **Kernel-Boot 29 « échec du démarrage rapide » (0xC0000411)** 2–3 s avant, aucune erreur WHEA, aucun dump.
- **L'utilisateur a confirmé avoir éteint le PC lui-même** le 11/10 vers minuit : Windows n'a pas terminé l'arrêt avec le démarrage rapide (Hyper-V/VBS actifs). Ni le Curve Optimizer, ni la chauffe, ni FanOMax.
- La carte « Stabilité » classe désormais cette signature en « Arrêt non terminé » (compté, sans alerte). Le démarrage rapide, un temps désactivé, a été réactivé par l'utilisateur : ces arrêts ratés peuvent revenir, sans conséquence.
- 2026-10-12 : pendant ces arrêts, les ventilateurs restaient sur la dernière consigne de FanOMax (le service n'est pas arrêté). Depuis, le service rend la main au BIOS dès que Windows annonce la suspension (TROUBLESHOOT.md §5).

### Résultats du premier pilotage réel (avant réinstallation, 2026-10-09, 09:41–09:56, jeu)
- 69,3 °C de moyenne pour une cible de 69 °C, **40 % de ventilation** contre environ 49 % avec le BIOS, écritures 863/863.
- Pic à 84,9 °C (charge concentrée sur un ou deux cœurs, PI trop lent) → **plancher de protection** ajouté : 40 % à 74 °C, 70 % à 78 °C, 100 % à 82 °C, sur une température lissée sur 2 s. **Pas encore observé en réel.**

## 3. Prochaines étapes

0. ✅ **Réglage CPU dans Ryzen Master (2026-10-10)** : Curve Optimizer d'Hydra remis, limites PBO d'Hydra remises (PPT 142 W, EDC 168 A, TDC 114 A, THM limit 80 °C). Test de Ryzen Master de 13:02 à 15:58 sans aucune erreur :
   - 16 min en charge lourde : 113–121 W, 71–74 °C, 4,68 GHz effectifs sur les 8 cœurs ;
   - puis 2 h 40 de charge concentrée par paliers (≈ 65 W, 67–74 °C).
1. **Surveiller la carte « Stabilité » quelques jours** : aucun « Redémarrage imprévu » (les « Arrêts non terminés » ne comptent pas), et pas d'erreur processeur corrigée (WHEA 19) sur un cœur. Si une erreur apparaît, remonter le Curve Optimizer de ce cœur de 3 à 5 crans.
2. ✅ **Bascule reprise à l'étape 3** (2026-10-10, 16:04). Ventilateurs avant (Fan #4, sans régime remonté) vérifiés à l'œil : ils tournent.
3. ✅ **Journal relu après une longue session de jeu** (2026-10-10 soir) : trop bruyant à 69 °C → étape 3b (73 °C, Silence), voir §2. Si c'est encore trop fort : cible 74–75 °C. Le plancher de protection pourrait démarrer plus haut avec cette cible (il bascule souvent sans effet), à décider.
4. Si c'est bon, **étape 4 : piloter le GPU**. Fichier **prêt** : `docs/phase7/etape-4-gpu.json` (validé par le test des configurations) :
   - groupe GPU activé (`/gpu-amd/0/control/0`, point chaud `/gpu-amd/0/temperature/7`, puissance `/gpu-amd/0/power/3`, cible 82 °C) ;
   - vérifier que le ventilateur GPU suit vraiment la consigne (`applied_percent` ≈ `decision_percent`) et que le pilote AMD (Adrenalin) ne reprend pas la main ;
   - modèle GPU calibré seulement jusqu'à 165 W ; en jeu, il monte à 180–195 W.
5. **Étape 5, tests finaux** : Cinebench 30 min + jeu 1 h, `Stop-Service` (retour au BIOS audible), `Start-Service` (reprise en douceur).
6. Restent ensuite :
   - contrôle des courbes Smart Fan dans l'UEFI (filet de sécurité, **pas encore fait**, à refaire si le BIOS a été réinitialisé) ;
   - les phases 4 → 6 → 8.

**Optionnel :**
- un balayage de ventilation (docs/phase1-mesures.md §6) pour fixer l'effet réel des ventilateurs sur le CPU, encore incertain : `c` = 0,085 à 0,153 °C/% ;
- un suivi des réglages par cœur dans FanOMax : saisir le Curve Optimizer de chaque cœur, et le tableau de bord le croise avec les erreurs WHEA.

## 4. Mode opératoire (important)

- **La session Claude n'est pas administrateur.** Elle peut lire les journaux, les configurations et le dépôt, **mais pas** modifier `C:\ProgramData\FanOMax\config.json`, installer le service ni lire les capteurs. L'utilisateur lance ces commandes dans **son terminal admin**, depuis `C:\Users\PACORCS\Documents\DEV\FanOMax`.
- Changer d'étape : `Copy-Item docs\phase7\<fichier>.json C:\ProgramData\FanOMax\config.json -Force` (appliqué à chaud).
- **Retour arrière** : `Copy-Item docs\phase7\etape-0-fantome.json C:\ProgramData\FanOMax\config.json -Force` (mode fantôme, plus rien n'est piloté).
- Mettre à jour le service après un changement de code : `powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1` (garde la configuration). Le service tourne depuis `C:\Program Files\FanOMax\Service`.
- Tableau de bord installé : menu Démarrer « FanOMax », et lancé à l'ouverture de session. Mettre à jour après un changement de code : `powershell -ExecutionPolicy Bypass -File scripts\install-app.ps1` (sans droits admin ; ferme et relance l'interface). Désinstaller : `scripts\uninstall-app.ps1`.
- Version de dev : `.\dashboard` (raccourci `dashboard.cmd`, compile en Release puis lance l'interface depuis `bin\`). **La fermer avant de la relancer** : sinon la compilation échoue (fichiers verrouillés).
- Sonde : `.\probe <commande>` (raccourci `probe.cmd`) : `inventory`, `record`, `analyze`, `calibrate`, `shadow-report --days N`.
- Lire les journaux sans droits admin : ouvrir en lecture partagée (`[IO.File]::Open(..., 'Open', 'Read', 'ReadWrite')`).
  - `C:\ProgramData\FanOMax\logs\fanomax-AAAAMMJJ.log` : événements, et un résumé par minute `[pilotage]` / `[fantôme]` ;
  - `C:\ProgramData\FanOMax\shadow\shadow-AAAAMMJJ[-n].csv` : une ligne par groupe et par seconde (température, puissance, anticipation, correction, décision, % appliqué, état), gardée 7 jours ;
  - `C:\ProgramData\FanOMax\live.json` : instantané réécrit à chaque cycle (chaque sortie PWM avec tr/min, % appliqué, consigne, groupe ; toutes les températures ; fréquences et tensions du CPU). DTO : `FanOMax.Contracts/LiveSnapshot.cs`.
  - Journal Système de Windows (erreurs WHEA, Kernel-Power 41, Kernel-Boot 29) : lisible sans droits admin, `Get-WinEvent -LogName System`.
- **Commit et push uniquement quand l'utilisateur le demande** : il répond « oui commit et push ». Messages de commit en français, terminés par la ligne Co-Authored-By. Git : `C:\Program Files\Git\cmd\git.exe` (pas forcément dans le PATH d'une session ouverte avant son installation).
- Avant d'annoncer « ça compile » : vérifier la sortie **non filtrée** de `dotnet build` (incident : un filtre `-clp:ErrorsOnly | Select -Last 2` avait masqué une erreur d'analyse).
- Règles de compilation strictes (`TreatWarningsAsErrors`, analyse `latest-recommended`). Pièges fréquents : CA1305 (culture → `CultureInfo.InvariantCulture`), CA1873 (argument de log coûteux → variable locale sous `IsEnabled`), CA1859.
- Langue : tout en **français** (échanges, docs, messages) ; identifiants de code en anglais.

## 5. Architecture du code

```
src/FanOMax.Core        Logique pure : Control/ (FanRegulator, PI, filtres, SensorGuard, OutputShaper,
                        FanCurve, StaticThermalModel, RegulatorSettings + profils), Calibration/, Simulation/,
                        Analysis/ (Csv, ShadowLogTail = lecture incrémentale du journal, ShadowLogLookup = mesures
                        à un instant passé, ShadowWindowStats), Diagnostics/ (StabilityEvents : tri des événements
                        WHEA et Kernel-Power 41, arrêts non terminés, bilan de stabilité), Sensors/
src/FanOMax.Contracts   DTO partagés service ↔ interface (LiveSnapshot)
src/FanOMax.Hardware    IHardwareBackend, LhmBackend (lecture/écriture), LhmMonitor (lecture seule, sonde), LhmMapping
src/FanOMax.Service     RegulationService (boucle 1 Hz, watchdog, veille), Engine/ (RegulationEngine, GroupRuntime,
                        ShadowLog, LiveStatusFile, LoopTiming), Configuration/ (options, validation, config par défaut)
src/FanOMax.Probe       Sonde en ligne de commande
src/FanOMax.App         Interface Avalonia : MainViewModel (relit les fichiers chaque seconde), GroupViewModel,
                        FanViewModel (+ FanNames : noms des canaux), ProcessorInfo (panneau « Processeur »),
                        StabilityViewModel + Diagnostics/SystemEventLog (carte « Stabilité », journal Windows
                        relu toutes les 30 s), Views/MainWindow (ScottPlot), PlotTheme ; Tray/ (TrayController :
                        icône et menu de la zone de notification, TrayIconRenderer : icônes dessinées au lancement,
                        SingleInstance, Autostart) ; `--minimized` = démarrage sans fenêtre
tests/                  FanOMax.Core.Tests (112), FanOMax.Service.Tests (62) : 174 tests, tous verts (2026-10-11)
docs/phase7/*.json      Configurations par étape de la bascule, validées par un test
scripts/                install-service.ps1, uninstall-service.ps1 (admin) ; install-app.ps1, uninstall-app.ps1 (utilisateur)
```

**Ajouts des 2026-10-10 et 11 :**
- fréquences et tensions du CPU dans `live.json`, et panneau « Processeur » du tableau de bord ;
- carte « Stabilité » : erreurs WHEA et redémarrages imprévus des 30 derniers jours, chacun avec le cœur en cause et les mesures de FanOMax au même moment. Pour un arrêt brutal, c'est la dernière mesure avant l'arrêt, ce qui en donne l'heure exacte. Les arrêts non terminés (démarrage rapide) sont reconnus et ne déclenchent pas d'alerte ;
- installation de l'interface par utilisateur, démarrage automatique ; configuration de jeu `etape-3b-groupe-cpu-jeu.json`.

**Règles de sécurité clés** (tests à l'appui, docs/phase3-service.md §2) :
- en mode fantôme, **aucune écriture ni aucun retour au BIOS** ;
- FanOMax ne rend au BIOS **que** les sorties qu'il a pilotées ;
- aucune écriture tant que FanControl tourne ;
- capteur perdu → BIOS puis reprise après 30 s ; au-delà de 90 °C (CPU) ou 105 °C (GPU) → 100 % ;
- watchdog (boucle bloquée plus de 5 s → BIOS) ; veille détectée (pas de fausse alerte) ; un échec du journal n'arrête jamais la régulation ;
- veille, hibernation ou arrêt avec démarrage rapide → BIOS avant la suspension, reprise en douceur au réveil ;
- démarrage en douceur (part de la ventilation appliquée, la garde 25 s) ; plancher de protection CPU.

## 6. Données de référence

| | Valeur |
|---|---|
| Modèle CPU (Tctl) | `36,8 + 0,447·P − 0,153·Ventilo%`, RMS 1,9 °C (calé avec les réglages d'Hydra, §1) |
| Modèle GPU (point chaud) | `44,3 + 0,178·P − 0,103·Ventilo%`, RMS 0,9 °C, calibré jusqu'à 165 W |
| Dynamique CPU | 51 % de la hausse en 3 s, T90 = 7 s : la température suit la puissance |
| Cinebench, ventilateurs à 100 % | 77,5–79 °C (plafonné par la THM limit de 80 °C), score 3 384 |
| Profils | Silence 70 °C / 50 % · Normal 67 °C / 75 % · Perf 65 °C / 100 % (cible CPU / ventilation en régime limité) ; GPU 85 / 82 / 78 °C |
| Canaux PWM | `/lpc/nct6796dr/0/control/0` = Fan #1 (ventirad), `control/1` = Fan #2 haut, `control/3` = Fan #4 hub avant ×3 (sans RPM), `control/6` = Fan #7 arrière ; `control/2` et `control/4` vides |
| Capteurs | CPU `/amdcpu/0/temperature/2` + `/amdcpu/0/power/0` ; GPU `/gpu-amd/0/temperature/7` + `/gpu-amd/0/power/3` |
| Curve Optimizer | C0 −16 (★ meilleur cœur), C1 −29, C2 −28 (● 2e meilleur cœur), C3 à C7 −30 |

## 7. Points d'attention

- **Réglages CPU (Ryzen Master)** : le modèle CPU n'est valable qu'avec le Curve Optimizer ci-dessus **et** PPT 142 W / EDC 168 A / TDC 114 A / THM limit 80 °C. Ouvrir les limites PBO, c'est quelques % de plus en charge lourde seulement (rien en jeu), pour une température qui peut monter jusqu'à 90 °C. Si les réglages changent, refaire les mesures et recalibrer (TROUBLESHOOT.md §4.16, écrit pour Hydra mais valable pour Ryzen Master).
- **Cœurs à surveiller** : C1 (−29) et C2 (−28) sont presque au plancher, et C2 est le 2e meilleur cœur. Une erreur WHEA 19 sur l'un d'eux dans la carte « Stabilité » → le remonter de 3 à 5 crans.
- **Arrêts ratés des 10 et 11/10** (§2) : expliqués (démarrage rapide, réactivé depuis, donc ils peuvent revenir). Un vrai « Redémarrage imprévu » dans la carte « Stabilité » serait, lui, à creuser : elle donne l'heure et la charge.
- **Biais connu** : le simulateur est ≈ 3,4 °C trop froid au repos (rafales sur un ou deux cœurs), sans conséquence.
- Journal des incidents (TROUBLESHOOT.md §5) : emballement du PI en boucle ouverte, disque plein, fausse alerte au réveil de la veille, faux « figé » GPU, pic à 84,9 °C, hub avant non piloté.
- **Hub avant sans régime remonté** : un ventilateur avant bloqué passerait inaperçu, contrôle visuel seulement. C'est aussi pour ça que la configuration automatique l'a oublié.
