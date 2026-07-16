Set-StrictMode -Version 2.0
$script:CollectorHelpersPath = $PSCommandPath

function Get-ConfiguredServers {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConfigPath
    )

    if (-not [string]::IsNullOrWhiteSpace($env:OT_SNAPSHOT_TARGET_SERVER)) {
        return @($env:OT_SNAPSHOT_TARGET_SERVER)
    }

    $serversFile = Join-Path $ConfigPath 'servers.json'
    if (-not (Test-Path $serversFile)) {
        return @($env:COMPUTERNAME)
    }

    $content = Get-Content -Path $serversFile -Raw | ConvertFrom-Json
    if ($null -eq $content.servers -or $content.servers.Count -eq 0) {
        return @($env:COMPUTERNAME)
    }

    return @($content.servers | ForEach-Object { $_.name })
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
        [System.IO.File]::WriteAllText($Path, '[]', [System.Text.UTF8Encoding]::new($false))
        return
    }

    $json = ConvertTo-Json -InputObject $items.ToArray() -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
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
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
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
    foreach ($name in @('ConfigPath', 'OutputPath', 'PSScriptRoot', 'RedactPaths', 'redactPathValues', 'registryPaths', 'logConfigs', 'windowHours', 'now')) {
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
