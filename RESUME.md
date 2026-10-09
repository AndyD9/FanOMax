# FanOMax : résumé pour reprendre le projet

> État au **2026-10-09, 10:30**. Document de passage de relais entre sessions : à lire en premier.
> Détails : [PLAN.md](PLAN.md) (plan, décisions), [TROUBLESHOOT.md](TROUBLESHOOT.md) (dépannage, journal des incidents), `docs/`.
> Repo : https://github.com/AndyD9/FanOMax (branche `main`, dernier commit `b25bbd9`).

---

## 1. Le projet en bref

Outil Windows qui remplace **FanControl** pour piloter les ventilateurs avec :
- une **anticipation par modèle** : la ventilation se calcule depuis la puissance CPU/GPU (`T ≈ a + b·P − c·Ventilo%`) ;
- un **PI lent** qui corrige l'erreur résiduelle, sans terme dérivé ;
- une **sécurité** systématique : retour au BIOS en cas de problème.

**Stack :** .NET 10, LibreHardwareMonitorLib 0.9.6 (driver PawnIO), service Windows (SYSTEM), Serilog, xUnit. Interface Avalonia 12 + ScottPlot : **première version du tableau de bord faite** (lecture des fichiers du service, en attendant l'IPC).

**Machine cible :** Ryzen 7 5800X + ventirad, ASRock B550 Pro4 (Nuvoton NCT6796D-R), Radeon RX 6750 XT. Réglages CPU appliqués par **Hydra** au démarrage : Curve Optimizer −16 à −30, PPT 142 W, **THM limit 80 °C**.

## 2. Où on en est

| Phase | État |
|---|---|
| 0 Mise en place | ✅ |
| 1 Sonde et mesures | ✅ (docs/phase1-resultats.md) |
| 2 Moteur de régulation | ✅ (docs/phase2-regulation.md) |
| 3 Service, mode fantôme | ✅ (docs/phase3-service.md), 4 incidents corrigés |
| **7 Bascule (avancée avant 4–6)** | 🔄 **en cours : étape 3, FanOMax pilote le groupe CPU** (docs/phase7-bascule.md) |
| 6 Interface : tableau de bord v1 (avancé) | ✅ v1 (`.\dashboard`) : lit `shadow/*.csv` + `live.json` |
| 4 IPC, 5 Historique SQLite, reste de la 6 | ⏳ à faire |
| 8 Finitions | ⏳ |

### Situation actuelle de la machine
- Service `FanOMax` **installé et en marche**, en **mode Active** sur le groupe CPU (config = `docs/phase7/etape-3-groupe-cpu.json`).
- Groupe CPU : **Fan #1 = ventirad**, **Fan #2 = haut** (extraction), **Fan #4 = hub avant de 3 ventilateurs** (admission, aucun régime remonté), **Fan #7 = arrière** (extraction), tous au même % : pression positive (3 en admission contre 2 en extraction). Fan #3 et #5 vides. **Cible CPU 69 °C** (profil Normal = 67 °C, surchargé).
- ⚠️ Fan #4 (hub avant) ajouté au groupe le 2026-10-09 vers 12:00 dans `etape-3-groupe-cpu.json` : **à appliquer** (copie de la config), puis vérifier à l'œil que les ventilateurs avant tournent à 25 %, puisqu'aucun régime n'est remonté.
- **GPU : pas encore piloté** (groupe désactivé). C'est le pilote AMD qui gère, souvent à plus de 80 % pour un point chaud de 55–65 °C.
- **FanControl fermé**. Son démarrage automatique est à désactiver si ce n'est pas fait.
- Version installée le 2026-10-09 à 10:01, avec le **plancher de protection**. La réinstallation a validé en réel le retour au BIOS à l'arrêt (« Retour au BIOS de 3 sortie(s) ») et la reprise en mode Active.

### Résultats du premier pilotage réel (2026-10-09, 09:41–09:56, jeu)
- 69,3 °C de moyenne pour une cible de 69 °C, **40 % de ventilation** contre environ 49 % avec le BIOS, écritures 863/863.
- Pic à 84,9 °C (charge concentrée sur un ou deux cœurs, PI trop lent) → **plancher de protection** ajouté : 40 % à 74 °C, 70 % à 78 °C, 100 % à 82 °C, sur une température lissée sur 2 s. **Pas encore observé en réel.**

## 3. Prochaines étapes

0. **Mettre à jour le service** (code modifié le 2026-10-09 vers 10:30, pas encore installé) : `powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1`. Apporte l'écriture de `live.json` (détail par ventilateur dans l'interface) et le journal écrit à chaque seconde (avant : toutes les 10 s, courbes saccadées). Vérifier ensuite dans `.\dashboard` : panneau « Ventilateurs » rempli, pastille « Pilotage actif ».

1. **Relire le journal après une longue session de jeu** (« lis le journal ») :
   - vérifier le plancher en réel : état `Protection` rare et bref, plus de pic au-delà de 80 °C ;
   - vérifier le yoyo des ventilateurs : course mesurée de 13,6 %/min, contre ≈ 8 %/min en simulation, à surveiller ;
   - vérifier la température moyenne par rapport à la cible.
2. Si c'est bon, **étape 4 : piloter le GPU**. Fichier **prêt** : `docs/phase7/etape-4-gpu.json` (validé par le test des configurations) : groupe GPU activé (`/gpu-amd/0/control/0`, point chaud `/gpu-amd/0/temperature/7`, puissance `/gpu-amd/0/power/3`, cible 82 °C). Vérifier que le ventilateur GPU suit vraiment la consigne (`applied_percent` ≈ `decision_percent`) et que le pilote AMD (Adrenalin) ne reprend pas la main. Modèle GPU calibré seulement jusqu'à 165 W ; en jeu, il monte à 180–195 W.
3. **Étape 5, tests finaux** : Cinebench 30 min + jeu 1 h, `Stop-Service` (retour au BIOS audible), `Start-Service` (reprise en douceur).
4. Restent ensuite : contrôle des courbes Smart Fan dans l'UEFI (filet de sécurité, **pas encore fait**), puis les phases 4 → 6 → 8.

**Optionnel :** un balayage de ventilation (docs/phase1-mesures.md §6) pour fixer l'effet réel des ventilateurs sur le CPU, encore incertain : `c` = 0,085 à 0,153 °C/%.

## 4. Mode opératoire (important)

- **La session Claude n'est pas administrateur.** Elle peut lire les journaux, les configurations et le dépôt, **mais pas** modifier `C:\ProgramData\FanOMax\config.json`, installer le service ni lire les capteurs. L'utilisateur lance ces commandes dans **son terminal admin**, depuis `C:\Users\andyd\Documents\DEV\FanControl`.
- Changer d'étape : `Copy-Item docs\phase7\<fichier>.json C:\ProgramData\FanOMax\config.json -Force` (appliqué à chaud).
- **Retour arrière** : `Copy-Item docs\phase7\etape-0-fantome.json C:\ProgramData\FanOMax\config.json -Force` (mode fantôme, plus rien n'est piloté).
- Mettre à jour le service après un changement de code : `powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1` (garde la configuration).
- Tableau de bord : `.\dashboard` (raccourci `dashboard.cmd`, compile en Release puis lance l'interface ; sans droits admin).
- Sonde : `.\probe <commande>` (raccourci `probe.cmd`) : `inventory`, `record`, `analyze`, `calibrate`, `shadow-report --days N`.
- Lire les journaux sans droits admin : ouvrir en lecture partagée (`[IO.File]::Open(..., 'Open', 'Read', 'ReadWrite')`).
  - `C:\ProgramData\FanOMax\logs\fanomax-AAAAMMJJ.log` : événements, et un résumé par minute `[pilotage]` / `[fantôme]` ;
  - `C:\ProgramData\FanOMax\shadow\shadow-AAAAMMJJ[-n].csv` : une ligne par groupe et par seconde (température, puissance, anticipation, correction, décision, % appliqué, état) ;
  - `C:\ProgramData\FanOMax\live.json` : instantané réécrit à chaque cycle (chaque sortie PWM avec tr/min, % appliqué, consigne, groupe ; toutes les températures). DTO : `FanOMax.Contracts/LiveSnapshot.cs`.
- **Commit et push uniquement quand l'utilisateur le demande** : il répond « oui commit et push ». Messages de commit en français, terminés par la ligne Co-Authored-By.
- Avant d'annoncer « ça compile » : vérifier la sortie **non filtrée** de `dotnet build` (incident : un filtre `-clp:ErrorsOnly | Select -Last 2` avait masqué une erreur d'analyse).
- Règles de compilation strictes (`TreatWarningsAsErrors`, analyse `latest-recommended`). Pièges fréquents : CA1305 (culture → `CultureInfo.InvariantCulture`), CA1873 (argument de log coûteux → variable locale sous `IsEnabled`), CA1859.
- Langue : tout en **français** (échanges, docs, messages) ; identifiants de code en anglais.

## 5. Architecture du code

```
src/FanOMax.Core        Logique pure : Control/ (FanRegulator, PI, filtres, SensorGuard, OutputShaper,
                        FanCurve, StaticThermalModel, RegulatorSettings + profils), Calibration/, Simulation/,
                        Analysis/ (Csv, ShadowLogTail = lecture incrémentale du journal, ShadowWindowStats), Sensors/
src/FanOMax.Contracts   DTO partagés service ↔ interface (LiveSnapshot)
src/FanOMax.Hardware    IHardwareBackend, LhmBackend (lecture/écriture), LhmMonitor (lecture seule, sonde), LhmMapping
src/FanOMax.Service     RegulationService (boucle 1 Hz, watchdog, veille), Engine/ (RegulationEngine, GroupRuntime,
                        ShadowLog, LiveStatusFile, LoopTiming), Configuration/ (options, validation, config par défaut)
src/FanOMax.Probe       Sonde en ligne de commande
src/FanOMax.App         Interface Avalonia : MainViewModel (relit les fichiers chaque seconde), GroupViewModel,
                        FanViewModel (+ FanNames : noms des canaux), Views/MainWindow (ScottPlot), PlotTheme
tests/                  FanOMax.Core.Tests (81), FanOMax.Service.Tests (51) : 132 tests, tous verts
docs/phase7/*.json      Configurations par étape de la bascule, validées par un test
scripts/                install-service.ps1, uninstall-service.ps1
```

**Règles de sécurité clés** (tests à l'appui, docs/phase3-service.md §2) :
- en mode fantôme, **aucune écriture ni aucun retour au BIOS** ;
- FanOMax ne rend au BIOS **que** les sorties qu'il a pilotées ;
- aucune écriture tant que FanControl tourne ;
- capteur perdu → BIOS puis reprise après 30 s ; au-delà de 90 °C (CPU) ou 105 °C (GPU) → 100 % ;
- watchdog (boucle bloquée plus de 5 s → BIOS) ; veille détectée (pas de fausse alerte) ; un échec du journal n'arrête jamais la régulation ;
- démarrage en douceur (part de la ventilation appliquée, la garde 25 s) ; plancher de protection CPU.

## 6. Données de référence

| | Valeur |
|---|---|
| Modèle CPU (Tctl) | `36,8 + 0,447·P − 0,153·Ventilo%`, RMS 1,9 °C |
| Modèle GPU (point chaud) | `44,3 + 0,178·P − 0,103·Ventilo%`, RMS 0,9 °C, calibré jusqu'à 165 W |
| Dynamique CPU | 51 % de la hausse en 3 s, T90 = 7 s : la température suit la puissance |
| Cinebench, ventilateurs à 100 % | 77,5–79 °C (plafonné par la THM limit), score 3 384 |
| Profils | Silence 70 °C / 50 % · Normal 67 °C / 75 % · Perf 65 °C / 100 % (cible CPU / ventilation en régime limité) ; GPU 85 / 82 / 78 °C |
| Canaux PWM | `/lpc/nct6796dr/0/control/0` = Fan #1 (ventirad), `control/1` = Fan #2 haut, `control/3` = Fan #4 hub avant ×3 (sans RPM), `control/6` = Fan #7 arrière ; `control/2` et `control/4` vides |
| Capteurs | CPU `/amdcpu/0/temperature/2` + `/amdcpu/0/power/0` ; GPU `/gpu-amd/0/temperature/7` + `/gpu-amd/0/power/3` |

## 7. Points d'attention

- **Disque `C:` presque plein** (2,6 % libres le 2026-10-09) : il a déjà arrêté le service le 2026-10-08 (incident corrigé côté code, mais le disque reste à libérer).
- **Hydra** doit rester actif avec le même profil : sinon le modèle CPU n'est plus valable (TROUBLESHOOT.md §4.16).
- **Biais connu** : le simulateur est ≈ 3,4 °C trop froid au repos (rafales sur un ou deux cœurs), sans conséquence.
- Journal des incidents (TROUBLESHOOT.md §5) : emballement du PI en boucle ouverte, disque plein, fausse alerte au réveil de la veille, faux « figé » GPU, pic à 84,9 °C, hub avant non piloté.
- **Hub avant sans régime remonté** : un ventilateur avant bloqué passerait inaperçu, contrôle visuel seulement.
