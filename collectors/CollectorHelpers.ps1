Set-StrictMode -Version 2.0
$script:CollectorHelpersPath = $PSCommandPath

function Get-ConfiguredServers {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConfigPath
    )

    if (-not [string]::IsNullOrWhiteSpace($env:OT_SNAPSHOT_TARGET_SERVER)) {
        return @($env:OT_SNAPSHOT_TARGET_SERVER.Trim())
    }

    $serversFile = Join-Path $ConfigPath 'servers.json'
    if (-not (Test-Path $serversFile)) {
        return @($env:COMPUTERNAME)
    }

    $content = Get-Content -Path $serversFile -Raw | ConvertFrom-Json
    $entries = @()
    if ($null -ne $content.servers) {
        $entries = @($content.servers)
    }
    if ($entries.Count -eq 0) {
        return @($env:COMPUTERNAME)
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $servers = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $entries) {
        $name = ''
        if ($null -ne $entry -and $null -ne $entry.PSObject.Properties['name']) {
            $name = [string]$entry.name
        }
        $name = $name.Trim()
        if ($name.Length -gt 0 -and $seen.Add($name)) {
            $servers.Add($name)
        }
    }

    if ($servers.Count -eq 0) {
        return @($env:COMPUTERNAME)
    }

    return @($servers.ToArray())
}

function Ensure-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path $Path)) {
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
    }
}

function Get-BoundedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [ValidateRange(1, 1000000)]
        [int] $MaxFiles = 50000
    )

    $candidates = @(Get-ChildItem -Path $Path -File -Recurse -Depth 4 -ErrorAction Stop | Select-Object -First ($MaxFiles + 1))
    [pscustomobject]@{
        files = @($candidates | Select-Object -First $MaxFiles)
        truncated = $candidates.Count -gt $MaxFiles
    }
}

function Remove-StaleAtomicArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $directory = Split-Path -Path $Path -Parent
    if ([string]::IsNullOrWhiteSpace($directory)) {
        $directory = (Get-Location).Path
    }

    $leaf = Split-Path -Path $Path -Leaf
    $prefix = "$leaf."
    $cutoff = (Get-Date).ToUniversalTime().AddDays(-1)
    try {
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -File -Force -ErrorAction Stop)) {
            $isGeneratedArtifact = $item.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -and
                ($item.Name.EndsWith('.tmp', [System.StringComparison]::OrdinalIgnoreCase) -or
                    $item.Name.EndsWith('.bak', [System.StringComparison]::OrdinalIgnoreCase))
            if (-not $isGeneratedArtifact -or $item.LastWriteTimeUtc -ge $cutoff) {
                continue
            }

            try {
                Remove-Item -LiteralPath $item.FullName -Force -ErrorAction Stop
            }
            catch {
                Write-Verbose "Could not remove stale atomic artifact '$($item.FullName)': $($_.Exception.Message)"
            }
        }
    }
    catch {
        Write-Verbose "Could not inspect atomic artifacts under '$directory': $($_.Exception.Message)"
    }
}

function Write-TextAtomically {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Contents
    )

    Remove-StaleAtomicArtifacts -Path $Path
    $tempId = [guid]::NewGuid().ToString('N')
    $tempPath = "$Path.$tempId.tmp"
    $backupPath = "$Path.$tempId.bak"
    try {
        [System.IO.File]::WriteAllText($tempPath, $Contents, [System.Text.UTF8Encoding]::new($false))
        if ([System.IO.File]::Exists($Path)) {
            [System.IO.File]::Replace($tempPath, $Path, $backupPath)
            try {
                if ([System.IO.File]::Exists($backupPath)) {
                    [System.IO.File]::Delete($backupPath)
                }
            }
            catch {
                # The destination is already updated; a backup can be cleaned up on a later run.
            }
        }
        else {
            [System.IO.File]::Move($tempPath, $Path)
        }
    }
    catch {
        try {
            if ([System.IO.File]::Exists($tempPath)) {
                [System.IO.File]::Delete($tempPath)
            }
            if ([System.IO.File]::Exists($backupPath)) {
                [System.IO.File]::Delete($backupPath)
            }
        }
        catch {
            # Preserve the original write failure; a leftover temp file can be retried later.
        }

        throw
    }
}

function Write-JsonOutput {
    param(
        [object] $Data,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $parent = Split-Path -Path $Path -Parent
    Ensure-Directory -Path $parent
    $items = New-Object System.Collections.Generic.List[object]
    if ($null -ne $Data) {
        if ($Data -is [System.Collections.IEnumerable] -and $Data -isnot [string]) {
            foreach ($item in $Data) {
                if ($null -ne $item) {
                    $items.Add($item)
                }
            }
        }
        else {
            $items.Add($Data)
        }
    }

    if ($items.Count -eq 0) {
        Write-TextAtomically -Path $Path -Contents '[]'
        return
    }

    $json = ConvertTo-Json -InputObject $items.ToArray() -Depth 8
    Write-TextAtomically -Path $Path -Contents $json
}

function Write-JsonDocument {
    param(
        [object] $Data,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $parent = Split-Path -Path $Path -Parent
    Ensure-Directory -Path $parent
    $json = ConvertTo-Json -InputObject $Data -Depth 8
    Write-TextAtomically -Path $Path -Contents $json
}

function Invoke-PerServer {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Servers,

        [Parameter(Mandatory = $true)]
        [string] $OutputPath,

        [Parameter(Mandatory = $true)]
        [scriptblock] $ScriptBlock,

        [int] $ServerTimeoutSeconds = 300,

        [int] $MaxRetries = 2
    )

    $results = New-Object System.Collections.Generic.List[object]
    $errors = New-Object System.Collections.Generic.List[object]
    $runTime = Get-Date -Format "yyyy-MM-ddTHH:mm:ss"
    $scriptText = $ScriptBlock.ToString()
    $callerVariables = @{}
    foreach ($name in @('ConfigPath', 'OutputPath', 'PSScriptRoot', 'RedactPaths', 'redactPathValues', 'registryPaths', 'logConfigs', 'windowHours', 'now', 'maxFiles')) {
        $variable = Get-Variable -Name $name -Scope 1 -ErrorAction SilentlyContinue
        if ($null -ne $variable) {
            $callerVariables[$name] = $variable.Value
        }
    }

    foreach ($server in $Servers) {
        $attempt = 0
        $success = $false
        while (-not $success -and $attempt -lt $MaxRetries) {
            $attempt++
            $job = $null
            try {
                Write-Verbose "Collecting from $server (attempt $attempt/$MaxRetries)..."
                $job = Start-Job -ScriptBlock {
                    param($server, $scriptText, $helperPath, $callerVariables)

                    . $helperPath
                    foreach ($entry in $callerVariables.GetEnumerator()) {
                        Set-Variable -Name $entry.Key -Value $entry.Value -Scope Local
                    }

                    $collectorBlock = [scriptblock]::Create($scriptText)
                    & $collectorBlock $server
                } -ArgumentList $server, $scriptText, $script:CollectorHelpersPath, $callerVariables
                $completed = Wait-Job -Job $job -Timeout $ServerTimeoutSeconds
                if ($null -ne $completed) {
                    $items = Receive-Job -Job $job -ErrorAction Stop
                    foreach ($item in @($items)) {
                        if ($null -ne $item) {
                            $results.Add($item)
                        }
                    }
                    $success = $true
                }
                else {
                    Stop-Job -Job $job -ErrorAction SilentlyContinue
                    if ($attempt -lt $MaxRetries) {
                        Write-Warning "Timeout collecting from $server (attempt $attempt/$MaxRetries); retrying..."
                        Start-Sleep -Seconds 1
                    }
                    else {
                        $errors.Add([pscustomobject]@{
                            server = $server
                            error = "Timeout after $ServerTimeoutSeconds seconds ($MaxRetries attempts)"
                            run = $runTime
                        })
                    }
                }
            }
            catch {
                if ($attempt -lt $MaxRetries) {
                    Write-Warning "Error collecting from $server : $($_.Exception.Message); retrying..."
                    Start-Sleep -Seconds 1
                }
                else {
                    $errors.Add([pscustomobject]@{
                        server = $server
                        error = $_.Exception.Message
                        run = $runTime
                    })
                }
            }
            finally {
                if ($null -ne $job) {
                    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }

    if ($errors.Count -gt 0) {
        $errorPath = Join-Path $OutputPath 'raw\_errors.json'
        $allErrors = New-Object System.Collections.Generic.List[object]
        foreach ($errorItem in $errors) {
            $allErrors.Add($errorItem)
        }

        Write-JsonOutput -Data $allErrors -Path $errorPath
    }

    return $results
}

function Test-IsLocalServer {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Server
    )

    return $Server -eq '.' -or
        $Server -eq 'localhost' -or
        $Server -eq $env:COMPUTERNAME -or
        $Server -eq "$env:COMPUTERNAME.$env:USERDNSDOMAIN"
}

function Invoke-ServerScript {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Server,

        [Parameter(Mandatory = $true)]
        [scriptblock] $ScriptBlock,

        [object[]] $ArgumentList = @()
    )

    if (Test-IsLocalServer -Server $Server) {
        & $ScriptBlock @ArgumentList
    }
    else {
        Invoke-Command -ComputerName $Server -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList
    }
}
