# Phase 2 : moteur de régulation

> Code : `src/FanOMax.Core/Control`, `Calibration`, `Simulation`. Tests : `tests/FanOMax.Core.Tests`.
> Rien ne touche au matériel : tout est validé sur un simulateur calé sur les mesures réelles de la phase 1.

## 1. Principe

Mesuré en phase 1 : la température du 5800X suit la puissance en 3 à 7 s. La régulation **calcule** donc la ventilation nécessaire à partir de la puissance, au lieu d'attendre que la température monte.

```
Puissance ──► moyenne 25 s ──► modèle statique ──► anticipation ─┐
                                                                 ├─► + ─► bornes ─► limiteur ─► PWM
Température ─► garde-fou ─► EMA 10 s ─► PI lent (sans D) ────────┘
                                │
                                └─► détection « limité thermiquement » ─► ventilation du profil
```

| Brique | Fichier | Rôle |
|---|---|---|
| `SensorGuard` | Control/SensorGuard.cs | Rejette les valeurs absentes, hors plage ou figées. Garde la dernière valeur valide 3 s, puis déclare le capteur perdu |
| `Ema`, `TimeWindowAverage` | Control/Filters.cs | Filtre de température (10 s) et moyenne de puissance (25 s, ignore les pics de boost) |
| `StaticThermalModel` | Control/StaticThermalModel.cs | `T ≈ a + b·P − c·Ventilo%` : donne la ventilation requise pour une cible |
| `PiController` | Control/PiController.cs | Corrige lentement l'erreur du modèle. Intégrale bornée et gelée à la saturation (anti-emballement) |
| `OutputShaper` | Control/OutputShaper.cs | Montée ≤ 5 %/s, descente ≤ 1 %/s, zone morte de 1 % (confort acoustique) |
| `FanRegulator` | Control/FanRegulator.cs | Assemble le tout, gère les modes (normal, cible inatteignable, limité thermiquement, capteur perdu) |
| `FanCurve` | Control/FanCurve.cs | Courbe classique avec hystérésis (secours, ventilateurs secondaires) |
| `ThermalModelCalibrator` | Calibration/ | Ajuste le modèle statique sur des captures (régression), en écartant les points proches de la THM limit |
| `ThermalSimulator` | Simulation/ | Simulateur à deux nœuds (puce rapide + ventirad lent) avec THM limit et bruit de mesure |

## 2. Profils

| Profil | Cible CPU | Ventilation en régime limité | Cible GPU (point chaud) |
|---|---|---|---|
| Silence | 70 °C | 50 % | 85 °C |
| **Normal** | **67 °C** | **75 %** | **82 °C** |
| Perf | 65 °C | 100 % | 78 °C |

Ventilation minimale : 25 % (CPU/boîtier), 30 % (GPU). En cas de perte de la température : **100 % immédiatement**, et le service (phase 3) rendra la main au BIOS.

## 3. Modèles

| | a (°C) | b (°C/W) | c (°C/%) | Erreur | Source |
|---|---|---|---|---|---|
| CPU (Tctl) | 36,8 | 0,447 | 0,153 | 1,9 °C | Régression phase 1 (toutes fenêtres) |
| GPU (point chaud) | 44,3 | 0,178 | 0,103 | 0,9 °C | Capture de jeu (18 fenêtres stables) |

⚠️ **Effet des ventilateurs sur le CPU (c) incertain** : 0,153 avec toutes les fenêtres, 0,085 en écartant celles proches de la THM limit. Le régulateur est testé stable et précis **dans les deux cas** (§4). Un balayage de ventilation (voir [phase1-mesures.md](phase1-mesures.md) §6) le tranchera ; ensuite `calibrate` donne les nouvelles valeurs.

## 4. Validation en boucle fermée (simulateur)

### Fidélité du simulateur (rejeu de la puissance et de la ventilation réelles)

| Capture | Erreur RMS (moyennes 10 s) | Commentaire |
|---|---|---|
| Jeu | 0,56 °C | |
| Cinebench | 2,1 °C | |
| Repos | 4,1 °C (biais −3,4 °C) | Biais connu : rafales de charge de moins d'une seconde sur 1 ou 2 cœurs, que la moyenne à 1 Hz ne montre pas. Sans effet : ventilation au minimum |
| Échelon 49 → 127 W | 56 % en 3 s, T90 = 9 s | Mesuré : 51 % en 3 s, T90 = 7 s |

### Régulation sur la vraie série de puissance du jeu (F1 Manager 24, 20 min)

| Profil | Cible | T moyenne | T max (10 s) | Ventilation moyenne | Agitation (σ) | Course |
|---|---|---|---|---|---|---|
| Silence | 70 °C | 69,8 °C | 71,3 °C | 44 % | 2,8 % | 8 %/min |
| Normal | 67 °C | 66,9 °C | 68,3 °C | 64 % | 2,7 % | 8 %/min |
| Perf | 65 °C | 64,9 °C | 66,4 °C | 77 % | 2,6 % | 8 %/min |

À comparer avec FanControl aujourd'hui : 69,6 °C de moyenne à 49 % de ventilation fixe, et 24 % du temps au-dessus de 70 °C.

### Autres scénarios testés
- **Robustesse** : ventilateurs deux fois moins, ou nettement plus, efficaces que le modèle. La cible est tenue (66,9 °C) sans oscillation.
- **Charge lourde (Cinebench)** : 100 % du temps en régime limité thermiquement, ventilation du profil, puis retour au minimum sans intégrale emballée.
- **Silence contre Perf sous charge lourde** : Silence 50 % de ventilation, CPU bridé à 109 W ; Perf 100 %, 128 W. C'est l'arbitrage bruit/performance.
- **Échelon 30 → 95 W** : 90 % de la ventilation finale en 29 s, aucun dépassement de cible + 3 °C.
- **Capteur** : une valeur invalide de 2 s ne fait pas bouger les ventilateurs ; une température perdue passe à 100 % en moins de 4 s.

## 5. Limites connues
- Le **GPU** n'est pas testé en boucle fermée : le simulateur est calé sur le CPU. Il sera validé en **mode fantôme** (phase 3).
- Le modèle GPU repose sur un seul jeu ; au-delà de 165 W, c'est une extrapolation.
- L'effet des ventilateurs sur le CPU reste à confirmer (§3).
- Hydra doit rester actif avec le même profil : sinon le modèle CPU n'est plus valable (TROUBLESHOOT.md §4.16).

## 6. Recalibrer

```powershell
.\probe calibrate (Get-ChildItem captures\*.csv | % FullName)
```

Produit `docs\thermal-model.md` avec les coefficients calibrés comparés à ceux du code, et la ligne de code à reporter dans `StaticThermalModel`.
