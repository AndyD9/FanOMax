#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Arrête et supprime le service Windows FanOMax.

.DESCRIPTION
    L'arrêt du service rend au BIOS les sorties PWM que FanOMax pilotait.
    La configuration, les journaux et le journal des décisions (C:\ProgramData\FanOMax) sont conservés,
    sauf avec -RemoveData.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\uninstall-service.ps1
    powershell -ExecutionPolicy Bypass -File scripts\uninstall-service.ps1 -RemoveData
#>
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'FanOMax\Service'),
    [switch] $RemoveData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'FanOMax'

$service = Get-Service $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Write-Host "Arrêt du service (retour au BIOS des sorties pilotées)..."
        Stop-Service $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    sc.exe delete $serviceName | Out-Null
    Write-Host "Service supprimé."
}
else {
    Write-Host "Service $serviceName absent."
}

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Fichiers supprimés : $InstallDir"
}

$data = Join-Path $env:ProgramData 'FanOMax'
if ($RemoveData -and (Test-Path $data)) {
    Remove-Item $data -Recurse -Force
    Write-Host "Données supprimées : $data"
}
elseif (Test-Path $data) {
    Write-Host "Données conservées : $data (utiliser -RemoveData pour les supprimer)"
}
