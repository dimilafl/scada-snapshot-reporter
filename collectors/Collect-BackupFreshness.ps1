param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run",
    [switch] $RedactPaths
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$redactPathValues = [bool]$RedactPaths
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        param($recordServer, $configPath, $redactPaths)

        $pathsFile = Join-Path $configPath 'expected_paths.json'
        $paths = @()
        if (Test-Path $pathsFile) {
            $paths = @((Get-Content -Path $pathsFile -Raw | ConvertFrom-Json).paths)
        }

        $now = Get-Date
        foreach ($expectedPath in $paths) {
            $exists = $false
            $newestFile = $null
            $newestWriteTime = $null
            $ageHours = $null
            $errorText = $null

            try {
                $exists = Test-Path -Path $expectedPath.path -ErrorAction Stop
                if ($exists) {
                    $allFiles = @(Get-ChildItem -Path $expectedPath.path -File -Recurse -Depth 4 -ErrorAction Stop)
                    if ($allFiles.Count -gt 50000) {
                        Write-Warning "Path '$($expectedPath.path)' contains $($allFiles.Count) files; truncating to 50000 for performance"
                    }

                    $newest = $allFiles |
                        Select-Object -First 50000 |
                        Sort-Object LastWriteTime -Descending |
                        Select-Object -First 1

                    if ($null -ne $newest) {
                        $newestFile = $newest.FullName
                        $newestWriteTime = $newest.LastWriteTime.ToString("s")
                        $ageHours = [math]::Round(($now - $newest.LastWriteTime).TotalHours, 2)
                    }
                }
            }
            catch {
                $errorText = $_.Exception.Message
            }

            $result = [pscustomobject]@{
                server = $recordServer
                name = $expectedPath.name
                path = $expectedPath.path
                maxAgeHours = $expectedPath.max_age_hours
                exists = $exists
                newestFile = $newestFile
                newestWriteTime = $newestWriteTime
                ageHours = $ageHours
                error = $errorText
            }
            if ($redactPaths) {
                $result.newestFile = "[redacted]"
            }
            $result
        }
    } -ArgumentList $server, $ConfigPath, $redactPathValues
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\backup_freshness.json')
