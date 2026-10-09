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

➡️ **Me transmettre** : canal → ventilateur(s) observé(s), et les notes du BIOS. Je prépare alors les fichiers des étapes suivantes.

## Étapes suivantes (fichiers préparés après l'identification)

| Étape | Ce que pilote FanOMax | Durée d'observation | Contrôle |
|---|---|---|---|
| 3 | Un seul canal de boîtier (le hub), en régulation | 1 journée d'usage normal | `.\probe shadow-report --days 1` : températures, bruit, états |
| 4 | Tout le groupe CPU (hub + ventirad) | 1 à 2 jours | idem + une session de jeu |
| 5 | + le GPU | 1 à 2 jours | point chaud ≤ 85 °C, ventilateur GPU qui suit vraiment la consigne |
| 6 | Tests finaux | 2 h | Cinebench 30 min, jeu 1 h ; `Stop-Service` → retour au BIOS audible ; `Start-Service` → reprise en douceur |

## Ce qui change en mode Active

- **Démarrage en douceur** : à chaque prise de main, FanOMax part de la ventilation trouvée et la garde 25 s, le temps que la moyenne de puissance soit fiable (sauf si la température dépasse déjà la cible + 3 °C).
- La **température critique** (90 °C CPU, 105 °C GPU) impose 100 % quoi qu'il arrive, y compris pendant l'identification.
- Arrêt du service, erreur répétée, capteur perdu, boucle bloquée : **retour au BIOS** des sorties pilotées (docs/phase3-service.md §2).
