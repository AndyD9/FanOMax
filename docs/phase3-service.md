# Phase 3 : service Windows (mode fantôme)

> Code : `src/FanOMax.Service`, `src/FanOMax.Hardware`. Tests : `tests/FanOMax.Service.Tests` (38 tests, faux matériel).

## 1. Ce que fait le service

Toutes les secondes :
1. lit les capteurs (LibreHardwareMonitor + PawnIO) ;
2. fait tourner un régulateur par **groupe** (CPU : ventirad + boîtier ; GPU), avec le profil choisi ;
3. applique la règle de **température critique** (100 % au-delà de 90 °C CPU / 105 °C GPU) ;
4. **mode fantôme** : n'écrit rien et journalise la décision à côté de ce que FanControl applique réellement ;
   **mode Active** : écrit la consigne sur les sorties PWM (phase 7).

## 2. Règles de sécurité implémentées

| Règle | Comportement | Test |
|---|---|---|
| Mode fantôme = aucun accès en écriture | Ni écriture PWM, ni retour au BIOS : FanControl garde toujours la main | `Shadow_NeverWritesNorRestores_WhateverHappens` |
| Mode fantôme par défaut | La configuration générée au premier démarrage est en `Shadow` | `DefaultConfig_IsValidCommentedJson_WithTheDetectedHardware` |
| FanControl en cours d'exécution | Aucune écriture, même en mode Active (vérifié toutes les 10 s) | `Active_RefusesToWrite_WhileFanControlIsRunning` |
| FanControl démarre pendant que FanOMax pilote | FanOMax cesse d'écrire **sans** rendre au BIOS (ce serait retirer la main à FanControl) | `Active_StopsWritingWithoutRestoring_WhenFanControlStarts` |
| Capteur de température perdu | Les sorties du groupe sont rendues au BIOS ; reprise après 30 s de valeurs valides | `Active_LostSensor_HandsTheGroupBackToBios_ThenResumes` |
| Température critique | 100 % immédiatement | `Active_CriticalTemperature_ForcesFullSpeed` |
| Erreur dans la boucle | Retour au BIOS, nouvelle tentative ; arrêt après 5 erreurs consécutives (Windows relance le service 2 fois) | service |
| Boucle bloquée > 5 s | Le **watchdog** (thread dédié) rend la main au BIOS | service |
| Réveil après une veille | Pause reconnue (pas de fausse alerte du watchdog), régulateurs réinitialisés | `IsSuspendGap_DistinguishesSleepFromSlowCycles`, `ResetRegulators_ClearsTheAccumulatedCorrection` |
| Journal des décisions en échec (disque plein…) | Erreur journalisée une fois, nouvel essai toutes les 60 s, **la régulation continue** | `ShadowLogFailure_DoesNotInterruptRegulation_AndIsRetried` |
| Capteurs GPU stables au repos | Pas de fausse alerte « valeur figée » (entiers constants plusieurs minutes) | `GpuRegulator_ToleratesLongIdleWithIntegerReadings` |
| Arrêt du service | Retour au BIOS des seules sorties pilotées par FanOMax | `Dispose_HandsBackOnlyTheControlsFanOMaxWrote` |
| Configuration invalide | Refusée, la précédente reste active, erreur dans le journal | `Reconfigure_InvalidOptions_KeepsTheCurrentConfiguration` |
| Passage Active → Shadow | Retour au BIOS immédiat des sorties pilotées | `Reconfigure_FromActiveToShadow_HandsBackAndStopsWriting` |

> Rappel : si le processus est tué brutalement, rien ne peut rendre la main au BIOS ; les PWM restent figés jusqu'au redémarrage (TROUBLESHOOT.md §1). En mode fantôme, cela n'a aucune conséquence.

## 3. Installation (terminal administrateur)

```powershell
cd $HOME\Documents\DEV\FanControl
powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
```

Le script publie le service dans `C:\Program Files\FanOMax\Service`, crée le service `FanOMax` (compte SYSTEM, démarrage automatique, redémarrage après un arrêt anormal) et le démarre. `-ExecutionPolicy Bypass` ne s'applique qu'à cette commande : aucun réglage système n'est modifié.

Mise à jour après une modification du code : relancer le même script. Désinstallation : `scripts\uninstall-service.ps1` (ajouter `-RemoveData` pour effacer aussi `C:\ProgramData\FanOMax`).

## 4. Fichiers

| Quoi | Où |
|---|---|
| Configuration (rechargée à chaud) | `C:\ProgramData\FanOMax\config.json` |
| Journal du service (14 jours) | `C:\ProgramData\FanOMax\logs\fanomax-AAAAMMJJ.log` |
| Journal des décisions, 1 ligne par groupe et par seconde (7 jours) | `C:\ProgramData\FanOMax\shadow\shadow-AAAAMMJJ.csv` |

Le journal du service contient un résumé par minute, par exemple :
`[fantôme] CPU 69.2 °C 88 W → 63 % (Normal), appliqué 49 % | GPU 61.0 °C 126 W → 30 % (Normal), appliqué 58 %`

## 5. Configuration

Générée au premier démarrage à partir du matériel détecté, et commentée. Champs principaux :

| Champ | Rôle |
|---|---|
| `Mode` | `Shadow` (défaut) ou `Active` |
| `Profile` | `Silence`, `Normal` (défaut) ou `Perf` |
| `Groups[].Controls` | Sorties PWM pilotées (par défaut : celles dont le ventilateur tournait au premier démarrage, soit Fan #1, #2, #7 et le GPU) |
| `Groups[].TargetTemperature` | Cible personnalisée (sinon celle du profil) |
| `Groups[].CriticalTemperature` | 100 % au-delà (90 °C CPU, 105 °C GPU) |

Chaque enregistrement du fichier est appliqué sans redémarrer le service.

## 6. Mode fantôme : température estimée

En mode fantôme, la température mesurée résulte de la ventilation de **FanControl**, pas de celle de FanOMax : la boucle est ouverte. Si le régulateur recevait la mesure brute, son PI s'accumulerait jusqu'à la butée dès que la température dépasse la cible (incident du 2026-10-07, TROUBLESHOOT.md §5).

Le service transmet donc au régulateur une **température estimée** :

```
T_estimée = T_mesurée + c × (ventilation appliquée − décision de FanOMax)    (écart filtré sur 30 s)
```

avec `c` l'effet des ventilateurs du modèle (0,153 °C/% CPU, 0,103 °C/% GPU). Elle est journalisée dans la colonne `regulator_temperature`. En mode Active, le régulateur reçoit la mesure réelle.

> C'est une **estimation** : elle vaut ce que vaut `c`, encore incertain pour le CPU. Le vrai juge reste le passage en mode Active (phase 7).

## 7. Mode fantôme : déroulé

1. Installer le service (FanControl continue de piloter normalement).
2. Utiliser le PC normalement **plusieurs jours** : bureau, jeux, charges lourdes.
3. Bilan, depuis le dossier du projet :
   ```powershell
   .\probe shadow-report --days 3
   ```
   Pour chaque groupe : ventilation et température appliquées aujourd'hui, comparées à ce que FanOMax aurait fait (température estimée par le modèle), « yoyo » des ventilateurs (course en %/min), états du régulateur et défauts capteurs.
4. Si le bilan est bon : phase 7, bascule progressive en mode `Active`.
