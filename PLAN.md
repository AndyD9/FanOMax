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
| Carte mère | ASRock B550 Pro4 | Super I/O Nuvoton, géré par LibreHardwareMonitor |
| CPU | AMD Ryzen 7 5800X | Températures très instables : il faut filtrer |
| Refroidissement CPU | Ventirad (air) | Forte inertie : réaction plus lente qu'une AIO, l'anticipation est d'autant plus utile |
| GPU | AMD Radeon RX 6750 XT | ⚠️ Contrôle des ventilateurs à valider (point le moins fiable) |
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
  FanOMax.Core/        Logique pure : PID, prédiction, courbes, modèles. Aucune dépendance au matériel.
  FanOMax.Hardware/    IHardwareBackend + implémentation LibreHardwareMonitor
  FanOMax.Contracts/   DTO et interfaces IPC partagés service ↔ interface
  FanOMax.Service/     Boucle de régulation, failsafe, IPC, SQLite, config
  FanOMax.App/         Interface Avalonia
  FanOMax.Probe/       Console de sonde en lecture seule (inventaire + enregistrement CSV)
tests/
  FanOMax.Core.Tests/  PID, prédiction, courbes, simulateur thermique
docs/
  hardware-inventory.md Résultat de la sonde
PLAN.md
TROUBLESHOOT.md
```

---

## 5. Algorithme de régulation (cible)

**Cible CPU : 65 °C (confort) / 70 °C (plafond visé).** Les ventilateurs du CPU et ceux du boîtier y participent.

> ⚠️ **Réalisme de la cible.** Un 5800X sous ventirad dépasse généralement 80 °C en charge maximale sur tous les cœurs (type Cinebench), même avec les ventilateurs à 100 %. 65–70 °C est réaliste **en jeu**, mais probablement pas en stress test, où le PID restera saturé à 100 % (donc bruyant).
> La phase 1 le mesurera. Si la cible est hors d'atteinte, les leviers sont **hors FanOMax**, dans le BIOS : PBO Curve Optimizer (undervolt), mode ECO 65 W ou limite PPT. On peut aussi accepter une cible plus haute en charge lourde, avec un profil « Perf ».

Pour chaque ventilateur ou groupe de ventilateurs, à chaque cycle (1 s) :

```
T_filtrée  = EMA(T_brute)                       // lisse les pics du 5800X
erreur     = T_filtrée - T_cible
PID        = Kp·erreur + Ki·∫erreur + Kd·d(T_filtrée)/dt
             - anti-windup : l'intégrale est bornée et gelée si la sortie est saturée
             - dérivée calculée sur la mesure, pas sur l'erreur, et filtrée
anticip.   = Kff · f(Puissance_W)                // feedforward : les watts annoncent la chaleur
sortie     = clamp(PID + anticip., min%, max%)
sortie     = limite_de_pente(sortie)            // confort acoustique : X %/s max
           + hystérésis à la descente
```

Les paramètres sont réglés d'abord sur le **simulateur** à partir des données réelles de la sonde, puis affinés en **mode fantôme** (voir phase 3).

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
- [ ] Inventaire : matériel, capteurs (type, identifiant, valeur), contrôles (identifiant, mode actuel)
- [ ] Export de l'inventaire vers `docs/hardware-inventory.md`
- [ ] Enregistrement CSV à 1 Hz : températures CPU/GPU, puissance CPU/GPU (W), charge, RPM, % PWM
- [ ] Captures : repos (10 min), Cinebench multi-cœur (10 min), jeu (20 min), retour au repos
- [ ] Vérifier que la **puissance du CPU** précède bien la température (calcul du délai)
- [ ] Vérifier la lecture des **ventilateurs du GPU** (RX 6750 XT)
- [ ] Mesurer la **température maximale atteinte** avec les ventilateurs à fond (Cinebench), pour savoir si la cible de 70 °C est atteignable
- [ ] Prototype FPS : lancer PresentMon et lire ses mesures en direct pendant un jeu
- **Livrable :** inventaire, fichiers CSV, délai mesuré entre puissance et température, verdict sur la cible de 70 °C, FPS lu

### Phase 2 : moteur de régulation (`FanOMax.Core`)
- [ ] Filtres : EMA, rejet des valeurs aberrantes
- [ ] Courbe classique (points interpolés) avec hystérésis
- [ ] PID avec anti-windup, dérivée sur la mesure et filtrée, sortie bornée
- [ ] Anticipation sur la puissance
- [ ] Limiteur de pente et vitesse minimale / vitesse de démarrage
- [ ] **Simulateur thermique** (modèle du 1er ordre calé sur les CSV de la phase 1)
- [ ] Tests unitaires : convergence, absence d'oscillation, saturation, valeurs aberrantes, rejeu des CSV réels
- **Livrable :** moteur testé, réglages initiaux validés sur le simulateur

### Phase 3 : service (`FanOMax.Service` + `FanOMax.Hardware`)
- [ ] `IHardwareBackend` + implémentation LHM
- [ ] Boucle de régulation à 1 Hz, horloge stable
- [ ] Config JSON rechargée à chaud, avec validation
- [ ] **Mode fantôme** : calcule les consignes sans les écrire et les journalise, à comparer avec FanControl
- [ ] Failsafe complet (§6) et watchdog
- [ ] Détection de FanControl (blocage du mode écriture)
- [ ] Collecte des FPS : PresentMon lancé par le service, suivi du processus 3D actif, remise à zéro quand aucun jeu ne tourne
- [ ] Journaux Serilog
- [ ] Scripts d'installation / désinstallation du service (`sc.exe` ou PowerShell)
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
- [ ] **Mini-widget** toujours au premier plan, déplaçable et semi-transparent : temp. et charge CPU/GPU, FPS
  - Visible par-dessus les jeux en **plein écran fenêtré** (pas en plein écran exclusif)
- [ ] Éditeur : courbe (points déplaçables), paramètres PID, anticipation, limites
- [ ] Vue historique : plage temporelle, superposition consigne / mesure
- [ ] Profils : Silence / Normal / Perf (+ horaires en option)
- [ ] Indicateur clair quand le BIOS a repris la main, et pourquoi

### Phase 7 : bascule depuis FanControl
- [ ] Contrôle des courbes Smart Fan dans l'UEFI
- [ ] Arrêter FanControl et désactiver son démarrage automatique
- [ ] Écriture activée sur **un seul ventilateur de boîtier**, observer 24 h
- [ ] Puis le ventilateur CPU, puis les autres ventilateurs de boîtier
- [ ] Puis le GPU (si validé en phase 1)
- [ ] Test de stress : Cinebench 30 min + jeu 1 h, sans oscillation ni dépassement
- [ ] Tests du failsafe : arrêt du service, débranchement simulé d'un capteur, processus tué

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

---

## 9. Questions ouvertes
- [ ] Le contrôle des ventilateurs de la RX 6750 XT via LHM fonctionne-t-il, ou faut-il ADLX ? (phase 1)
- [ ] Quel délai réel entre la puissance et la température sur ce 5800X et ce refroidissement ? (phase 1)
- [x] Type de refroidissement CPU : **ventirad**
- [x] Cible CPU : **65 / 70 °C**
- [ ] Cible GPU et niveau sonore acceptable ?
- [ ] La cible de 70 °C est-elle atteignable en charge lourde ? (mesure en phase 1)
