# Shared helpers for VinayakaAgencies Claude Code hooks.
# Dot-sourced by the other hook scripts. Windows PowerShell 5.1 compatible.

Set-StrictMode -Version Latest

function Get-RepoRoot {
    # .claude\hooks\common.ps1 -> .claude\hooks -> .claude -> <repo root>
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Read-HookInput {
    # Claude Code writes the hook payload to stdin as JSON.
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    try { return $raw | ConvertFrom-Json } catch { return $null }
}

function Get-CurrentPhase {
    param([string]$RepoRoot)
    $file = Join-Path $RepoRoot 'specs\CURRENT_PHASE'
    if (-not (Test-Path $file)) { return $null }
    $text = (Get-Content $file -Raw).Trim()
    $n = 0
    if ([int]::TryParse($text, [ref]$n)) { return $n }
    return $null
}

function Get-PhaseSpecPath {
    param([string]$RepoRoot, [int]$Phase)
    $matches = Get-ChildItem -Path (Join-Path $RepoRoot 'specs') -Filter ("phase-{0}-*.md" -f $Phase) -File -ErrorAction SilentlyContinue
    if ($null -eq $matches -or @($matches).Count -eq 0) { return $null }
    return @($matches)[0].FullName
}

function Get-SpecStatus {
    param([string]$SpecPath)
    if (-not (Test-Path $SpecPath)) { return $null }
    foreach ($line in Get-Content $SpecPath) {
        if ($line -match '^\s*Status:\s*(.+?)\s*$') { return $Matches[1].Trim() }
    }
    return $null
}

function Get-CheckpointLines {
    param([string]$SpecPath)
    # Pull the rows of the "Checkpoint criteria" tables: | C1.2 | ... |
    $out = @()
    foreach ($line in Get-Content $SpecPath) {
        if ($line -match '^\|\s*(C\d+\.\d+)\s*\|\s*(.+?)\s*\|') {
            $out += ("  [{0}] {1}" -f $Matches[1], $Matches[2])
        }
    }
    return $out
}
