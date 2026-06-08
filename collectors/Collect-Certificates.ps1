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

        $results = New-Object System.Collections.Generic.List[object]
        $stores = @('Cert:\LocalMachine\My', 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\CA')

        foreach ($storePath in $stores) {
            Get-ChildItem -Path $storePath -ErrorAction SilentlyContinue | ForEach-Object {
                $daysUntilExpiry = ($_.NotAfter - (Get-Date)).Days
                $results.Add([pscustomobject]@{
                    server = $recordServer
                    subject = $_.Subject
                    issuer = $_.Issuer
                    thumbprint = $_.Thumbprint
                    notBefore = $_.NotBefore.ToString("s")
                    notAfter = $_.NotAfter.ToString("s")
                    daysUntilExpiry = $daysUntilExpiry
                    store = $storePath
                })
            }
        }

        return $results
    } -ArgumentList $server
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\certificates.json')
