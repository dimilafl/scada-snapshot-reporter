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

$hadTargetServerOverride = Test-Path Env:OT_SNAPSHOT_TARGET_SERVER
$previousTargetServerOverride = $env:OT_SNAPSHOT_TARGET_SERVER
if ($PerServer) {
    $env:OT_SNAPSHOT_TARGET_SERVER = $env:COMPUTERNAME
}

try {
    foreach ($script in $collectorScripts) {
        $index++
        Write-Host "[$index/$total] Running $($script.Name)..." -ForegroundColor Cyan
        $collectorArgs = @{
            ConfigPath = $ConfigPath
            OutputPath = $collectorOutputPath
        }
        try {
            if ($RedactPaths) {
                $collectorCommand = Get-Command -Name $script.FullName -ErrorAction Stop
                if ($collectorCommand.Parameters.ContainsKey('RedactPaths')) {
                    $collectorArgs.RedactPaths = $true
                }
            }

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
}
finally {
    if ($PerServer) {
        if ($hadTargetServerOverride) {
            $env:OT_SNAPSHOT_TARGET_SERVER = $previousTargetServerOverride
        }
        else {
            Remove-Item Env:OT_SNAPSHOT_TARGET_SERVER -ErrorAction SilentlyContinue
        }
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
