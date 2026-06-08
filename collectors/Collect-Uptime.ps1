param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run"
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$now = Get-Date
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    $os = if (Test-IsLocalServer -Server $server) {
        Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    }
    else {
        Get-CimInstance -ClassName Win32_OperatingSystem -ComputerName $server -ErrorAction Stop
    }

    $boot = $os.LastBootUpTime
    [pscustomobject]@{
        server = $server
        lastBootTime = $boot.ToString("s")
        uptimeHours = [math]::Round(($now - $boot).TotalHours, 2)
    }
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\uptime.json')
