param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run"
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        param($recordServer, $configPath)

        $sharesFile = Join-Path $configPath 'shares.json'
        $shares = @()
        if (Test-Path $sharesFile) {
            $shares = @((Get-Content -Path $sharesFile -Raw | ConvertFrom-Json).shares)
        }

        foreach ($share in $shares) {
            $reachable = $false
            $errorText = $null

            try {
                $reachable = Test-Path -Path $share.path -ErrorAction Stop
            }
            catch {
                $errorText = $_.Exception.Message
            }

            [pscustomobject]@{
                server = $recordServer
                name = $share.name
                path = $share.path
                reachable = $reachable
                error = $errorText
                checkedAt = (Get-Date).ToString("s")
            }
        }
    } -ArgumentList $server, $ConfigPath
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\file_shares.json')
