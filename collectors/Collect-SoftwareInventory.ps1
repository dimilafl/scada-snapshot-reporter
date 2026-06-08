param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run",
    [switch] $RedactPaths
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$registryPaths = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$redactPathValues = [bool]$RedactPaths

$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        param($recordServer, $paths, $redactPaths)

        foreach ($path in $paths) {
            Get-ItemProperty -Path $path -ErrorAction SilentlyContinue |
                Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.PSObject.Properties['DisplayName'].Value } |
                ForEach-Object {
                    $displayName = $_.PSObject.Properties['DisplayName'].Value
                    $displayVersion = $_.PSObject.Properties['DisplayVersion']
                    $installLocation = $_.PSObject.Properties['InstallLocation']
                    $publisher = $_.PSObject.Properties['Publisher']
                    $result = [pscustomobject]@{
                        server = $recordServer
                        name = $displayName
                        version = if ($displayVersion -and $displayVersion.Value) { $displayVersion.Value } else { '[no version]' }
                        publisher = if ($publisher) { $publisher.Value } else { $null }
                        installLocation = if ($installLocation -and $installLocation.Value) { $installLocation.Value } else { '' }
                    }
                    if ($redactPaths) {
                        $result.installLocation = "[redacted]"
                    }
                    $result
                }
        }
    } -ArgumentList $server, (, $registryPaths), $redactPathValues
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\software.json')
