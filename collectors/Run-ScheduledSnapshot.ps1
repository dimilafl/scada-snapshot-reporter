param(
    [string] $RepositoryRoot,
    [string] $ConfigPath,
    [string] $OutputRoot,
    [Parameter(Mandatory = $true)]
    [string] $ReportExecutablePath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $RepositoryRoot 'config'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepositoryRoot 'Output'
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
if (-not [System.IO.Path]::IsPathRooted($ConfigPath)) {
    $ConfigPath = Join-Path $RepositoryRoot $ConfigPath
}
if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $RepositoryRoot $OutputRoot
}
if (-not [System.IO.Path]::IsPathRooted($ReportExecutablePath)) {
    $ReportExecutablePath = Join-Path $RepositoryRoot $ReportExecutablePath
}
$ConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$ReportExecutablePath = [System.IO.Path]::GetFullPath($ReportExecutablePath)

function Get-LatestReportFolder {
    param(
        [Parameter(Mandatory = $true)]
        [string] $OutputRoot
    )

    if (-not (Test-Path $OutputRoot)) {
        return $null
    }

    Get-ChildItem -Path $OutputRoot -Directory |
        Where-Object {
            $_.Name -match '^\d{4}-\d{2}-\d{2}_\d{4,6}$' -and
            (Test-Path (Join-Path $_.FullName 'raw')) -and
            (Test-Path (Join-Path $_.FullName 'index.html'))
        } |
        Sort-Object Name -Descending |
        Select-Object -First 1
}

function New-AvailableCollectionPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $OutputRoot
    )

    $stamp = Get-Date
    do {
        $candidate = Join-Path $OutputRoot ("collection_" + $stamp.ToString('yyyy-MM-dd_HHmmss'))
        $stamp = $stamp.AddSeconds(1)
    } while (Test-Path -LiteralPath $candidate)

    return $candidate
}

if (-not (Test-Path -LiteralPath $ReportExecutablePath -PathType Leaf)) {
    throw "Report executable was not found: $ReportExecutablePath"
}

$collectionPath = New-AvailableCollectionPath -OutputRoot $OutputRoot
New-Item -Path $collectionPath -ItemType Directory -Force | Out-Null

$previous = Get-LatestReportFolder -OutputRoot $OutputRoot
& (Join-Path $PSScriptRoot 'Run-Collectors.ps1') -ConfigPath $ConfigPath -OutputPath $collectionPath
if ($LASTEXITCODE -ne 0) {
    throw "Collector run failed with exit code $LASTEXITCODE; report generation was skipped."
}

$arguments = @('--input', $collectionPath, '--config', $ConfigPath, '--output', $OutputRoot)
if ($null -ne $previous) {
    $arguments += @('--previous', $previous.FullName)
}

if ($ReportExecutablePath.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)) {
    $LASTEXITCODE = 0
    & dotnet $ReportExecutablePath @arguments
}
else {
    $LASTEXITCODE = 0
    & $ReportExecutablePath @arguments
}
if ($LASTEXITCODE -ne 0) {
    throw "Report generation failed with exit code $LASTEXITCODE."
}
