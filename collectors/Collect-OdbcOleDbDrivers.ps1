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

        $drivers = New-Object System.Collections.Generic.List[object]
        $odbcRoots = @(
            @{ Path = 'HKLM:\Software\ODBC\ODBCINST.INI\ODBC Drivers'; Architecture = '64-bit' },
            @{ Path = 'HKLM:\Software\WOW6432Node\ODBC\ODBCINST.INI\ODBC Drivers'; Architecture = '32-bit' }
        )

        foreach ($root in $odbcRoots) {
            $driverNames = (Get-ItemProperty -Path $root.Path -ErrorAction SilentlyContinue).PSObject.Properties |
                Where-Object { $_.Name -notlike 'PS*' } |
                Select-Object -ExpandProperty Name

            foreach ($name in $driverNames) {
                $detailPath = $root.Path -replace '\\ODBC Drivers$', "\$name"
                $detail = Get-ItemProperty -Path $detailPath -ErrorAction SilentlyContinue
                $driverPath = if ($detail) { $detail.Driver } else { $null }
                $version = $null
                $lastModified = $null
                if ($driverPath -and (Test-Path $driverPath)) {
                    $file = Get-Item $driverPath
                    $version = $file.VersionInfo.FileVersion
                    $lastModified = $file.LastWriteTime.ToString("s")
                }

                if ($redactPaths) {
                    $driverPath = "[redacted]"
                }

                $drivers.Add([pscustomobject]@{
                    server = $recordServer
                    type = 'ODBC'
                    name = $name
                    version = $version
                    architecture = $root.Architecture
                    installPath = $driverPath
                    lastModified = $lastModified
                })
            }
        }

        $oleDbRoots = @(
            @{ View = [Microsoft.Win32.RegistryView]::Registry64; Architecture = '64-bit' },
            @{ View = [Microsoft.Win32.RegistryView]::Registry32; Architecture = '32-bit' }
        )

        foreach ($root in $oleDbRoots) {
            $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $root.View)
            $clsidKey = $baseKey.OpenSubKey('Software\Classes\CLSID')
            if ($null -eq $clsidKey) {
                $baseKey.Close()
                continue
            }

            foreach ($clsid in $clsidKey.GetSubKeyNames()) {
                $classKey = $clsidKey.OpenSubKey($clsid)
                if ($null -eq $classKey) {
                    continue
                }

                $providerKey = $classKey.OpenSubKey('OLE DB Provider')
                if ($null -eq $providerKey) {
                    $classKey.Close()
                    continue
                }

                $inprocKey = $classKey.OpenSubKey('InprocServer32')
                $installPath = if ($null -ne $inprocKey) { $inprocKey.GetValue('') } else { $null }
                $providerName = $providerKey.GetValue('')
                $className = $classKey.GetValue('')
                $name = if ($providerName) { $providerName } elseif ($className) { $className } else { $clsid }
                $version = $null
                $lastModified = $null

                if ($installPath -and (Test-Path $installPath)) {
                    $file = Get-Item $installPath
                    $version = $file.VersionInfo.FileVersion
                    $lastModified = $file.LastWriteTime.ToString("s")
                }

                if ($redactPaths) {
                    $installPath = "[redacted]"
                }

                $drivers.Add([pscustomobject]@{
                    server = $recordServer
                    type = 'OLE DB'
                    name = $name
                    version = $version
                    architecture = $root.Architecture
                    installPath = $installPath
                    lastModified = $lastModified
                })

                if ($null -ne $inprocKey) { $inprocKey.Close() }
                $providerKey.Close()
                $classKey.Close()
            }

            $clsidKey.Close()
            $baseKey.Close()
        }

        return $drivers
    } -ArgumentList $server, $redactPathValues
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\odbc_oledb.json')
