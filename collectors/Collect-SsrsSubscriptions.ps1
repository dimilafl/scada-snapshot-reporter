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

        $sqlCmd = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
        if (-not $sqlCmd) {
            return @()
        }

        $instances = @(".")
        $namedInstances = Get-Service -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'MSSQL$*' } |
            ForEach-Object { ".\$($_.Name -replace '^MSSQL\$', '')" }
        $instances += @($namedInstances)

        $query = @"
SET NOCOUNT ON;
IF DB_ID('ReportServer') IS NULL RETURN;
SELECT
    c.Path,
    ISNULL(s.Description, ''),
    ISNULL(u.UserName, ''),
    CASE WHEN u.UserName IS NULL OR u.UserName = '' THEN 0 ELSE 1 END,
    ISNULL(s.LastStatus, ''),
    ISNULL(CONVERT(varchar(30), s.LastRunTime, 126), ''),
    CASE WHEN s.InactiveFlags = 0 THEN 1 ELSE 0 END
FROM ReportServer.dbo.Subscriptions s
JOIN ReportServer.dbo.Catalog c ON s.Report_OID = c.ItemID
LEFT JOIN ReportServer.dbo.Users u ON s.OwnerID = u.UserID;
"@

        foreach ($instance in $instances | Select-Object -Unique) {
            try {
                $lines = & sqlcmd.exe -S $instance -E -Q $query -h -1 -W -s "|" 2>$null
                if ($LASTEXITCODE -ne 0 -or -not $lines) {
                    continue
                }

                foreach ($line in @($lines)) {
                    if ([string]::IsNullOrWhiteSpace($line) -or $line -match '^\(\d+ rows? affected\)$') {
                        continue
                    }

                    $parts = $line -split '\|', 7
                    if ($parts.Count -lt 7) {
                        continue
                    }

                    [pscustomobject]@{
                        server = $recordServer
                        instance = $instance
                        reportPath = $parts[0].Trim()
                        subscriptionDescription = $parts[1].Trim()
                        owner = $parts[2].Trim()
                        ownerExists = $parts[3].Trim() -eq '1'
                        lastStatus = $parts[4].Trim()
                        lastRunTime = $parts[5].Trim()
                        enabled = $parts[6].Trim() -eq '1'
                    }
                }
            }
            catch {
                continue
            }
        }
    } -ArgumentList $server
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\ssrs_subscriptions.json')
