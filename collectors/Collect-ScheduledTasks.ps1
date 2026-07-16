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
        param($recordServer, $redactPaths)

        Get-ScheduledTask | ForEach-Object {
            $info = Get-ScheduledTaskInfo -TaskName $_.TaskName -TaskPath $_.TaskPath -ErrorAction SilentlyContinue
            $lastRunTime = if ($info -and $info.LastRunTime) { $info.LastRunTime.ToString("s") } else { $null }
            $nextRunTime = if ($info -and $info.NextRunTime) { $info.NextRunTime.ToString("s") } else { $null }
            $runAs = if ($_.Principal -and $_.Principal.UserId) { $_.Principal.UserId } else { '' }
            $action = if ($_.Actions) {
                ($_.Actions | ForEach-Object {
                    $execute = if ($_.PSObject.Properties['Execute']) { $_.PSObject.Properties['Execute'].Value } else { '' }
                    $arguments = if ($_.PSObject.Properties['Arguments']) { $_.PSObject.Properties['Arguments'].Value } else { '' }
                    "$execute $arguments".Trim()
                }) -join '; '
            } else { '' }
            $result = [pscustomobject]@{
                server = $recordServer
                taskPath = $_.TaskPath
                taskName = $_.TaskName
                enabled = ($_.State -ne 'Disabled')
                state = $_.State.ToString()
                lastRunTime = $lastRunTime
                lastTaskResult = if ($info) { $info.LastTaskResult } else { $null }
                nextRunTime = $nextRunTime
                runAs = $runAs
                action = $action
            }
            if ($redactPaths) {
                $result.action = "[redacted]"
            }
            $result
        }
    } -ArgumentList $server, $redactPathValues
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\scheduled_tasks.json')
