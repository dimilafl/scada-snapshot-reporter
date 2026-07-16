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

            $result = [pscustomobject]@{
                server = $recordServer
                name = $share.name
                path = $share.path
                reachable = $reachable
                error = $errorText
                checkedAt = (Get-Date).ToString("s")
            }
            if ($redactPaths) {
                $result.path = "[redacted]"
            }
            $result
        }
    } -ArgumentList $server, $ConfigPath, $redactPathValues
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\file_shares.json')
