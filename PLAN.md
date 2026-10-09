# FanOMax : plan de projet

> Outil Windows de régulation des ventilateurs : **PID**, **prédiction sur la consommation**, **historique**.
> Remplace FanControl (Rem0o) une fois validé.
> Nom : **FanOMax**, distinct de « FanControl » pour éviter toute confusion avec l'outil existant.
> Repo : https://github.com/AndyD9/FanOMax

---

## 1. Objectifs

| # | Objectif | Priorité |
|---|---|---|
| O1 | Régulation **PID** sur une température cible, et non sur une simple courbe | Haute |
| O2 | **Prédiction** : anticiper la chauffe à partir de la consommation CPU/GPU (W) et de la pente de la température | Haute |
| O3 | **Sécurité** : rendre la main au BIOS en cas de problème | Haute |
| O4 | Interface de bureau : supervision temps réel, édition des réglages | Moyenne |
| O5 | **Historique et statistiques** (indispensables pour régler le PID et la prédiction) | Moyenne |
| O6 | Mode courbe classique en secours ou pour les ventilateurs secondaires | Moyenne |
| O7 | Profils manuels (Silence / Normal / Perf), et éventuellement des profils horaires | Basse |
| O8 | **Monitoring** : température et charge CPU/GPU, **FPS**, dans l'application et dans un mini-widget toujours visible | Moyenne |

### Hors périmètre (décidé)
- ❌ Pilotage à distance, interface web, API réseau
- ❌ Détection du jeu ou de l'application au premier plan
- ❌ Overlay injecté dans le jeu (hook DirectX : risque avec les anti-cheat). L'overlay d'AMD Adrenalin existe déjà pour ça.

---

## 2. Matériel cible

| Élément | Modèle | Remarque |
|---|---|---|
| Carte mère | ASRock B550 Pro4 | Super I/O **Nuvoton NCT6796D-R** (`/lpc/nct6796dr/0`) : 7 canaux PWM, **3 ventilateurs détectés** (Fan #1, #2, #7) |
| CPU | AMD Ryzen 7 5800X | Températures très instables : il faut filtrer. Capteur retenu : `Core (Tctl/Tdie)` (pas de décalage Tctl sur le 5800X) |
| Réglages CPU | Appliqués au démarrage par **Hydra** : Curve Optimizer −16 à −30 par cœur, PPT 142 W, EDC 168 A, TDC 114 A, **THM limit 80 °C** | Undervolt déjà en place. La THM limit plafonne le CPU à 80 °C : au-delà, il réduit son boost. Toutes les mesures sont faites avec Hydra actif |
| Ventilateurs boîtier | Sur un **hub** | Le hub ne renvoie le régime que d'un ventilateur : un seul RPM visible pour tout le groupe |
| Refroidissement CPU | Ventirad (air) | Forte inertie : réaction plus lente qu'une AIO, l'anticipation est d'autant plus utile |
| GPU | AMD Radeon RX 6750 XT | Ventilateur **lu** (RPM) et contrôle **exposé** par LHM (`/gpu-amd/0/control/0`). Écriture à valider en phase 7 |
| Driver | PawnIO (déjà installé par FanControl) | Remplace WinRing0, qui est signalé par Defender |
| Référence | FanControl utilise LibreHardwareMonitorLib 0.9.6 | Lecture et contrôle déjà validés sur cette machine |

---

## 3. Stack technique

| Couche | Choix |
|---|---|
| Runtime | **.NET 10** |
| Moteur | **Service Windows** (Worker Service, compte SYSTEM) |
| Matériel | **LibreHardwareMonitorLib** + driver **PawnIO** |
| Interface | **Avalonia** + CommunityToolkit.Mvvm + **ScottPlot**, avec une icône dans la barre des tâches |
| IPC service ↔ interface | **Named pipes** + StreamJsonRpc (local uniquement, aucun port réseau) |
| FPS | **PresentMon** (Intel, open source, MIT) lancé par le service, via ETW. Fonctionne avec tous les GPU et tous les jeux, sans injection. |
| Historique | **SQLite** (Microsoft.Data.Sqlite) |
| Config | JSON dans `C:\ProgramData\FanOMax\config.json`, rechargé à chaud |
| Logs | Serilog, fichiers dans `C:\ProgramData\FanOMax\logs\` |
| Tests | xUnit + **simulateur thermique** |

---

## 4. Architecture

```
┌──────────────── FanOMax.Service (SYSTEM) ────────────────┐
│                                                            │
│  Capteurs (LHM) ──► Moteur de régulation ──► PWM (LHM)     │
│   temp, W, RPM      [PID + prédiction | courbe]            │
│        │                     │                             │
│        ▼                     ▼                             │
│     SQLite             Watchdog / Failsafe ──► BIOS        │
│                                                            │
│              Serveur named pipe (StreamJsonRpc)            │
└──────────────────────────────┬─────────────────────────────┘
                               │
                 FanOMax.App (Avalonia, session utilisateur)
```

**Principe :** l'interface n'est qu'une fenêtre sur le service. Fermer l'interface ou la faire planter ne touche jamais à la régulation.

### Structure de la solution

```
FanOMax.slnx              Solution (nouveau format XML)
Directory.Build.props     Paramètres communs (net10.0, nullable, warnings traités en erreurs)
Directory.Packages.props  Versions NuGet centralisées
src/
  FanOMax.Core/        Logique pure, aucune dépendance au matériel :
    Control/             Régulateur, PI, filtres, garde-fou capteurs, courbe, modèle statique, profils
    Calibration/         Calibrage du modèle statique (régression)
    Simulation/          Simulateur thermique CPU + ventirad
    Analysis/, Sensors/  Analyse des captures, capteurs clés
  FanOMax.Hardware/    IHardwareBackend, LhmBackend (lecture/écriture, service), LhmMonitor (lecture seule, sonde)
  FanOMax.Contracts/   DTO et interfaces IPC partagés service ↔ interface
  FanOMax.Service/     Service Windows : moteur (Engine/), configuration (Configuration/), watchdog, journal fantôme
  FanOMax.App/         Interface Avalonia
  FanOMax.Probe/       Sonde : inventory, record, analyze, calibrate, shadow-report
tests/
  FanOMax.Core.Tests/     Briques de régulation, calibrage, simulateur, boucle fermée
  FanOMax.Service.Tests/  Moteur du service et règles de sécurité (faux matériel), configuration, journal fantôme
scripts/
  install-service.ps1   Publie, installe et démarre le service (admin)
  uninstall-service.ps1 Arrête et supprime le service (admin)
docs/
  phase1-mesures.md     Protocole de mesure de la phase 1
  phase1-resultats.md   Résultats des mesures
  phase2-regulation.md  Moteur de régulation et validation
  phase3-service.md     Service, sécurité, installation, mode fantôme
  hardware-inventory.md Résultat de la sonde (généré)
captures/               CSV et analyses de la sonde (non versionné)
tools/                  Outils externes, ex. PresentMon (non versionné)
PLAN.md
TROUBLESHOOT.md
```

---

## 5. Algorithme de régulation (cible)

**Cible CPU : 65 °C (confort) / 70 °C (plafond visé).** Les ventilateurs du CPU et ceux du boîtier y participent.

> ✅ **Mesuré en phase 1** ([docs/phase1-resultats.md](docs/phase1-resultats.md)) :
> - Cinebench à 100 % de ventilation : **77,5–79 °C** pour 128 W, juste sous la **THM limit de 80 °C** : le CPU se limite lui-même. 70 °C est hors d'atteinte en charge lourde, même avec l'undervolt déjà en place.
> - Jeu : 69,6 °C à 49 % de ventilation pour 88 W, donc 70 °C **atteignable** avec un peu plus de ventilation (≈ 66 °C estimés à 65 %).
> - La température suit la puissance en **3 à 7 s** (T90 = 7 s) ; le ventirad lui-même a beaucoup de marge (+2 à 3 °C en 10 min).

**Modèle statique mesuré** (RMS 1,9 °C, coefficient du ventilateur à confirmer) :
`T_cpu ≈ 36,8 + 0,447·P_cpu(W) − 0,153·Ventilo(%)`

Comme la température est presque une fonction directe de la puissance, l'anticipation devient un **calcul de modèle**, et le PID un **correcteur lent (PI)**. Pour chaque ventilateur ou groupe de ventilateurs, à chaque cycle (1 s) :

```
P_filtrée  = moyenne(P_cpu, 20–30 s)             // ignore les pics de boost
anticip.   = (a + b·P_filtrée − T_cible) / c     // ventilation requise selon le modèle statique
T_filtrée  = EMA(T_brute)                        // lisse les pics du 5800X
erreur     = T_filtrée − T_cible
PI         = Kp·erreur + Ki·∫erreur              // corrige l'erreur résiduelle du modèle, lentement
             - anti-windup : intégrale bornée et gelée à la saturation
             - pas de terme dérivé (Kd = 0) : température trop rapide et trop bruitée
sortie     = clamp(anticip. + PI, min%, max%)
sortie     = limite_de_pente(sortie)             // confort acoustique : X %/s max
           + hystérésis à la descente
```

Si la puissance dépasse ce que le modèle peut tenir à 100 % (≈ 108 W pour 70 °C), le moteur plafonne à 100 % **sans s'emballer** et le signale (cible physiquement hors d'atteinte).

**Régime limité thermiquement** (Tctl ≥ THM limit − 3 °C, de façon soutenue) : le CPU tient lui-même sa température en réduisant son boost, donc la ventilation n'agit plus sur les degrés mais sur les **fréquences**. Le PI est alors suspendu et la ventilation suit le **profil** :
- **Silence** : niveau modéré, le CPU tient 80 °C avec un peu moins de boost ;
- **Perf** : ventilation forte pour garder les fréquences maximales.

**GPU :** cible = **point chaud 80–85 °C** (mesuré : 69 °C max en jeu, donc forte marge pour réduire le bruit du ventilateur GPU).

Les paramètres sont réglés d'abord sur le **simulateur** calé sur les CSV de la phase 1, puis affinés en **mode fantôme** (voir phase 3).

---

## 6. Règles de sécurité (non négociables)

1. **Rendre la main au BIOS** (`IControl.SetDefault()`) si :
   - un capteur utilisé est perdu ou renvoie une valeur aberrante (NaN, < 0 °C, > 110 °C, figée) ;
   - une exception non gérée survient dans la boucle ;
   - le **watchdog** détecte une boucle bloquée (> 5 s sans cycle) ;
   - le service s'arrête normalement.
2. **Seuil critique** : si une température dépasse la limite critique (CPU 90 °C par exemple), tout passe à 100 %, quel que soit le mode.
3. **Pas d'écriture PWM tant que FanControl tourne** : le service le détecte et refuse de démarrer en mode écriture.
4. **Mode lecture seule par défaut** : l'écriture doit être activée explicitement dans la config.
5. Vérifier une fois que les **courbes Smart Fan de l'UEFI** sont correctes, car c'est notre filet de sécurité.

> ⚠️ Si le processus est tué brutalement, LHM ne peut pas restaurer le BIOS : les PWM restent figés sur leur dernière valeur **jusqu'au redémarrage**. Voir TROUBLESHOOT.md.

---

## 7. Phases

Légende : `[ ]` à faire · `[~]` en cours · `[x]` fait

### Phase 0 : mise en place
- [x] `git init`, `.gitignore` (.NET), `Directory.Build.props` (net10.0, nullable, warnings traités en erreurs)
- [x] Versions NuGet centralisées (`Directory.Packages.props`)
- [x] Création de la solution et des projets (structure §4)
- [x] Référence NuGet LibreHardwareMonitorLib 0.9.6 (même version que FanControl, basée sur PawnIO)
- [x] Le build passe (0 avertissement), les tests vides passent, le service et l'interface démarrent
- [x] Vérifié : aucun `WinRing0*.sys` dans les fichiers produits par le build
- [x] Premier commit et push vers GitHub

### Phase 1 : sonde en lecture seule (`FanOMax.Probe`)
> Compatible avec FanControl en cours d'exécution. Nécessite les droits admin.
> Protocole de mesure : [docs/phase1-mesures.md](docs/phase1-mesures.md)

**Code (fait)**
- [x] `LhmMonitor` (FanOMax.Hardware) : accès LHM en lecture seule, n'expose jamais `IControl`
- [x] `inventory` : matériel, capteurs, contrôles PWM (mode, %, ventilateur associé) → `docs/hardware-inventory.md`
- [x] `record` : CSV à 1 Hz (températures, puissance, charge, RPM, % PWM) + statut en direct, Ctrl+C ou `--duration`
- [x] `record --fps` : FPS via PresentMon (`tools\PresentMon*.exe`), application au premier plan détectée automatiquement
- [x] `analyze` : statistiques, verdict sur la cible de 70 °C, **réponse thermique aux échelons de puissance** (hausse immédiate, T50/T63/T90, °C/W)
- [x] Core : `KeySensorResolver`, `StepResponseAnalyzer`, `SeriesStats`, `FpsCounter` + 26 tests unitaires
- [x] Validé de bout en bout sur un CSV synthétique (`analyze`) et le refus sans droits admin

**Mesures (à faire par l'utilisateur, en admin)**
- [x] Inventaire réel ([docs/hardware-inventory.md](docs/hardware-inventory.md)) : les 7 capteurs clés sont trouvés
- [x] Captures : repos (10 min), Cinebench avec ventilateurs à 100 % (15 min), jeu avec FPS (20 min)
- [x] Délai entre puissance et température : **T50 = 3 s, T63 = 4 s, T90 = 7 s** (51 % de la hausse en 3 s)
- [x] Vérifier la lecture des **ventilateurs du GPU** (RX 6750 XT) : RPM lu, contrôle exposé
- [ ] Associer chaque canal (Fan #1, #2, #7) à son ventilateur physique
- [x] Verdict 70 °C : **impossible en charge lourde** (78–79 °C à 100 %), **atteignable en jeu**
- [x] FPS lus pendant un jeu : F1Manager24.exe détecté, 159 FPS en moyenne
- [ ] (Optionnel, recommandé) **Balayage de ventilation à puissance constante** (30 / 60 / 100 %, 5 min chacun) pour confirmer l'effet des ventilateurs
- **Livrable :** [docs/phase1-resultats.md](docs/phase1-resultats.md)

### Phase 2 : moteur de régulation (`FanOMax.Core`)
> Détail et résultats : [docs/phase2-regulation.md](docs/phase2-regulation.md)
- [x] Filtres (EMA 10 s, moyenne de puissance 25 s) et garde-fou capteurs (absent, hors plage, figé ; maintien 3 s puis « perdu »)
- [x] Courbe classique (points interpolés) avec hystérésis
- [x] PI avec anti-windup (intégrale bornée et gelée à la saturation) ; dérivée disponible mais désactivée
- [x] Anticipation par **modèle statique** T = a + b·P − c·Ventilo, sur puissance filtrée
- [x] Calibrage du modèle (régression) : commande `calibrate` de la sonde, **en excluant les points proches de la THM limit**
- [x] Détection du régime limité thermiquement, ventilation selon le profil dans ce régime
- [x] Sonde : fréquence CPU effective enregistrée (« Cores (Average Effective) »)
- [x] Limiteur de pente (+5 %/s, −1 %/s), zone morte 1 %, ventilation minimale
- [ ] ~~Vitesse de démarrage~~ : inutile tant que la ventilation minimale reste > 0 (ventilateurs jamais arrêtés)
- [x] **Simulateur thermique** à deux nœuds (puce + ventirad) avec THM limit, validé contre les captures réelles (jeu : 0,56 °C d'erreur)
- [x] Profils Silence / Normal / Perf pour le CPU et le GPU (modèle GPU calibré sur la capture de jeu)
- [x] 65 tests : briques, calibrage, simulateur, **boucle fermée sur les vraies séries de puissance** (cible tenue à ±0,2 °C en jeu, sans yoyo), robustesse à l'incertitude du modèle, défauts capteurs
- [ ] GPU en boucle fermée : à valider en mode fantôme (phase 3), le simulateur étant calé sur le CPU
- **Livrable :** moteur testé, réglages initiaux validés sur le simulateur ✅

### Phase 3 : service (`FanOMax.Service` + `FanOMax.Hardware`)
> Détail : [docs/phase3-service.md](docs/phase3-service.md)
- [x] `IHardwareBackend` + `LhmBackend` (lecture/écriture) ; la sonde garde un accès strictement en lecture seule
- [x] Boucle de régulation à 1 Hz (PeriodicTimer, dt mesuré), groupes CPU et GPU
- [x] Config JSON commentée, générée au premier démarrage depuis le matériel détecté, rechargée à chaud, avec validation (config invalide refusée)
- [x] **Mode fantôme** (par défaut) : consignes calculées, jamais écrites, journalisées avec le % réellement appliqué (`shadow-AAAAMMJJ.csv`)
- [x] Failsafe complet (§6) : capteur perdu → BIOS puis reprise après 30 s, température critique → 100 %, erreurs → BIOS, arrêt → BIOS
- [x] Watchdog sur thread dédié (boucle bloquée > 5 s → BIOS)
- [x] Détection de FanControl : aucune écriture tant qu'il tourne, et jamais de retour au BIOS sous ses pieds
- [x] Journaux Serilog (14 jours) avec résumé par minute
- [x] Scripts d'installation / désinstallation, récupération automatique (2 redémarrages)
- [x] Sonde : `shadow-report`, bilan FanOMax contre FanControl
- [x] 38 tests du service (faux matériel) : 105 tests au total
- [x] Correctif après le premier démarrage : température estimée en mode fantôme (le PI s'emballait en boucle ouverte)
- [x] Correctifs après 2 jours de mode fantôme : réveil après veille (watchdog et pas de temps), journal en échec sans arrêt de la régulation (disque plein), pas de détection « figée » sur le GPU (capteurs entiers)
- [x] Raccourci `probe.cmd` à la racine du projet ; `shadow-report` ignore les journaux de l'ancien format
- [x] Démarrage en douceur (fait en phase 7)
- [ ] ~~Collecte des FPS par le service~~ : déplacée en phase 6 (utile seulement à l'affichage)
- [x] **Installation par l'utilisateur et mode fantôme** : 7,8 h de données sur 2 jours, bilan fait ; 3 incidents corrigés
- **Livrable :** service qui tourne en mode fantôme pendant plusieurs jours sans erreur

### Phase 4 : IPC
- [ ] Contrats (`FanOMax.Contracts`) : état temps réel, lecture/écriture config, commandes (profil, mode)
- [ ] Serveur named pipe + StreamJsonRpc ; ACL : administrateurs + utilisateur interactif
- [ ] Notifications poussées (état toutes les secondes)

### Phase 5 : historique (SQLite)
- [ ] Schéma : échantillons à 1 s, agrégats par minute
- [ ] Écriture par lots (pas une transaction par seconde)
- [ ] Agrégation au-delà de 24 h, conservation 30 jours (réglable)
- [ ] Requêtes : plage temporelle, min/max/moyenne, temps passé au-dessus d'un seuil

### Phase 6 : interface (`FanOMax.App`, Avalonia)
- [ ] Icône dans la barre des tâches : état (OK / fantôme / failsafe), profil actif, ouvrir/quitter
- [ ] Tableau de bord : températures, charge, W, RPM, % PWM, **FPS** en temps réel (ScottPlot)
- [ ] Collecte des FPS (PresentMon) pour l'affichage, reprise de la sonde (déplacé depuis la phase 3)
- [ ] **Mini-widget** toujours au premier plan, déplaçable et semi-transparent : temp. et charge CPU/GPU, FPS
  - Visible par-dessus les jeux en **plein écran fenêtré** (pas en plein écran exclusif)
- [ ] Éditeur : courbe (points déplaçables), paramètres PID, anticipation, limites
- [ ] Vue historique : plage temporelle, superposition consigne / mesure
- [ ] Profils : Silence / Normal / Perf (+ horaires en option)
- [ ] Indicateur clair quand le BIOS a repris la main, et pourquoi

### Phase 7 : bascule depuis FanControl
> Avancée avant les phases 4 à 6 (décision du 2026-10-09). Procédure : [docs/phase7-bascule.md](docs/phase7-bascule.md)

**Code (fait)**
- [x] Démarrage en douceur : à chaque prise de main, départ de la ventilation appliquée, maintenue pendant le préchauffage (25 s) sauf si T > cible + 3 °C ; pas de « cible inatteignable » pendant le préchauffage
- [x] `FixedPercent` par groupe (identification, tests), température critique prioritaire, plancher 20 %
- [x] Fichiers de configuration par étape (`docs/phase7/*.json`), validés par un test automatique
- [x] 116 tests au total

**Bascule (utilisateur)**
- [ ] Mise à jour du service, configuration de départ (fantôme, cible CPU 69 °C)
- [ ] Contrôle des courbes Smart Fan dans l'UEFI, relevé des prises (CPU_FAN1, CHA_FAN…)
- [ ] FanControl fermé et démarrage automatique désactivé (fermé depuis le 2026-10-09)
- [ ] Identification des canaux Fan #1, #2, #7 (ventilation fixe à 100 %, un canal à la fois)
- [ ] Écriture activée sur **un seul canal de boîtier** (hub), observer 24 h
- [ ] Puis tout le groupe CPU (hub + ventirad)
- [ ] Puis le GPU
- [ ] Test de stress : Cinebench 30 min + jeu 1 h, sans oscillation ni dépassement
- [ ] Tests du failsafe : arrêt du service (retour au BIOS), reprise en douceur au redémarrage

### Phase 8 : finitions
- [ ] Démarrage automatique de l'interface à l'ouverture de session
- [ ] Installeur (script PowerShell ou Inno Setup)
- [ ] README d'utilisation

---

## 8. Journal des décisions

| Date | Décision | Raison |
|---|---|---|
| 2026-10-07 | Outil maison plutôt qu'un plugin FanControl | L'API plugin ne permet que d'ajouter des capteurs ou des contrôles, pas une logique de régulation (PID, prédiction) |
| 2026-10-07 | .NET 10 + LibreHardwareMonitorLib + PawnIO | Seule bibliothèque mature, déjà validée sur la machine via FanControl |
| 2026-10-07 | Pas de web ni d'accès distant | Besoin uniquement local, d'où une surface d'attaque nulle |
| 2026-10-07 | Avalonia plutôt que WPF | Développement actif, rendu Skia fluide, liaisons compilées, thème moderne |
| 2026-10-07 | Service et interface séparés, named pipes | Droits admin pour le service, régulation indépendante de l'interface |
| 2026-10-07 | Failsafe = rendre la main au BIOS | Comportement prévisible, défini par l'UEFI |
| 2026-10-07 | Prédiction sur la **puissance (W)** plutôt que la charge (%) | Sur Ryzen, la puissance est l'indicateur le plus direct de la chaleur à venir |
| 2026-10-07 | Pas de détection de jeu | Pas nécessaire, la prédiction couvre le besoin |
| 2026-10-07 | Cible CPU 65 °C (confort) / 70 °C (plafond) | Choix utilisateur, à confronter aux mesures de la phase 1 |
| 2026-10-07 | FPS via PresentMon (ETW), pas d'overlay injecté | Fonctionne avec tous les jeux et GPU, sans risque anti-cheat |
| 2026-10-07 | Affichage : tableau de bord + mini-widget toujours au premier plan | Visible en jeu (plein écran fenêtré) sans injection |
| 2026-10-07 | Nom du projet : **FanOMax** | Aligné sur le repo GitHub |
| 2026-10-07 | Core et Contracts en `net10.0`, le reste en `net10.0-windows` | La logique pure reste portable et testable sans matériel |
| 2026-10-07 | Avalonia 12.1.3 + ScottPlot.Avalonia 5.1.59 | ScottPlot 5.1.59 cible explicitement Avalonia 12 |
| 2026-10-07 | Régulation = **anticipation par modèle statique + PI lent**, sans terme dérivé | Mesuré : la température suit la puissance en 3–7 s, avec peu d'inertie du ventirad (phase 1) |
| 2026-10-07 | Cible 70 °C **non tenable en charge lourde** : plafond à 100 % sans emballement | Mesuré : 78–79 °C sous Cinebench à 100 % de ventilation, plafonné par la THM limit (80 °C) |
| 2026-10-07 | Régime limité thermiquement : ventilation selon le profil (Silence / Perf), PI suspendu | À la THM limit, la ventilation agit sur les fréquences, plus sur les degrés |
| 2026-10-07 | Cible GPU : **point chaud 80–85 °C** | Choix utilisateur ; GPU largement sur-refroidi aujourd'hui (69 °C max en jeu) |
| 2026-10-07 | Profils : Silence 70 °C / 50 %, Normal 67 °C / 75 %, Perf 65 °C / 100 % (cible CPU / ventilation en régime limité) ; GPU 85 / 82 / 78 °C | Encadre la plage 65–70 °C demandée ; Normal par défaut. Ajustables |
| 2026-10-07 | Modèle CPU conservé à c = 0,153 malgré le calibrage à 0,085 (sans les points près de la THM limit) | Régulateur testé stable et précis dans les deux cas ; à trancher par un balayage de ventilation |
| 2026-10-07 | Perte de la température : 100 % immédiatement (régulateur), puis retour au BIOS (service) | Sécurité d'abord, sans rampe |
| 2026-10-07 | FanOMax ne rend au BIOS **que** les sorties qu'il a lui-même pilotées ; jamais en mode fantôme | Un retour au BIOS sur une sortie pilotée par FanControl lui retirerait la main |
| 2026-10-07 | Journal des décisions en CSV quotidien (7 jours) en attendant SQLite (phase 5) | Simple, lisible, suffisant pour le bilan du mode fantôme |
| 2026-10-07 | Groupe CPU = Fan #1, #2, #7 (ventilateurs qui tournent), pilotés au même % | Comme FanControl aujourd'hui ; le hub et le ventirad seront distingués en phase 7 si besoin |
| 2026-10-07 | Arrêt sur erreurs matérielles avec code de sortie 1 | Windows relance le service (2 tentatives), le BIOS garde la main entre-temps |
| 2026-10-07 | Mode fantôme : régulateur alimenté par une **température estimée** (mesure + c × écart de ventilation, filtré 30 s) | Sans cela, boucle ouverte et PI emballé vers 90 % (incident du premier démarrage, TROUBLESHOOT.md §5) |
| 2026-10-09 | Le journal des décisions ne peut jamais interrompre la régulation | Incident disque plein du 2026-10-08 : le service s'était arrêté |
| 2026-10-09 | Pause > 10 s (et > 5 intervalles) = veille : régulateurs réinitialisés, watchdog ignoré | Incident du réveil du 2026-10-08 (pas de temps de 83 298 s) |
| 2026-10-09 | Pas de détection « valeur figée » sur le GPU | Capteurs entiers stables au repos (692 s mesurés) : fausses alertes |
| 2026-10-09 | **Cible CPU : 69 °C** (`TargetTemperature` du groupe CPU dans `config.json`), profil Normal conservé pour le reste | Choix utilisateur après le bilan fantôme : Normal (67 °C) ventilait jusqu'à 76 % en charge contre 52 % avec FanControl ; 69 °C garde un niveau sonore proche de l'actuel |

---

## 9. Questions ouvertes
- [~] Le contrôle des ventilateurs de la RX 6750 XT via LHM fonctionne-t-il, ou faut-il ADLX ? Lecture OK et contrôle exposé (phase 1) ; l'écriture sera testée en phase 7
- [~] Quel ventilateur physique sur chaque canal ? Les ventilateurs de boîtier sont sur un **hub**. Reste à savoir quel canal porte le hub et lequel porte le ventirad, parmi Fan #1 (max ≈ 3 060 RPM), Fan #2 (max ≈ 1 670 RPM) et Fan #7 (max ≈ 1 500 RPM). Canaux 3, 4, 5 : probablement vides. À confirmer en phase 7 en faisant varier chaque PWM.
- [ ] Capteurs carte mère à ignorer : `Temperature #3` (8 °C) et `#5` (12 °C) sont des entrées non branchées. Le failsafe ne doit jamais s'appuyer dessus.
- [x] Délai entre puissance et température : T90 = 7 s (phase 1)
- [x] Type de refroidissement CPU : **ventirad**
- [x] Cible CPU : **65 / 70 °C**
- [x] Cible GPU : **point chaud 80–85 °C**
- [x] La cible de 70 °C est-elle atteignable en charge lourde ? **Non** (78–79 °C à 100 %, plafonné par la THM limit) ; oui en jeu
- [x] Undervolt : **déjà en place** (Curve Optimizer −16 à −30)
- [x] Réglages persistants : **oui**, Hydra se lance au démarrage de Windows et les applique
- [ ] Coût en performance de la THM limit à 80 °C : comparer le score Cinebench (3 384 à 80 °C) et la fréquence effective avec une limite plus haute (décision utilisateur, hors FanOMax). La sonde enregistre désormais la fréquence CPU
- [ ] Effet réel des ventilateurs sur le CPU (c = 0,085 ou 0,153 °C/%) : balayage de ventilation, voir [docs/phase1-mesures.md](docs/phase1-mesures.md) §6
