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

$stamp = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$collectionPath = Join-Path $OutputRoot "collection_$stamp"
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
