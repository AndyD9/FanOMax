#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Compile et installe (ou met à jour) le service Windows FanOMax.

.DESCRIPTION
    - Publie FanOMax.Service dans "C:\Program Files\FanOMax\Service".
    - Crée le service "FanOMax" (compte SYSTEM, démarrage automatique) s'il n'existe pas.
    - Configure la récupération : redémarrage automatique après un arrêt anormal (2 tentatives).
    - Démarre le service.

    Au premier démarrage, le service crée C:\ProgramData\FanOMax\config.json en MODE FANTÔME :
    il calcule ses décisions sans jamais toucher aux ventilateurs.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
#>
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'FanOMax\Service')
)

$ErrorActionPreference = 'Stop'
$serviceName = 'FanOMax'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\FanOMax.Service\FanOMax.Service.csproj'

$existing = Get-Service $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Arrêt du service existant (les sorties pilotées par FanOMax sont rendues au BIOS)..."
    Stop-Service $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

Write-Host "Publication de FanOMax.Service vers $InstallDir ..."
dotnet publish $project -c Release -o $InstallDir --nologo
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué (code $LASTEXITCODE)." }

$exe = Join-Path $InstallDir 'FanOMax.Service.exe'
if (-not $existing) {
    Write-Host "Création du service $serviceName ..."
    New-Service -Name $serviceName `
        -BinaryPathName "`"$exe`"" `
        -DisplayName 'FanOMax' `
        -Description 'Régulation des ventilateurs (PI + anticipation sur la puissance). Voir C:\ProgramData\FanOMax.' `
        -StartupType Automatic | Out-Null
}

# Récupération : redémarrage 10 s puis 30 s après un arrêt anormal ; compteur remis à zéro chaque jour.
sc.exe failure $serviceName reset= 86400 actions= restart/10000/restart/30000 | Out-Null

Write-Host "Démarrage du service..."
Start-Service $serviceName
Start-Sleep -Seconds 3
Get-Service $serviceName | Format-Table Name, Status, StartType -AutoSize

$root = Join-Path $env:ProgramData 'FanOMax'
Write-Host "Configuration : $root\config.json"
Write-Host "Journaux      : $root\logs\"
Write-Host "Journal des décisions (mode fantôme) : $root\shadow\"
Write-Host ""
Write-Host "Dernières lignes du journal :"
Get-ChildItem (Join-Path $root 'logs') -Filter 'fanomax-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1 |
    ForEach-Object { Get-Content $_.FullName -Tail 15 }
