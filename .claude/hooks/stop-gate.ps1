# Stop hook — project.md §8
# Blocks the session from ending until the solution builds, the phase's tests pass, and
# qa-agent has filed specs\evidence\phase-N-evidence.md.
#
# project.md §8 warns that Claude Code overrides a hook after 8 consecutive blocks. Rather than
# grind to that limit and let the session force through, this gate blocks at most
# $maxConsecutiveBlocks times, then releases with a loud "get human review" message so the problem
# reaches a person instead of being silently overridden.
#
# Exit codes: 0 = allow stop, 2 = block stop (stderr is fed back to Claude).

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$maxConsecutiveBlocks = 3

function Get-BlockCount([string]$stateFile) {
    if (-not (Test-Path $stateFile)) { return 0 }
    $n = 0
    if ([int]::TryParse((Get-Content $stateFile -Raw).Trim(), [ref]$n)) { return $n }
    return 0
}

try {
    $root = Get-RepoRoot
    $payload = Read-HookInput

    # Never re-enter: if we are already inside a stop-hook-triggered continuation, allow.
    if ($null -ne $payload -and $payload.PSObject.Properties.Name -contains 'stop_hook_active' -and $payload.stop_hook_active) {
        exit 0
    }

    $stateDir  = Join-Path $root '.claude\.state'
    if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Path $stateDir -Force | Out-Null }
    $stateFile = Join-Path $stateDir 'stop-gate-blocks.txt'

    $phase    = Get-CurrentPhase -RepoRoot $root
    $problems = @()

    # --- 1. Build + test -------------------------------------------------------------------
    $buildScript = Join-Path $root 'build.ps1'
    if (-not (Test-Path $buildScript)) {
        $problems += "build.ps1 is missing — the build/test harness is a Phase 0 deliverable."
    }
    else {
        $log = Join-Path $stateDir 'last-stop-gate-build.log'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript *> $log
        $code = $LASTEXITCODE
        if ($code -ne 0) {
            $tail = (Get-Content $log -Tail 40 -ErrorAction SilentlyContinue) -join [Environment]::NewLine
            $problems += "build.ps1 failed (exit $code). Last 40 lines:`n$tail`n(full log: .claude\.state\last-stop-gate-build.log)"
        }
    }

    # --- 2. QA evidence --------------------------------------------------------------------
    if ($null -ne $phase) {
        $evidence = Join-Path $root ("specs\evidence\phase-{0}-evidence.md" -f $phase)
        if (-not (Test-Path $evidence)) {
            $problems += "specs\evidence\phase-$phase-evidence.md does not exist. project.md §9: qa-agent files an evidence report at the end of every phase — test output, command transcripts, screenshots. Not an assertion that it works."
        }
    }

    # --- 3. Verdict ------------------------------------------------------------------------
    if (@($problems).Count -eq 0) {
        if (Test-Path $stateFile) { Remove-Item $stateFile -Force }
        exit 0
    }

    $blocks = (Get-BlockCount $stateFile) + 1
    Set-Content -Path $stateFile -Value $blocks -Encoding utf8

    if ($blocks -ge $maxConsecutiveBlocks) {
        Remove-Item $stateFile -Force
        [Console]::Error.WriteLine(@"
STOP GATE: released after $blocks consecutive blocks WITHOUT the phase being green.

Outstanding:
$($problems -join "`n`n")

project.md §8: do not force this through. Stop and get human review. Report the failure to the
project owner exactly as it stands — do not describe the phase as complete.
"@)
        exit 0
    }

    [Console]::Error.WriteLine(@"
STOP GATE (block $blocks of $maxConsecutiveBlocks): the session cannot end yet.

$($problems -join "`n`n")

Fix these, then stop again. If you cannot fix them, say so plainly to the project owner rather than
retrying — a phase that is not green is not done.
"@)
    exit 2
}
catch {
    # Fail open — a broken gate must not trap the session.
    [Console]::Error.WriteLine("STOP GATE: hook error, allowing the stop: $($_.Exception.Message)")
    exit 0
}
