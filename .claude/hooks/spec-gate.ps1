# PreToolUse hook (Write|Edit|MultiEdit|NotebookEdit) — project.md §8
# THE SPEC GATE: blocks edits to src\ and tests\ unless the current phase's spec is Status: Approved.
#
# Exit codes: 0 = allow, 2 = block (stderr is fed back to Claude as the reason).

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

function Deny([string]$message) {
    [Console]::Error.WriteLine($message)
    exit 2
}

try {
    $root = Get-RepoRoot
    $payload = Read-HookInput

    $path = $null
    if ($null -ne $payload -and $null -ne $payload.tool_input) {
        if ($payload.tool_input.PSObject.Properties.Name -contains 'file_path') {
            $path = [string]$payload.tool_input.file_path
        }
        elseif ($payload.tool_input.PSObject.Properties.Name -contains 'notebook_path') {
            $path = [string]$payload.tool_input.notebook_path
        }
    }

    # No path to judge -> not our business.
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }

    # Normalise without requiring the file to exist yet (Write creates new files).
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $root $path }
    $full = [System.IO.Path]::GetFullPath($path)

    $srcDir   = [System.IO.Path]::GetFullPath((Join-Path $root 'src'))
    $testsDir = [System.IO.Path]::GetFullPath((Join-Path $root 'tests'))

    $isGated = $full.StartsWith($srcDir + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
               $full.StartsWith($testsDir + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)

    # Specs, docs, agents, hooks and build files are always editable — that is how a phase gets unblocked.
    if (-not $isGated) { exit 0 }

    $phase = Get-CurrentPhase -RepoRoot $root
    if ($null -eq $phase) {
        Deny "SPEC GATE: specs\CURRENT_PHASE is missing or unreadable, so no phase is in flight. Refusing the edit to '$path'. Set CURRENT_PHASE to the phase number you are implementing."
    }

    $spec = Get-PhaseSpecPath -RepoRoot $root -Phase $phase
    if ($null -eq $spec) {
        Deny "SPEC GATE: CURRENT_PHASE is $phase but no specs\phase-$phase-*.md exists. Per project.md §6, the phase spec is written before implementation begins. Write it, get it approved, then edit '$path'."
    }

    $rel    = $spec.Substring($root.Length).TrimStart('\')
    $status = Get-SpecStatus -SpecPath $spec

    if ($status -ne 'Approved') {
        $shown = if ([string]::IsNullOrWhiteSpace($status)) { '(none found)' } else { $status }
        Deny @"
SPEC GATE: refusing to modify '$path'.

  Current phase : $phase
  Spec          : $rel
  Spec status   : $shown  (must be 'Approved')

project.md §6: "No implementation starts on a phase until its spec file is marked Status: Approved."

Do not edit the spec's Status yourself to unblock this. Approval is the project owner's call.
Finish the spec, ask the owner to approve it, then retry. Meanwhile you can still edit specs\,
docs, .claude\ and build files.
"@
    }

    exit 0
}
catch {
    # Fail open on an internal error — a broken hook must not brick the session — but say so loudly.
    [Console]::Error.WriteLine("SPEC GATE: hook error, allowing the edit: $($_.Exception.Message)")
    exit 0
}
