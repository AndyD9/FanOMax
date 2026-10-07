# Phase 1 : résultats des mesures

> Captures du 2026-10-07 (FanControl actif, sonde en lecture seule). Fichiers bruts dans `captures/` (non versionnés).

## 1. Captures

| Capture | Durée | Contexte | CPU (moy. / max) | Puissance CPU (moy.) | Ventilo Fan #1 |
|---|---|---|---|---|---|
| `repos` | 10 min | Bureau, activité de fond | 52.4 / 67.6 °C | 44 W (pics à 84 W) | 29 % (courbe FanControl) |
| `cinebench-100pct` | 15 min | Cinebench multi-cœur, **ventilateurs à 100 %** | 78–79 °C en charge | 128 W en charge | 100 % |
| `jeu` | 20 min | F1 Manager 24, FPS via PresentMon | 69.6 / 72.5 °C | 88 W | 49 % (fixe) |

## 2. Réglages CPU en place (Hydra)

| Réglage | Valeur |
|---|---|
| Curve Optimizer (CCD0) | C00 −16, C01 −29, C02 −28, C03 à C07 −30 (undervolt déjà en place) |
| PPT / EDC / TDC | 142 W / 168 A / 114 A |
| **THM limit** | **80 °C** |
| Fréquence max | 0 (pas de dépassement du boost) |

> Ces réglages sont appliqués par **Hydra** à chaque démarrage de Windows (et non dans le BIOS). Toutes les captures ont été faites avec Hydra actif. Si Hydra est désactivé ou si son profil change, le modèle thermique du §4 n'est plus valable et il faut refaire les mesures.
>
> Score Cinebench obtenu pendant la capture : **3 384** (ventilateurs à 100 %, THM limit 80 °C, 128 W, sous la limite PPT de 142 W).

## 3. Verdicts

### Cible 70 °C en charge lourde (Cinebench) : ❌ hors d'atteinte
Avec **tous les ventilateurs à 100 %**, le CPU se stabilise à **77,5–79 °C** pour 128 W, juste sous la **limite thermique PBO de 80 °C**. Ce plateau est très probablement la limite elle-même : le CPU réduit son boost pour rester sous 80 °C, et non un équilibre naturel du refroidissement.

Conséquences :
- 70 °C est hors d'atteinte sur ce type de charge, même à 100 % et avec l'undervolt déjà en place ;
- en régime **limité thermiquement**, plus de ventilation fait gagner des **fréquences** (performance), pas des degrés. Le choix devient : performance (ventilateurs forts) ou silence (le CPU tient 80 °C lui-même, avec un peu moins de boost) ;
- les points Cinebench sont plafonnés par la limite : ils biaisent probablement le coefficient du ventilateur dans le modèle du §4.

### Cible 70 °C en jeu : ✅ atteignable
- Mesuré : 69,6 °C en moyenne à 49 % de ventilation, **24 % du temps au-dessus de 70 °C** (max 72,5 °C).
- Estimé par le modèle (§4) : environ **66 °C à 65 %** et **61 °C à 100 %** de ventilation, à 88 W.
- Il suffit donc d'un peu plus de ventilation que la courbe FanControl actuelle pendant le jeu.

### FPS : ✅ fonctionne
PresentMon a détecté `F1Manager24.exe` automatiquement : moyenne 159 FPS, max 353, min 2 (écrans de chargement).

### Ventilateur GPU : ✅ lu, contrôle exposé
Écriture à valider en phase 7.

## 4. Dynamique thermique du CPU : le résultat clé

### La température suit la puissance presque instantanément
Échelon Cinebench (49 → 127 W, ventilateurs à 100 %) :

| Hausse immédiate (3 s) | T50 | T63 | T90 | Hausse lente ensuite |
|---|---|---|---|---|
| +16,6 °C (51 %) | 3 s | 4 s | 7 s | +2 à 3 °C sur 10 min |

Chronologie : 42 °C (repos) → **76 °C en 10 s** → 77,5 °C à 40 s → 78–79 °C ensuite, stable.

**Interprétation :** sur le 5800X (une seule puce de 8 cœurs, très dense), la résistance thermique **puce → capot → ventirad** domine. Le ventirad lui-même a beaucoup de marge : il ne se réchauffe que de 2 à 3 °C en 10 min à pleine charge. Conséquences :
- **la température CPU est presque une fonction directe de la puissance**, avec seulement quelques secondes de retard ;
- la « prédiction » sur la puissance ne donne donc que **3 à 7 s d'avance** sur la température elle-même ;
- les pics de température du 5800X sont des pics de puissance (boost) : **les ventilateurs ne doivent pas les suivre**.

### Modèle statique (régression sur les 3 captures, fenêtres stables de 10 s)

```
T_cpu ≈ 36,8 + 0,447 · P_cpu(W) − 0,153 · Ventilo(%)        erreur RMS : 1,9 °C
```

| Ventilation | Puissance max pour tenir 70 °C |
|---|---|
| 30 % | 84 W |
| 50 % | 91 W |
| 75 % | 100 W |
| 100 % | 108 W |

> ⚠️ **Effet des ventilateurs (−0,153 °C/%, soit environ −11 °C entre 30 et 100 %) à confirmer.** Dans ces captures, la ventilation varie surtout *en même temps* que le type de charge : le coefficient peut être biaisé. Un test dédié à puissance constante le mesurera directement (voir §6).

## 5. Conséquences sur la conception (phase 2)

1. **Anticipation = modèle statique, pas prédiction temporelle.** Comme T ≈ f(P, ventilo), la consigne de ventilation se calcule directement depuis la puissance :
   `ventilo_requis = (36,8 + 0,447·P − T_cible) / 0,153`. Le PID ne sert plus qu'à **corriger lentement** l'erreur résiduelle (PI, intégrale lente).
2. **Puissance filtrée (moyenne sur 20–30 s)** pour l'anticipation : les pics de boost ne doivent pas faire monter les ventilateurs.
3. **Dérivée du PID inutile** (Kd ≈ 0) : la température suit la puissance trop vite et est trop bruitée.
4. **Plafond réaliste par profil** : au-delà d'environ 108 W, 70 °C est physiquement hors d'atteinte. Le moteur doit le savoir et plafonner à 100 % sans s'emballer (anti-windup), plutôt que de « chercher » une cible impossible.
5. **Détecter le régime « limité thermiquement »** (Tctl ≥ THM limit − 3 °C de façon soutenue) : la température ne répond plus aux ventilateurs, c'est le CPU qui la tient. Dans ce régime, la ventilation suit le **profil** (Silence : niveau modéré, le CPU tient 80 °C en réduisant son boost ; Perf : ventilation forte pour garder les fréquences), au lieu d'un PI saturé en permanence.
6. **Modèle calé hors régime limité** : exclure les points proches de la THM limit lors du calibrage, pour ne pas fausser le coefficient du ventilateur.

## 6. Mesures complémentaires proposées

- **Balayage de ventilation à puissance constante** (≈ 15 min, en jeu ou avec une charge fixe) : ventilateurs à 30 %, puis 60 %, puis 100 %, 5 min chacun. Mesure directement le gain °C par % de ventilation, nécessaire au réglage du PI.
- **Inventaire physique des ventilateurs** : les ventilateurs de boîtier passent par un **hub**, qui ne renvoie le régime que d'un seul ventilateur. Reste à savoir sur quel canal est le hub et sur quel canal est le ventirad, parmi Fan #1 (≈ 3 060 RPM max), Fan #2 (≈ 1 670 RPM max) et Fan #7 (≈ 1 500 RPM max). Les canaux 3, 4 et 5 sont pilotés par FanControl mais ne renvoient aucun RPM : probablement rien de branché. À confirmer en phase 7 en faisant varier chaque PWM.
- **Fréquence CPU dans les captures** : en régime limité thermiquement, l'effet de la ventilation se lit sur les fréquences, que la sonde n'enregistre pas encore (à ajouter : « Cores (Average Effective) »).

## 7. GPU : forte marge pour réduire le bruit

En jeu (79 % de charge GPU, 126 W en moyenne) :

| Capteur | Moyenne | Max |
|---|---|---|
| GPU Core | 49,8 °C | 54 °C |
| GPU Hot Spot | 60,9 °C | 69 °C |
| Ventilateur GPU | 58 % (2 117 RPM) | 80 % (2 934 RPM) |

Le GPU est **largement sur-refroidi** : un point chaud à 69 °C est très loin des limites du RX 6750 XT (110 °C). Une cible de point chaud autour de 80–85 °C permettrait de baisser nettement la vitesse et le bruit du ventilateur GPU, sans risque.

✅ **Décidé : cible GPU = point chaud 80–85 °C.**
