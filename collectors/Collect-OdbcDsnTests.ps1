param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run"
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        param($recordServer)

        function Test-OdbcConnection {
            param([string] $ConnectionSetting)

            if (-not $ConnectionSetting) {
                return $false
            }

            try {
                $conn = New-Object System.Data.Odbc.OdbcConnection("$ConnectionSetting;Connection Timeout=5")
                $conn.Open()
                $conn.Close()
                return $true
            }
            catch {
                return $false
            }
        }

        $results = New-Object System.Collections.Generic.List[object]
        $roots = @(
            @{ Path = 'HKLM:\Software\ODBC\ODBC.INI'; Architecture = '64-bit' },
            @{ Path = 'HKLM:\Software\WOW6432Node\ODBC\ODBC.INI'; Architecture = '32-bit' }
        )

        foreach ($root in $roots) {
            $sourcesPath = Join-Path $root.Path 'ODBC SourceNames'
            $sources = Get-ItemProperty -Path $sourcesPath -ErrorAction SilentlyContinue
            if (-not $sources) {
                continue
            }

            $sources.PSObject.Properties |
                Where-Object { $_.Name -notlike 'PS*' } |
                ForEach-Object {
                    $dsnName = $_.Name
                    $driverName = $_.Value
                    $dsnKey = Get-ItemProperty -Path (Join-Path $root.Path $dsnName) -ErrorAction SilentlyContinue
                    $connectionSetting = if ($dsnKey) { "DSN=$dsnName" } else { $null }
                    $passed = Test-OdbcConnection -ConnectionSetting $connectionSetting

                    $results.Add([pscustomobject]@{
                        server = $recordServer
                        dsnName = $dsnName
                        driverName = $driverName
                        type = 'System'
                        architecture = $root.Architecture
                        serverTarget = if ($dsnKey -and $dsnKey.Server) { $dsnKey.Server } else { $null }
                        database = if ($dsnKey -and $dsnKey.Database) { $dsnKey.Database } else { $null }
                        connectionPassed = $passed
                    })
                }
        }

        return $results
    } -ArgumentList $server
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\odbc_dsn_tests.json')
