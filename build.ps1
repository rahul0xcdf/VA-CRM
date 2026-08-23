# build.ps1 — restore + build + test harness (specs/phase-0-foundations.md §2.5, checkpoint C0.1)
#
# Exit code is load-bearing: 0 only on full success, non-zero on the first failing step.
# PS 5.1 compatible — no &&, ||, ternary, ??, or ?. .

param(
    [switch]$NoTest,
    [switch]$NoRestore,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Write-Banner([string]$Text) {
    Write-Host ''
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'VinayakaAgencies.sln'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$testResultsDir = Join-Path $artifactsDir 'test-results'

if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}

if (-not $NoRestore) {
    Write-Banner 'Restore'
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Restore failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Banner "Build ($Configuration)"
# Directory.Build.props already sets TreatWarningsAsErrors=true for every project; nothing here
# should defeat that.
dotnet build $solution -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed (exit $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($NoTest) {
    Write-Banner 'Done (build only, -NoTest set)'
    exit 0
}

Write-Banner 'Test'
if (-not (Test-Path $testResultsDir)) {
    New-Item -ItemType Directory -Path $testResultsDir -Force | Out-Null
}

# Test per project rather than once against the whole solution: `dotnet test` does not reliably
# expand the $(MSBuildProjectName) trx-logger token across a multi-project solution run, which
# would otherwise leave every project's results overwriting the same file.
$testProjects = Get-ChildItem -Path (Join-Path $repoRoot 'tests') -Filter '*.csproj' -Recurse
if ($testProjects.Count -eq 0) {
    Write-Host 'No test projects found under tests/.' -ForegroundColor Yellow
}

$testFailed = $false
foreach ($project in $testProjects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
    Write-Host "--- $projectName ---"
    dotnet test $project.FullName -c $Configuration --no-build `
        --logger "trx;LogFileName=$projectName.trx" `
        --results-directory $testResultsDir `
        --collect:"XPlat Code Coverage"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$projectName failed (exit $LASTEXITCODE)." -ForegroundColor Red
        $testFailed = $true
    }
}

if ($testFailed) {
    Write-Host 'One or more test projects failed.' -ForegroundColor Red
    exit 1
}

Write-Banner 'Done (build + test succeeded)'
exit 0
