<#
.SYNOPSIS
    Compile et installe (ou met à jour) l'interface FanOMax pour l'utilisateur courant, avec démarrage automatique.

.DESCRIPTION
    - Ferme l'interface si elle tourne (ses fichiers seraient verrouillés).
    - Publie FanOMax.App dans "%LOCALAPPDATA%\Programs\FanOMax\App".
    - Crée un raccourci « FanOMax » dans le menu Démarrer.
    - Inscrit le lancement à l'ouverture de session (HKCU\...\Run), fenêtre réduite dans la barre des tâches.
    - Relance l'interface.

    Sans droits administrateur : l'interface ne fait que lire les fichiers du service (C:\ProgramData\FanOMax)
    et le journal Système de Windows. Le service s'installe à part (install-service.ps1, en admin).

.PARAMETER NoAutostart
    N'inscrit pas le lancement à l'ouverture de session (et retire une inscription existante).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install-app.ps1
#>
param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\FanOMax\App'),
    [switch] $NoAutostart
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\FanOMax.App\FanOMax.App.csproj'
$exe = Join-Path $InstallDir 'FanOMax.App.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'FanOMax'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'FanOMax.lnk'

$running = Get-Process 'FanOMax.App' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Fermeture de l'interface en cours d'exécution..."
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Write-Host "Publication de FanOMax.App vers $InstallDir ..."
dotnet publish $project -c Release -o $InstallDir --nologo
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué (code $LASTEXITCODE)." }

Write-Host "Raccourci du menu Démarrer : $shortcut"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $InstallDir
$link.Description = 'Tableau de bord FanOMax'
$link.Save()

if ($NoAutostart) {
    Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue
    Write-Host "Démarrage automatique : désactivé."
} else {
    Set-ItemProperty -Path $runKey -Name $runName -Value "`"$exe`" --minimized"
    Write-Host "Démarrage automatique : à l'ouverture de session, fenêtre réduite."
}

Write-Host "Lancement de l'interface..."
Start-Process $exe -WorkingDirectory $InstallDir
