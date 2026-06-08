<#
.SYNOPSIS
Pre-publication audit scanner. Fails if any sensitive terms are found.
Exit code 0 = clean, 1 = sensitive hits found.
#>
$ErrorActionPreference = 'Stop'
$exitCode = 0

$terms = @(
    ('Ever' + 'source'), ('NS' + 'TAR'), ('Yan' + 'kee'), ('EG' + 'MA'),
    ('r' + 'ts\.local'), ('R' + 'TS Enterprise CA'), ('R' + 'TS Gas SCADA'),
    ('Gas' + ' SCADA'), ('OA' + 'SyS'), ('AV' + 'EVA'), ('Clear' + 'SCADA'), ('T' + 'iPS'), ('Op' + 'Log'),
    ('sv' + 'c_'), ('sv' + 'c-'), ('sv' + 'c_'), ('sv' + 'c-'),
    ('AD' + 'MIN\$'), ('histor' + 'ian\$'), ('R' + 'TS\\svc'), ('R' + 'TS\\former'),
    'password', 'secret', 'token',
    'connectionString', 'User ID=', 'Password=',
    'Initial Catalog', 'Data Source', 'Server=',
    'Trusted_Connection',
    ('R' + 'TS SCADA'), ('R' + 'TS Snapshot'),
    ('CN=' + 'scada-pi'), ('CN=' + 'scada-web'),
    ('C:\\\\' + 'R' + 'TS\\\\'),
    ('\\\\server' + '\\\\share'),
    ('hist' + '01')
)

Write-Host "=== Public Audit Scanner ===" -ForegroundColor Cyan
Write-Host "Scanning for $(($terms).Count) sensitive patterns..." -ForegroundColor Yellow
Write-Host ""

# Scan file contents
$excludedAuditFiles = @(
    'scripts\public_audit.ps1',
    '.gitignore',
    'SECURITY.md'
)
$files = Get-ChildItem -Recurse -File -Exclude '.git' | Where-Object {
    $_.FullName -notmatch '\\\.git\\' -and
    $excludedAuditFiles -notcontains $_.FullName.Replace((Get-Location).Path + '\', '')
}
foreach ($term in $terms) {
    $hits = $files | Select-String -Pattern $term -CaseSensitive:$false 2>$null
    if ($hits) {
        Write-Host "  FOUND '$term' in:" -ForegroundColor Red
        foreach ($hit in $hits) {
            $relPath = $hit.Path.Replace((Get-Location).Path + '\', '')
            Write-Host "    $relPath : $($hit.LineNumber)" -ForegroundColor Red
        }
        $exitCode = 1
    }
}

# Scan filenames
$nameTerms = @(
    ('*r' + 'ts*'),
    ('*ever' + 'source*'),
    ('*eg' + 'ma*'),
    ('*ns' + 'tar*'),
    ('*yan' + 'kee*'),
    ('*oa' + 'sys*'),
    ('*av' + 'eva*')
)
foreach ($term in $nameTerms) {
    $hits = Get-ChildItem -Recurse -Name -File -Exclude '.git' | Where-Object { $_ -like $term }
    if ($hits) {
        Write-Host "  FOUND filename pattern '$term':" -ForegroundColor Red
        foreach ($hit in $hits) { Write-Host "    $hit" -ForegroundColor Red }
        $exitCode = 1
    }
}

# Scan directory names
foreach ($term in $nameTerms) {
    $hits = Get-ChildItem -Recurse -Directory -Exclude '.git' | Where-Object { $_.Name -like $term }
    if ($hits) {
        Write-Host "  FOUND directory pattern '$term':" -ForegroundColor Red
        foreach ($hit in $hits) { Write-Host "    $($hit.FullName)" -ForegroundColor Red }
        $exitCode = 1
    }
}

if ($exitCode -eq 0) {
    Write-Host "`n=== AUDIT PASSED: No sensitive terms found ===" -ForegroundColor Green
} else {
    Write-Host "`n=== AUDIT FAILED: Sensitive terms found ===" -ForegroundColor Red
}

exit $exitCode
