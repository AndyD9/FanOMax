# Phase 7 : bascule de FanOMax aux commandes

> Principe : **un canal à la fois**, et un retour arrière en une commande à chaque étape.
> Toutes les commandes se lancent dans un **terminal administrateur**, depuis le dossier du projet (`cd $HOME\Documents\DEV\FanControl`).
> Les fichiers `docs\phase7\*.json` sont des configurations complètes, testées automatiquement (`Phase7ConfigFiles_AreValid_AndMatchTheHardware`). Le service les applique à chaud, sans redémarrage.

## Retour arrière (à tout moment)

```powershell
Copy-Item docs\phase7\etape-0-fantome.json C:\ProgramData\FanOMax\config.json -Force
```
FanOMax repasse en mode fantôme et rend immédiatement au BIOS les sorties qu'il pilotait. En dernier recours : `Stop-Service FanOMax`, ou redémarrer le PC (TROUBLESHOOT.md §1).

## Étape 0 : préparation

1. **Mettre à jour le service** (démarrage en douceur, ventilation fixe pour l'identification) :
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
   ```
2. **Appliquer la configuration de départ** (mode fantôme, cible CPU 69 °C) :
   ```powershell
   Copy-Item docs\phase7\etape-0-fantome.json C:\ProgramData\FanOMax\config.json -Force
   ```
3. **FanControl** : le laisser fermé et **désactiver son démarrage automatique** (Gestionnaire des tâches → Applications de démarrage, ou dans ses paramètres). Tant qu'il tourne, FanOMax refuse de piloter.
4. **Espace disque** : libérer de la place sur `C:` (2,6 % libres le 2026-10-09).

## Étape 1 : vérifier le BIOS (filet de sécurité)

Au redémarrage, entrer dans l'UEFI (touche `F2` ou `Suppr` sur ASRock), menu **H/W Monitor** :
1. **Courbes des ventilateurs** (CPU_FAN1, CHA_FAN…) : elles doivent réagir à la température CPU, de façon raisonnable (par exemple 50 % vers 50 °C, 100 % vers 75–80 °C). Ni « Silent » extrême, ni arrêt complet. C'est ce que tu retrouveras chaque fois que FanOMax rend la main.
2. **Noter, pour chaque prise** (CPU_FAN1, CPU_FAN2/WP, CHA_FAN1/WP, CHA_FAN2/WP, CHA_FAN3/WP) : y a-t-il un ventilateur, son régime affiché, et ce qui y est branché (ventirad, hub).
3. Prendre une photo de ces écrans, puis quitter sans rien modifier.

## Étape 2 : identifier les canaux

Trois canaux ont un ventilateur qui tourne : **Fan #1** (jusqu'à ≈ 3 060 RPM), **Fan #2** (≈ 1 670 RPM), **Fan #7** (≈ 1 500 RPM). Pour chacun, FanOMax le met **seul** à 100 % ; les autres restent au BIOS.

```powershell
Copy-Item docs\phase7\etape-1-identifier-fan1.json C:\ProgramData\FanOMax\config.json -Force
```
Attendre 10 s, regarder et écouter : **quel ventilateur accélère** ? (ventirad, ventilateurs du hub, autre). Puis :
```powershell
Copy-Item docs\phase7\etape-1-identifier-fan2.json C:\ProgramData\FanOMax\config.json -Force
```
```powershell
Copy-Item docs\phase7\etape-1-identifier-fan7.json C:\ProgramData\FanOMax\config.json -Force
```
Et pour finir, retour au mode fantôme :
```powershell
Copy-Item docs\phase7\etape-0-fantome.json C:\ProgramData\FanOMax\config.json -Force
```

Le journal confirme chaque étape : `Groupe Identification Fan #1 : — → Fixed`.

**Si un ventilateur n'a réagi à aucun des trois** (constaté le 2026-10-09) : il est sans doute branché sur un canal **sans régime remonté** (Fan #3, #4 ou #5 : pilotables, mais 0 RPM affiché). Même méthode avec `etape-1-identifier-fan3.json`, `fan4` et `fan5`, puis retour à `etape-0-fantome.json`. S'il ne réagit à aucun canal, il est alimenté directement (Molex/SATA, alimentation) et n'est pas pilotable par la carte mère.

➡️ **Me transmettre** : canal → ventilateur(s) observé(s), et les notes du BIOS. Je prépare alors les fichiers des étapes suivantes.

### Résultat de l'identification (2026-10-09)

| Canal | Ventilateur | Courbe BIOS au repos |
|---|---|---|
| Fan #1 (`control/0`) | **Ventirad** (≈ 3 060 RPM max) | ≈ 49 % |
| Fan #2 (`control/1`) | Boîtier (hub ou autre, non distingué) | ≈ 68 % |
| Fan #7 (`control/6`) | Boîtier (hub ou autre, non distingué) | ≈ 30 % |

Écriture à 100 % et retour au BIOS vérifiés sur les trois canaux (journal du 2026-10-09, 09:21–09:22).

## Étapes suivantes

> Changement de plan (2026-10-09) : l'étape « un seul canal de boîtier » est sautée. Seul, un ventilateur de boîtier agit à peine sur la température CPU : le régulateur le pousserait au maximum en jeu sans rien apprendre sur la régulation. L'écriture et le retour au BIOS étant déjà validés, on passe directement au groupe CPU complet.

| Étape | Ce que pilote FanOMax | Durée d'observation | Contrôle |
|---|---|---|---|
| 3 | Groupe CPU complet (ventirad + boîtier) : `etape-3-groupe-cpu.json` | 1 à 2 jours, dont une session de jeu | `.\probe shadow-report --days 1` : températures, bruit, états |
| 4 | + le GPU (fichier préparé après l'étape 3) | 1 à 2 jours | point chaud ≤ 85 °C, ventilateur GPU qui suit vraiment la consigne |
| 5 | Tests finaux | 2 h | Cinebench 30 min, jeu 1 h ; `Stop-Service` → retour au BIOS audible ; `Start-Service` → reprise en douceur |

```powershell
Copy-Item docs\phase7\etape-3-groupe-cpu.json C:\ProgramData\FanOMax\config.json -Force
```

## Ce qui change en mode Active

- **Démarrage en douceur** : à chaque prise de main, FanOMax part de la ventilation trouvée et la garde 25 s, le temps que la moyenne de puissance soit fiable (sauf si la température dépasse déjà la cible + 3 °C).
- La **température critique** (90 °C CPU, 105 °C GPU) impose 100 % quoi qu'il arrive, y compris pendant l'identification.
- Arrêt du service, erreur répétée, capteur perdu, boucle bloquée : **retour au BIOS** des sorties pilotées (docs/phase3-service.md §2).
