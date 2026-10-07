# FanOMax : dépannage

> Guide de diagnostic et journal des incidents.
> **Règle :** chaque bug rencontré est ajouté au §5 (journal), et à la §4 s'il peut se reproduire.

---

## 1. 🚨 Urgence : ventilateurs bloqués ou PC qui chauffe

À faire dans l'ordre :

1. **Arrêter le service proprement**, ce qui rend la main au BIOS :
   ```powershell
   Stop-Service FanOMax
   ```
2. Si le service ne répond pas ou si les ventilateurs restent figés : **redémarrer le PC**. Le redémarrage remet la puce Nuvoton sous le contrôle du BIOS.
3. En dépannage temporaire, relancer **FanControl** (Rem0o), qui reste installé.
4. Récupérer les journaux (§2) **avant** de relancer FanOMax.

> Pourquoi un redémarrage ? Si le processus est tué brutalement (plantage dur, `taskkill /f`), LHM n'a pas le temps d'appeler `SetDefault()`. Les registres PWM gardent alors leur dernière valeur jusqu'au reset de la carte.

---

## 2. Emplacements utiles

| Quoi | Où |
|---|---|
| Config | `C:\ProgramData\FanOMax\config.json` |
| Journaux du service | `C:\ProgramData\FanOMax\logs\` |
| Historique (SQLite) | `C:\ProgramData\FanOMax\history.db` |
| Journaux de l'interface | `%LOCALAPPDATA%\FanOMax\logs\` |
| Driver PawnIO | `C:\Program Files\PawnIO\` |
| FanControl (référence) | `C:\Program Files (x86)\FanControl\` |

---

## 3. Commandes de diagnostic

```powershell
# État du service
Get-Service FanOMax

# Dernières lignes du journal du service
Get-Content "C:\ProgramData\FanOMax\logs\*.log" -Tail 100

# Erreurs et avertissements uniquement
Select-String -Path "C:\ProgramData\FanOMax\logs\*.log" -Pattern "\[(ERR|FTL|WRN)\]" | Select-Object -Last 50

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

_(aucun incident pour l'instant)_
