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
$failedCollectors = New-Object System.Collections.Generic.List[string]
foreach ($script in $collectorScripts) {
    $index++
    Write-Host "[$index/$total] Running $($script.Name)..." -ForegroundColor Cyan
    $collectorArgs = @{
        ConfigPath = $ConfigPath
        OutputPath = $collectorOutputPath
    }
    if ($RedactPaths -and $script.Name -in @('Collect-BackupFreshness.ps1', 'Collect-FileShareReachability.ps1', 'Collect-OdbcOleDbDrivers.ps1', 'Collect-ScheduledTasks.ps1', 'Collect-SoftwareInventory.ps1')) {
        $collectorArgs.RedactPaths = $true
    }

    try {
        $LASTEXITCODE = 0
        & $script.FullName @collectorArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "  $($script.Name) exited with code $LASTEXITCODE"
            $failedCollectors.Add($script.Name)
        }
        else {
            Write-Host "  Done." -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "  $($script.Name) threw: $($_.Exception.Message)"
        $failedCollectors.Add($script.Name)
    }
}

if ($PerServer) {
    Write-Host "Per-server collection written to $collectorOutputPath"
}

if ($failedCollectors.Count -gt 0) {
    Write-Warning "Collection failed for: $($failedCollectors -join ', ')"
    exit 1
}

exit 0
