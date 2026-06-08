param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run"
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    $query = if (Test-IsLocalServer -Server $server) {
        Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DriveType = 3" -ErrorAction Stop
    }
    else {
        Get-CimInstance -ClassName Win32_LogicalDisk -ComputerName $server -Filter "DriveType = 3" -ErrorAction Stop
    }

    $query | ForEach-Object {
        $totalGb = [math]::Round($_.Size / 1GB, 2)
        $freeGb = [math]::Round($_.FreeSpace / 1GB, 2)
        $freePercent = 0
        if ($_.Size -gt 0) {
            $freePercent = [math]::Round(($_.FreeSpace / $_.Size) * 100, 2)
        }

        [pscustomobject]@{
            server = $server
            drive = $_.DeviceID
            totalGb = $totalGb
            freeGb = $freeGb
            freePercent = $freePercent
        }
    }
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\disk_space.json')
