# PreCompact hook — project.md §8
# Before any context compaction, write the current file list, outstanding TODOs and test commands
# to specs\session-log.md so nothing is lost across long sessions.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

try {
    $root = Get-RepoRoot
    $null = Read-HookInput

    $phase = Get-CurrentPhase -RepoRoot $root
    $spec  = if ($null -ne $phase) { Get-PhaseSpecPath -RepoRoot $root -Phase $phase } else { $null }

    $out = New-Object System.Collections.Generic.List[string]
    $out.Add("# Session log — written by the PreCompact hook")
    $out.Add("")
    $out.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
    $out.Add("Current phase: $phase")
    if ($null -ne $spec) {
        $out.Add("Phase spec: $($spec.Substring($root.Length).TrimStart('\'))  (Status: $(Get-SpecStatus -SpecPath $spec))")
    }
    $out.Add("")

    # --- Working tree state ---------------------------------------------------------------
    $out.Add("## Uncommitted changes")
    $out.Add("")
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $git -and (Test-Path (Join-Path $root '.git'))) {
        Push-Location $root
        try {
            $status = & git status --short
            if (@($status).Count -eq 0) { $out.Add("(clean working tree)") }
            else { $out.Add('```'); $status | ForEach-Object { $out.Add($_) }; $out.Add('```') }
            $out.Add("")
            $out.Add("Branch: $(& git rev-parse --abbrev-ref HEAD)  HEAD: $(& git rev-parse --short HEAD)")
        }
        finally { Pop-Location }
    }
    else {
        $out.Add("(not a git repository, or git unavailable)")
    }
    $out.Add("")

    # --- Source files ---------------------------------------------------------------------
    $out.Add("## Source files")
    $out.Add("")
    foreach ($dir in @('src', 'tests')) {
        $p = Join-Path $root $dir
        if (-not (Test-Path $p)) { continue }
        $files = Get-ChildItem -Path $p -Recurse -File -Include *.cs, *.xaml, *.csproj -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
        $out.Add("### $dir  ($(@($files).Count) files)")
        $out.Add('```')
        foreach ($f in $files) { $out.Add($f.FullName.Substring($root.Length).TrimStart('\')) }
        $out.Add('```')
        $out.Add("")
    }

    # --- Outstanding TODOs ----------------------------------------------------------------
    $out.Add("## Outstanding TODOs in src\ and tests\")
    $out.Add("")
    $todos = @()
    foreach ($dir in @('src', 'tests')) {
        $p = Join-Path $root $dir
        if (-not (Test-Path $p)) { continue }
        $todos += Get-ChildItem -Path $p -Recurse -File -Include *.cs, *.xaml -ErrorAction SilentlyContinue |
                  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
                  Select-String -Pattern 'TODO|HACK|FIXME' -ErrorAction SilentlyContinue
    }
    if (@($todos).Count -eq 0) { $out.Add("(none)") }
    else {
        $out.Add('```')
        foreach ($t in $todos) {
            $out.Add("$($t.Path.Substring($root.Length).TrimStart('\')):$($t.LineNumber): $($t.Line.Trim())")
        }
        $out.Add('```')
    }
    $out.Add("")

    # --- Open questions -------------------------------------------------------------------
    $oq = Join-Path $root 'specs\open-questions.md'
    if (Test-Path $oq) {
        $open = @(Get-Content $oq | Where-Object { $_ -match '\*\*Open\*\*' })
        $out.Add("## Open questions still unanswered ($(@($open).Count))")
        $out.Add("")
        if (@($open).Count -eq 0) { $out.Add("(none)") }
        else { foreach ($q in $open) { $out.Add($q) } }
        $out.Add("")
    }

    # --- Commands -------------------------------------------------------------------------
    $out.Add("## Commands")
    $out.Add("")
    $out.Add('```powershell')
    $out.Add('.\build.ps1                 # restore + build + test (what the Stop hook runs)')
    $out.Add('.\build.ps1 -NoTest         # restore + build only')
    $out.Add('dotnet test VinayakaAgencies.sln')
    $out.Add('dotnet run --project src\VinayakaAgencies.App')
    $out.Add('dotnet ef migrations list -p src\VinayakaAgencies.Infrastructure -s src\VinayakaAgencies.App')
    $out.Add('```')
    $out.Add("")
    $out.Add("After compaction: re-read the phase spec above before continuing. Work only within its scope.")

    $target = Join-Path $root 'specs\session-log.md'
    Set-Content -Path $target -Value ($out -join [Environment]::NewLine) -Encoding utf8

    Write-Output "PreCompact: session state written to specs\session-log.md — re-read it and the current phase spec after compaction."
    exit 0
}
catch {
    Write-Output "VinayakaAgencies PreCompact hook error: $($_.Exception.Message)"
    exit 0
}
