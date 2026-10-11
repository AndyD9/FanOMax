<#
.SYNOPSIS
    Désinstalle l'interface FanOMax de l'utilisateur courant (le service n'est pas touché : uninstall-service.ps1).

.DESCRIPTION
    Ferme l'interface, retire le démarrage automatique et le raccourci du menu Démarrer, puis supprime
    "%LOCALAPPDATA%\Programs\FanOMax\App". Les données du service (C:\ProgramData\FanOMax) sont conservées.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\uninstall-app.ps1
#>
param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\FanOMax\App')
)

$ErrorActionPreference = 'Stop'

$running = Get-Process 'FanOMax.App' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Fermeture de l'interface..."
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'FanOMax' -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'FanOMax.lnk') -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    # Dossier parent laissé vide par l'interface seule.
    $parent = Split-Path $InstallDir -Parent
    if (-not (Get-ChildItem $parent -Force -ErrorAction SilentlyContinue)) { Remove-Item $parent }
}

Write-Host "Interface désinstallée (démarrage automatique et raccourci retirés)."
