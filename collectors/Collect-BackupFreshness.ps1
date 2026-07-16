param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run",
    [switch] $RedactPaths,
    [ValidateRange(1, 1000000)]
    [int] $MaxFiles = 50000
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$redactPathValues = [bool]$RedactPaths
$maxFiles = $MaxFiles
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        param($recordServer, $configPath, $redactPaths, $fileLimit)

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
                    $scan = Get-BoundedFiles -Path $expectedPath.path -MaxFiles $fileLimit
                    if ($scan.truncated) {
                        Write-Warning "Path '$($expectedPath.path)' contains more than $fileLimit files; truncating to $fileLimit for performance"
                    }

                    $newest = @($scan.files) |
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
                $result.path = "[redacted]"
                $result.newestFile = "[redacted]"
            }
            $result
        }
    } -ArgumentList $server, $ConfigPath, $redactPathValues, $maxFiles
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\backup_freshness.json')
