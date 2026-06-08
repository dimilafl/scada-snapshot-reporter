param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run"
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    $query = if (Test-IsLocalServer -Server $server) {
        Get-CimInstance -ClassName Win32_Service -ErrorAction Stop
    }
    else {
        Get-CimInstance -ClassName Win32_Service -ComputerName $server -ErrorAction Stop
    }

    $query | ForEach-Object {
        [pscustomobject]@{
            server = $server
            name = $_.Name
            displayName = if ($_.DisplayName) { $_.DisplayName } else { $_.Name }
            status = $_.State
            startupType = $_.StartMode
            startName = if ($_.StartName) { $_.StartName } else { 'Unknown' }
        }
    }
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\services.json')
