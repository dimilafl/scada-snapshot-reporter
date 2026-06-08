param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run",
    [switch] $PerServer,
    [switch] $RedactPaths
)

$ErrorActionPreference = 'Stop'

$collectorOutputPath = $OutputPath
if ($PerServer) {
    $collectorOutputPath = Join-Path $OutputPath (Join-Path $env:COMPUTERNAME (Get-Date -Format 'yyyy-MM-dd_HHmmss'))
}

$collectorScripts = @(Get-ChildItem -Path $PSScriptRoot -Filter 'Collect-*.ps1' | Sort-Object Name)
$total = $collectorScripts.Count
$index = 0
foreach ($script in $collectorScripts) {
    $index++
    Write-Host "[$index/$total] Running $($script.Name)..." -ForegroundColor Cyan
    $collectorArgs = @{
        ConfigPath = $ConfigPath
        OutputPath = $collectorOutputPath
    }
    if ($RedactPaths -and $script.Name -in @('Collect-BackupFreshness.ps1', 'Collect-OdbcOleDbDrivers.ps1', 'Collect-SoftwareInventory.ps1')) {
        $collectorArgs.RedactPaths = $true
    }

    try {
        & $script.FullName @collectorArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "  $($script.Name) exited with code $LASTEXITCODE"
        }
        else {
            Write-Host "  Done." -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "  $($script.Name) threw: $($_.Exception.Message)"
    }
}

if ($PerServer) {
    Write-Host "Per-server collection written to $collectorOutputPath"
}
