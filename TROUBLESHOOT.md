# FanOMax : dépannage

> Guide de diagnostic et journal des incidents.
> **Règle :** chaque bug rencontré est ajouté au §5 (journal), et à la §4 s'il peut se reproduire.

---

## 1. 🚨 Urgence : ventilateurs bloqués ou PC qui chauffe

À faire dans l'ordre :

1. **Arrêter le service proprement**, ce qui rend au BIOS les sorties pilotées par FanOMax (terminal admin) :
   ```powershell
   Stop-Service FanOMax
   ```
   En **mode fantôme**, FanOMax ne pilote rien : le problème vient alors de FanControl ou du BIOS, pas de FanOMax.
2. Si le service ne répond pas ou si les ventilateurs restent figés : **redémarrer le PC**. Le redémarrage remet la puce Nuvoton sous le contrôle du BIOS.
3. En dépannage temporaire, relancer **FanControl** (Rem0o), qui reste installé.
4. Récupérer les journaux (§2) **avant** de relancer FanOMax.

> Pourquoi un redémarrage ? Si le processus est tué brutalement (plantage dur, `taskkill /f`), LHM n'a pas le temps d'appeler `SetDefault()`. Les registres PWM gardent alors leur dernière valeur jusqu'au reset de la carte.

---

## 2. Emplacements utiles

| Quoi | Où |
|---|---|
| Config (rechargée à chaud) | `C:\ProgramData\FanOMax\config.json` |
| Journaux du service (14 jours) | `C:\ProgramData\FanOMax\logs\fanomax-AAAAMMJJ.log` |
| Journal des décisions (7 jours) | `C:\ProgramData\FanOMax\shadow\shadow-AAAAMMJJ.csv` |
| Binaires du service | `C:\Program Files\FanOMax\Service\` |
| Historique (SQLite, phase 5) | `C:\ProgramData\FanOMax\history.db` |
| Journaux de l'interface | `%LOCALAPPDATA%\FanOMax\logs\` |
| Driver PawnIO | `C:\Program Files\PawnIO\` |
| FanControl (référence) | `C:\Program Files (x86)\FanControl\` |

---

## 3. Commandes de diagnostic

```powershell
# État du service
Get-Service FanOMax

# Dernières lignes du journal du service (fichier du jour)
Get-ChildItem "C:\ProgramData\FanOMax\logs\fanomax-*.log" | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content -Tail 100

# Erreurs et avertissements uniquement
Select-String -Path "C:\ProgramData\FanOMax\logs\*.log" -Pattern "\[(ERR|FTL|WRN)\]" | Select-Object -Last 50

# Changements d'état des groupes (SensorLost, ThermalLimited, Bios, Critical…)
Select-String -Path "C:\ProgramData\FanOMax\logs\*.log" -Pattern "Groupe .* → " | Select-Object -Last 30

# Bilan du mode fantôme (depuis le dossier du projet)
.\probe shadow-report --days 1

# (Ré)installer / désinstaller le service (terminal admin)
powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
powershell -ExecutionPolicy Bypass -File scripts\uninstall-service.ps1

# Lancer le service en console pour le déboguer (terminal admin, service arrêté)
Stop-Service FanOMax; & "C:\Program Files\FanOMax\Service\FanOMax.Service.exe"

# FanControl tourne-t-il ? (conflit d'écriture PWM)
Get-Process FanControl -ErrorAction SilentlyContinue

# Le driver PawnIO est-il présent ?
Test-Path "C:\Program Files\PawnIO\PawnIOLib.dll"

# Session admin ?
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Sonde en lecture seule (inventory / record : admin requis ; analyze : non)
dotnet run --project src\FanOMax.Probe -- inventory
dotnet run --project src\FanOMax.Probe -- record --label test --duration 1m
dotnet run --project src\FanOMax.Probe -- analyze captures\<fichier>.csv

# Sessions ETW actives (FPS : chercher « FanOMaxProbe », session orpheline)
logman query -ets
```

---

## 4. Problèmes connus et anticipés

Statut : 🔮 anticipé (pas encore rencontré) · 🐛 rencontré · ✅ corrigé dans le code

### 4.1 Aucun capteur de carte mère / aucun contrôle de ventilateur — 🔮
- **Symptôme :** seuls les capteurs CPU et GPU apparaissent, pas de Nuvoton ni de contrôle PWM.
- **Causes probables :**
  1. Processus lancé sans droits admin.
  2. Driver PawnIO absent ou bloqué.
  3. Version de LHM trop ancienne (avant PawnIO, elle utilisait WinRing0).
- **Correctifs :** lancer en admin ; vérifier §3 (PawnIO) ; aligner la version de LibreHardwareMonitorLib.

### 4.2 Les ventilateurs « se battent » (vitesse qui saute) — 🔮
- **Cause :** FanControl et FanOMax écrivent tous les deux les PWM.
- **Correctif :** fermer FanControl et désactiver son démarrage automatique. FanOMax doit refuser le mode écriture quand FanControl tourne (règle de sécurité n° 3) : si ce n'est pas le cas, c'est un bug.

### 4.3 Windows Defender signale un driver vulnérable — 🔮
- **Cause :** une dépendance embarque encore **WinRing0** (CVE-2020-14979).
- **Correctif :** n'utiliser qu'une version de LHM basée sur PawnIO ; vérifier qu'aucun `WinRing0*.sys` n'est présent dans les fichiers produits par le build.

### 4.4 Température CPU qui saute de ±10 °C en une seconde — 🔮 (normal sur 5800X)
- **Cause :** comportement normal des Ryzen (boost d'un seul cœur, capteur Tctl très réactif).
- **Correctif :** ce n'est pas un bug. Le filtre EMA doit absorber ces sauts. Si les ventilateurs réagissent quand même : augmenter la constante du filtre ou réduire `Kd`.

### 4.5 La régulation oscille (ventilateurs qui montent et descendent en boucle) — 🔮
- **Référence :** en simulation sur la vraie série de jeu, la consigne varie de σ ≈ 2,7 % et parcourt ≈ 8 %/min (docs/phase2-regulation.md §4). Nettement plus = anomalie.
- **Diagnostic :** dans l'historique, superposer température filtrée, anticipation (`Feedforward`), correction (`Correction`) et % PWM.
  - L'**anticipation** bouge beaucoup : la puissance varie trop vite. Allonger `PowerWindowSeconds` (25 s par défaut).
  - La **correction** oscille : gain PI trop fort. Baisser `Kp` (2 %/°C) de 30 %, puis `Ki` (0,05).
  - Petits sauts permanents : augmenter `Deadband` (1 %) ou baisser `MaxRisePerSecond`.
- `Kd` doit rester à 0 : la température du 5800X est trop rapide et bruitée.
- **Toujours** valider un nouveau réglage avec les tests de boucle fermée (`ClosedLoopTests`) avant de l'appliquer au matériel.

### 4.6 La cible n'est jamais atteinte / ventilation bloquée à 100 % — 🔮
- **Mode `TargetUnreachable`** : la puissance dépasse ce que le refroidissement peut tenir (≈ 108 W pour 70 °C à 100 %). Normal en charge lourde : relever la cible ou choisir un autre profil.
- **Mode `ThermalLimited`** : le CPU est à sa THM limit (80 °C) et se bride lui-même ; la ventilation suit le profil (Silence 50 %, Normal 75 %, Perf 100 %). C'est voulu.
- **Ni l'un ni l'autre, et la correction PI reste très positive** : le modèle surestime l'effet des ventilateurs. Recalibrer (`calibrate`, docs/phase2-regulation.md §6).
- L'anti-emballement est couvert par des tests (`PiController_DoesNotWindUpWhileSaturated`, `HeavyLoad_EntersThermalLimitedMode_ThenRecoversWithoutWindup`).

### 4.7 Un ventilateur s'arrête à bas régime — 🔮
- **Cause :** PWM sous le seuil de démarrage ou de maintien du ventilateur.
- **Correctif :** régler `minPercent` (maintien) et `startPercent` (démarrage), mesurés avec la sonde.

### 4.8 Ventilateurs du GPU (RX 6750 XT) non pilotables — 🔮 (risque connu)
- **Cause :** le contrôle des GPU AMD via LHM (ADL) est partiel sur RDNA2.
- **Pistes :** passer par ADLX (FanControl fournit `ADLXWrapper.dll`), ou laisser le GPU en mode automatique du driver AMD.

### 4.9 L'interface n'arrive pas à se connecter au service — 🔮
- **Symptôme :** « Service indisponible » ou accès refusé sur le named pipe.
- **Causes :** service arrêté ; ACL du pipe trop restrictive ; différence de version entre les contrats de l'interface et du service.
- **Correctifs :** `Get-Service FanOMax` ; vérifier l'ACL (administrateurs + utilisateur interactif) ; recompiler les deux avec la même version de `FanOMax.Contracts`.

### 4.10 Le failsafe se déclenche sans raison apparente — 🔮
- **Diagnostic :** le journal doit toujours indiquer **la raison** (capteur perdu, valeur aberrante, watchdog, exception). Si la raison manque, c'est un bug de journalisation à corriger en priorité.
- **Cause fréquente :** capteur « figé » mal détecté (valeur stable au repos considérée comme figée). Ajuster le seuil de détection.

### 4.11 La base SQLite grossit trop / « database is locked » — 🔮
- **Causes :** agrégation ou purge non exécutée ; écriture concurrente.
- **Correctifs :** vérifier la tâche d'agrégation ; mode WAL activé ; une seule connexion en écriture.

### 4.12 FPS absents ou à 0 — 🔮
- **Causes probables :**
  1. PresentMon introuvable ou bloqué (antivirus).
  2. Session ETW orpheline après un plantage (« session already exists »).
  3. Mauvais processus suivi (launcher au lieu du jeu).
- **Correctifs :**
  - Sonde : PresentMon est cherché dans `tools\PresentMon*.exe`, ou via `--presentmon <chemin>` ; l'option `--fps` est obligatoire.
  - Session orpheline : la sonde la nettoie au démarrage (`--stop_existing_session`). Sinon : `logman stop FanOMaxProbe -ets`.
  - Mauvais processus : le compteur retient l'application qui présente le plus d'images par seconde (colonne `fps_app` du CSV).

### 4.13 Le mini-widget n'apparaît pas par-dessus le jeu — 🔮 (limitation)
- **Cause :** le jeu est en **plein écran exclusif** : aucune fenêtre Windows ne peut s'afficher par-dessus.
- **Correctif :** passer le jeu en **plein écran fenêtré / sans bordure**, ou utiliser l'overlay d'AMD Adrenalin.

### 4.14 Sonde et FanControl en parallèle : valeurs incohérentes — 🔮
- **Contexte :** la sonde et FanControl utilisent tous deux LibreHardwareMonitor. Les accès à la puce Super I/O sont synchronisés par un mutex système partagé, donc la lecture simultanée est normalement sans risque.
- **Symptôme possible :** une valeur aberrante isolée (RPM à 0, température à 0 ou 255) dans un CSV.
- **Correctif :** ignorer les points isolés (l'analyse utilise des moyennes) ; si c'est fréquent, fermer FanControl le temps de la mesure (le BIOS reprend alors la main).
- **Rappel :** la sonde n'écrit **jamais** les PWM. Les % enregistrés dans les colonnes `control` sont ceux imposés par FanControl ou le BIOS.

### 4.15 « Droits administrateur requis » au lancement de la sonde — 🔮
- **Cause :** `inventory` et `record` accèdent au driver PawnIO, réservé aux administrateurs.
- **Correctif :** ouvrir le terminal avec « Exécuter en tant qu'administrateur ». `analyze` fonctionne sans droits particuliers.

### 4.16 Comportement thermique différent d'un jour à l'autre (Hydra) — 🔮
- **Contexte :** les réglages CPU (Curve Optimizer, PPT, THM limit 80 °C) sont appliqués par **Hydra** au démarrage de Windows, pas par le BIOS. Le modèle thermique de FanOMax a été calé avec Hydra actif.
- **Symptômes :** températures nettement plus hautes ou plus basses qu'avant à charge égale, régulation qui vise mal, CPU qui dépasse 80 °C (la THM limit n'est plus appliquée).
- **Causes probables :** Hydra ne s'est pas lancé (mise à jour, démarrage rapide de Windows, plantage), ou son profil a changé.
- **Correctifs :** vérifier que Hydra tourne (`Get-Process HYDRA`) et que son profil est le bon ; si les réglages ont changé volontairement, refaire les captures et recalibrer le modèle.
- **À surveiller :** Hydra et LibreHardwareMonitor dialoguent tous deux avec le gestionnaire interne du CPU (SMU). Un conflit d'accès est rare, mais pourrait donner des lectures CPU aberrantes ponctuelles ; si c'est le cas, le noter dans le journal (§5).

### 4.17 `calibrate` : « effet des ventilateurs non identifiable » — 🔮
- **Cause :** dans les captures, la ventilation a trop peu varié (σ < 5 %), ou l'effet ajusté est non physique (un ventilateur qui réchaufferait). Le coefficient `c` est alors repris du modèle actuel.
- **Correctif :** faire un balayage de ventilation à puissance constante (docs/phase1-mesures.md §6), puis relancer `calibrate` avec toutes les captures.

### 4.18 `calibrate` donne des coefficients très différents d'une fois à l'autre — 🔮
- **Causes :** trop peu de fenêtres stables (GPU surtout : sa puissance varie beaucoup en jeu), ou captures faites avec des réglages CPU différents (Hydra, §4.16).
- **Correctif :** combiner plusieurs captures longues faites avec les mêmes réglages ; vérifier dans `docs\thermal-model.md` le nombre de fenêtres retenues (≥ 50 conseillé) et l'erreur RMS (≤ 2 °C).

### 4.19 Le service ne démarre pas ou s'arrête aussitôt — 🔮
- **Diagnostic :** dernières lignes du journal (§3). Messages possibles :
  - « Droits administrateur requis » : le service ne tourne pas sous le compte SYSTEM. Réinstaller avec `install-service.ps1`.
  - « Impossible d'ouvrir LibreHardwareMonitor » : driver PawnIO absent ou bloqué (§4.1, §4.3). Windows relance le service deux fois, puis abandonne.
  - « Trop d'erreurs consécutives » : erreurs matérielles répétées ; l'exception est dans le journal juste au-dessus.
- **Rien dans le journal :** lancer le service en console (§3) pour voir l'erreur directement. Vérifier que le runtime .NET 10 est installé (`dotnet --list-runtimes`).

### 4.20 « Configuration refusée » ou « Groupe désactivé » dans le journal — 🔮
- **Configuration refusée :** `config.json` invalide (sortie PWM dans deux groupes, cible ≥ température critique, intervalle hors 0,5–5 s…). La configuration précédente reste active ; corriger le fichier et enregistrer.
- **Groupe désactivé :** un capteur ou une sortie PWM du groupe est introuvable (identifiant mal recopié, ou changement de matériel/pilote). Comparer avec `docs\hardware-inventory.md` (relancer `inventory` si besoin). Les autres groupes continuent de fonctionner.
- **JSON illisible :** le fichier accepte les commentaires `//`, mais une virgule ou un guillemet manquant empêche toute lecture. Pour repartir de zéro : supprimer `config.json` et redémarrer le service, qui en régénère un (en mode fantôme).

### 4.21 Mode `Active` configuré, mais FanOMax ne pilote rien — 🔮
- **Cause n° 1 :** FanControl tourne (journal : « FanControl est en cours d'exécution »). C'est voulu : FanOMax refuse d'écrire tant qu'il est présent. Fermer FanControl **et** désactiver son démarrage automatique.
- **Cause n° 2 :** groupe rendu au BIOS après une perte de capteur (état `Bios`) : reprise automatique après 30 s de valeurs valides.
- **Vérification :** le résumé par minute du journal indique `[pilotage]`, `[fantôme]` ou `[Active bloqué (FanControl)]`.

### 4.22 Le dossier `shadow` grossit — 🔮
- **Ordre de grandeur :** environ 15 à 20 Mo par jour (2 groupes, 1 ligne par seconde). Conservation par défaut : 7 jours.
- **Correctif :** baisser `ShadowLog.RetentionDays`, ou `ShadowLog.Enabled: false` une fois la phase de validation terminée.

### 4.23 En mode Active, un ventilateur ne change pas de vitesse — 🔮
- **Vérifier d'abord :** le journal indique `[pilotage]` (sinon §4.21) et l'état du groupe (`Fixed`, `Normal`… et non `Bios`).
- **Comparer** `decision_percent` et `applied_percent` dans le journal des décisions : si FanOMax écrit (colonne `written` = 1) mais que `applied_percent` ne suit pas, la puce ou le pilote ignore l'écriture.
- **Causes possibles :**
  1. Prise réglée en mode DC/PWM incompatible avec le ventilateur dans l'UEFI : essayer l'autre mode (`H/W Monitor`).
  2. Hub alimenté en SATA qui ne relaie que le signal PWM : vérifier que les ventilateurs du hub sont bien des modèles PWM (4 broches).
  3. GPU : le pilote AMD (Adrenalin, réglage de ventilation manuel) peut reprendre la main. Remettre la ventilation GPU en automatique dans Adrenalin.
- **Retour arrière immédiat :** `etape-0-fantome.json` (docs/phase7-bascule.md).

### 4.24 Pendant l'identification, aucun ventilateur n'accélère — 🔮
- **Canal vide ou mal identifié :** le régime (RPM) du canal doit monter vers son maximum en quelques secondes (`.\probe inventory` ou le journal). S'il monte sans ventilateur visible qui accélère, le ventilateur est peut-être déjà au maximum, ou caché (ventilateur de l'alimentation : non piloté par la carte mère).
- **Rien ne bouge, même le RPM :** voir §4.23.

---

## 5. Journal des incidents

> Ajouter les incidents les plus récents en haut. Copier le modèle ci-dessous.

```markdown
### AAAA-MM-JJ : titre court
- **Symptôme :** ce qui a été observé
- **Contexte :** phase / version / mode (lecture seule, fantôme, écriture) / charge en cours
- **Journaux :** extrait pertinent
- **Cause racine :** pourquoi c'est arrivé
- **Correctif :** ce qui a été changé (fichier, commit)
- **Prévention :** test ajouté, garde-fou, entrée ajoutée en §4 ?
```

### 2026-10-09 : pic à 84,9 °C au premier quart d'heure de pilotage
- **Symptôme :** premier pilotage réel du groupe CPU (cible 69 °C, jeu). Après 14 min impeccables (69,3 °C de moyenne, 40 % de ventilation), le Tctl est monté de 69 à **84,9 °C** en 25 s (09:55:30 → 09:55:58), puis est retombé quand la charge a baissé.
- **Journaux :** puissance stable (≈ 80 W, 72–93 W) ; ventilation 38 % → 52 % ; `filtered_temperature` 78 °C quand la mesure brute atteignait 85 °C ; état `Normal` tout du long.
- **Cause racine :** charge **concentrée sur un ou deux cœurs** (chargement, compilation de shaders…) : à puissance égale, le cœur le plus chaud (Tctl) monte bien plus. Le PI régule volontairement sur une température lissée sur 10 s (pour ignorer les pics de boost) : il a réagi trop lentement à une vraie montée de 30 s. Et FanOMax ventilait moins que le BIOS (38 % contre ≈ 49 %) : point de départ plus bas. Historique : 0 s au-dessus de 80 °C en 6,3 h sous FanControl, 6 s en 14 min sous FanOMax. La THM limit d'Hydra (80 °C) tolère de brefs dépassements.
- **Correctif :** **plancher de protection** sur une température lissée sur 2 s : 40 % à 74 °C, 70 % à 78 °C, 100 % à 82 °C (`RegulatorSettings.CpuProtectionCurve`). Inactif en régime limité thermiquement (le profil décide). Rejeu du vrai pic : ≥ 85 % de ventilation au sommet au lieu d'≈ 50 %.
- **Prévention :** tests `ProtectionFloorTests` sur la série réelle du pilotage (`Data/pilotage-pic-20261009.csv`) : le pic est couvert, le jeu normal n'est quasiment pas touché (2 s de déclenchement en 14 min, +3 %), un pic d'une seconde n'a pas d'effet.
- **Pas de danger à aucun moment :** seuil critique 90 °C non approché ; le 5800X est conçu pour fonctionner jusqu'à 90 °C.

### 2026-10-09 : fausses alertes « valeur figée » sur le GPU
- **Symptôme :** dans `shadow-report`, 23 alertes « Puissance : valeur figée » et 3 « Température : valeur figée » sur le groupe GPU, avec de brefs passages en `SensorLost`.
- **Contexte :** phase 3, mode fantôme, PC au repos.
- **Cause racine :** température et puissance GPU sont des **entiers** qui ne bougent pas au repos (mesuré dans les journaux : point chaud à 49 °C identique pendant **692 s**, puissance à 31 W pendant 134 s). Le seuil « figé » de 120 s, valable pour le Tctl du CPU (série identique la plus longue : 9 s), ne l'est pas pour le GPU. En mode Active, le GPU aurait été rendu au BIOS à tort.
- **Correctif :** détection « valeur figée » désactivée pour le GPU (`RegulatorSettings.Gpu`) ; une vraie panne reste détectée (valeur absente ou hors plage), et le GPU garde sa propre protection thermique. Conservée pour le CPU.
- **Prévention :** tests `GpuRegulator_ToleratesLongIdleWithIntegerReadings`, `CpuRegulator_StillDetectsAFrozenTemperature`.

### 2026-10-08 : service arrêté sur « Espace insuffisant sur le disque »
- **Symptôme :** service `FanOMax` arrêté le 2026-10-08 à 21:22 ; plus aucune donnée en mode fantôme.
- **Journaux :** `[ERR] Erreur dans la boucle de régulation (1/5) : sorties rendues au BIOS — System.IO.IOException: Espace insuffisant sur le disque. : '…\shadow\shadow-20261008.csv'` à `ShadowLog.Write`.
- **Cause racine :** le disque `C:` s'est rempli (2,6 % libres le 2026-10-09 ; les fichiers de FanOMax ne pèsent que 5 Mo). L'échec d'écriture du **journal de diagnostic** remontait comme une **erreur de régulation** : 5 erreurs consécutives, puis arrêt du service. Défaut de conception : un journal ne doit jamais interrompre la régulation.
- **Correctif :** l'échec du journal des décisions est journalisé une fois, l'écriture est suspendue 60 s puis retentée, la régulation continue (`RegulationEngine.WriteShadowLog`) ; le fichier est refermé proprement après une erreur.
- **Prévention :** test `ShadowLogFailure_DoesNotInterruptRegulation_AndIsRetried`.
- **À surveiller :** espace libre sur `C:` (hors FanOMax).

### 2026-10-08 : fausse alerte du watchdog au réveil de la veille
- **Symptôme :** `[FTL] Watchdog : boucle de régulation bloquée depuis 83298 s, retour au BIOS.` au réveil du PC, puis `La boucle de régulation est repartie`.
- **Cause racine :** pendant une mise en veille, aucun thread ne tourne mais l'horloge continue. Au réveil, le watchdog voyait 23 h sans cycle, et le premier cycle recevait un pas de temps de 83 298 s, ce qui sature d'un coup l'intégrale du PI et vide les filtres. En mode Active : retour au BIOS inutile et premières décisions faussées.
- **Correctif :** `LoopTiming.IsSuspendGap` détecte les pauses du système. Le watchdog ignore une pause pendant laquelle il n'a pas tourné lui-même ; la boucle réinitialise les régulateurs (`ResetRegulators`) au lieu d'appliquer le pas de temps géant.
- **Prévention :** tests `IsSuspendGap_DistinguishesSleepFromSlowCycles`, `ResetRegulators_ClearsTheAccumulatedCorrection`.

### 2026-10-07 : en mode fantôme, le régulateur CPU s'emballait vers 90 %
- **Symptôme :** dès le premier démarrage du service, en jeu, FanOMax décidait 87–90 % pour le CPU alors que FanControl appliquait 53 %, avec une correction PI qui ne cessait de croître (+22 % en une minute).
- **Contexte :** phase 3, mode fantôme, profil Normal (cible 67 °C), CPU à 70–74 °C pour 90 W sous les courbes de FanControl.
- **Journaux :** `shadow-20261007.csv` : `filtered_temperature` 70,5–71 °C, `feedforward` 66 %, `correction` +21,6 → +23,2 %, `decision_percent` 87–90 %, `applied_percent` 53 %.
- **Cause racine :** en mode fantôme la boucle est **ouverte** : la température mesurée résulte de la ventilation de FanControl et ne réagit jamais aux décisions de FanOMax. Le PI voyait une erreur permanente (70 > 67 °C) et intégrait jusqu'à la butée. Le bilan du mode fantôme aurait fortement surestimé la ventilation de FanOMax. Défaut de conception, non couvert par les tests (le faux matériel n'avait pas de cas « température durablement au-dessus de la cible »).
- **Correctif :** quand FanOMax ne pilote pas, le régulateur reçoit une **température estimée** : mesure + effet des ventilateurs du modèle × (ventilation appliquée − décision de FanOMax), filtrée sur 30 s. Nouvelle colonne `regulator_temperature` dans le journal ; `shadow-report` l'utilise. Au passage : un rechargement de configuration identique ne remet plus les régulateurs à zéro, et un changement de format du journal ouvre un nouveau fichier (`shadow-AAAAMMJJ-2.csv`).
- **Prévention :** tests `Shadow_DoesNotWindUp_AndRegulatesOnTheEstimatedTemperature`, `Reconfigure_WithIdenticalOptions_KeepsTheRunningRegulators`, `Write_StartsANewFile_WhenTheFormatOfTheDayFileChanged`.
- **Limite connue :** l'estimation dépend de l'effet des ventilateurs du modèle (incertain : 0,085 à 0,153 °C/% pour le CPU, voir docs/phase2-regulation.md §3). Le bilan du mode fantôme est donc une estimation, pas une mesure.
