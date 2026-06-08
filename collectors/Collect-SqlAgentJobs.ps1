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
SELECT
    j.name,
    j.enabled,
    ISNULL(CONVERT(varchar(20), h.run_date), ''),
    ISNULL(CONVERT(varchar(20), h.run_time), ''),
    ISNULL(CONVERT(varchar(20), h.run_status), ''),
    ISNULL(CONVERT(varchar(20), h.run_duration), ''),
    REPLACE(REPLACE(ISNULL(h.message, ''), CHAR(13), ' '), CHAR(10), ' '),
    CONVERT(varchar(30), j.date_created, 126),
    CONVERT(varchar(30), j.date_modified, 126),
    ISNULL(SUSER_SNAME(j.owner_sid), '')
FROM msdb.dbo.sysjobs j
OUTER APPLY (
    SELECT TOP 1 run_date, run_time, run_status, run_duration, message
    FROM msdb.dbo.sysjobhistory
    WHERE job_id = j.job_id AND step_id = 0
    ORDER BY run_date DESC, run_time DESC
) h;
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

                    $parts = $line -split '\|', 10
                    if ($parts.Count -lt 10) {
                        continue
                    }

                    [pscustomobject]@{
                        server = $recordServer
                        instance = $instance
                        jobName = $parts[0].Trim()
                        enabled = $parts[1].Trim() -eq '1'
                        lastRunDate = $parts[2].Trim()
                        lastRunTime = $parts[3].Trim()
                        lastRunStatus = if ($parts[4].Trim()) { [int]$parts[4].Trim() } else { $null }
                        lastRunDuration = if ($parts[5].Trim()) { [int]$parts[5].Trim() } else { $null }
                        lastRunMessage = $parts[6].Trim()
                        dateCreated = $parts[7].Trim()
                        dateModified = $parts[8].Trim()
                        jobOwner = $parts[9].Trim()
                    }
                }
            }
            catch {
                continue
            }
        }
    } -ArgumentList $server
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\sql_agent_jobs.json')
