# SessionStart hook — project.md §8
# Injects the current phase's spec path and checkpoint checklist into context.
# stdout from a SessionStart hook is added to the session context.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

try {
    $root = Get-RepoRoot
    $null = Read-HookInput

    $phase = Get-CurrentPhase -RepoRoot $root
    if ($null -eq $phase) {
        Write-Output "VinayakaAgencies: specs\CURRENT_PHASE is missing or unreadable. Set it to the phase number in flight before doing any work."
        exit 0
    }

    $spec = Get-PhaseSpecPath -RepoRoot $root -Phase $phase
    if ($null -eq $spec) {
        Write-Output "VinayakaAgencies: CURRENT_PHASE is $phase but no specs\phase-$phase-*.md exists. Write that spec before any implementation."
        exit 0
    }

    $rel = $spec.Substring($root.Length).TrimStart('\')
    $status = Get-SpecStatus -SpecPath $spec
    if ([string]::IsNullOrWhiteSpace($status)) { $status = '(no Status field found)' }

    $lines = @()
    $lines += "=== VinayakaAgencies — active phase ==="
    $lines += "Current phase : $phase"
    $lines += "Spec          : $rel"
    $lines += "Spec status   : $status"
    $lines += ""

    if ($status -ne 'Approved') {
        $lines += "IMPLEMENTATION IS BLOCKED. The spec gate (PreToolUse) will refuse edits to src\ and tests\"
        $lines += "until this spec's Status is 'Approved'. Work on the spec, or ask the project owner to approve it."
        $lines += ""
    }

    $checks = Get-CheckpointLines -SpecPath $spec
    if (@($checks).Count -gt 0) {
        $lines += "Checkpoint criteria for this phase (project.md §10):"
        $lines += $checks
        $lines += ""
    }

    $oq = Join-Path $root 'specs\open-questions.md'
    if (Test-Path $oq) {
        $open = @(Get-Content $oq | Where-Object { $_ -match '\*\*Open\*\*' }).Count
        if ($open -gt 0) {
            $lines += "$open open question(s) outstanding in specs\open-questions.md. Do not invent answers to these — ask."
            $lines += ""
        }
    }

    $lines += "Reminders: work only within this phase's scope (project.md §9). Flag scope creep, do not absorb it."
    $lines += "The audit trail cannot be disabled. Nothing tax-related is hardcoded. Money and quantity are decimal."
    $lines += "Run .\build.ps1 before ending the session; qa-agent must file specs\evidence\phase-$phase-evidence.md."
    $lines += "===================================="

    $lines -join [Environment]::NewLine | Write-Output
    exit 0
}
catch {
    # A hook failure must never break the session.
    Write-Output "VinayakaAgencies SessionStart hook error: $($_.Exception.Message)"
    exit 0
}
