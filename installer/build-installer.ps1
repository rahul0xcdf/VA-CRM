# build-installer.ps1 - publishes the application self-contained and packages it as an MSI.
#
# phase-10-hardening.md section 4a D6 and checkpoint C10.2. Two steps in a fixed order, because the
# installer project packages a *published directory* rather than referencing the application
# project: a project reference would hand it the framework-dependent build output, and the whole
# point of D6 is that the target machine needs no .NET runtime installed.
#
# PS 5.1 compatible - no &&, ||, ternary, ?? or ?. .

param(
    [string]$Configuration = 'Release',

    # Skips the publish and packages whatever is already staged. For iterating on the .wxs only.
    [switch]$NoPublish,

    # Where the staged application is written and read. One default, used by both steps.
    [string]$PublishDir,

    # Code-signing certificate thumbprint. section 4a D4: the installer ships unsigned at launch because
    # the certificate is a purchase the owner has to make, but the step is wired, so buying one
    # later is a variable rather than a rebuild. Falls back to $env:VA_SIGNING_THUMBPRINT.
    [string]$SigningThumbprint
)

$ErrorActionPreference = 'Stop'

function Write-Banner([string]$Text) {
    Write-Host ''
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\VinayakaAgencies.App\VinayakaAgencies.App.csproj'
$installerProject = Join-Path $PSScriptRoot 'VinayakaAgencies.Installer\VinayakaAgencies.Installer.wixproj'

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $repoRoot 'artifacts\publish\win-x64'
}

# The MSI version is read from Directory.Build.props rather than restated here. An installer whose
# version does not advance will not upgrade an existing install, and two places to edit is how that
# gets forgotten - which is precisely the failure C10.3 is about.
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$propsXml = [xml](Get-Content -Path $propsPath -Raw)
$productVersion = ($propsXml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

if ([string]::IsNullOrWhiteSpace($productVersion)) {
    Write-Host "Could not read <Version> from $propsPath." -ForegroundColor Red
    exit 1
}

Write-Host "Product version : $productVersion"
Write-Host "Publish folder  : $PublishDir"

if (-not $NoPublish) {
    Write-Banner 'Publish (self-contained win-x64)'

    if (Test-Path $PublishDir) {
        # A stale file from a previous publish would otherwise be harvested into the package and
        # installed beside its own replacement.
        Remove-Item -Path $PublishDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    # Self-contained, not trimmed. WPF reflects over its own assemblies for XAML, so trimming
    # removes types that are only ever resolved by name - the failure shows up as a screen that
    # will not load, at run time, on the customer's machine.
    dotnet publish $appProject `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        -o $PublishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    $staged = (Get-ChildItem -Path $PublishDir -Recurse -File | Measure-Object).Count
    Write-Host "Staged $staged file(s)."

    $exePath = Join-Path $PublishDir 'VinayakaAgencies.App.exe'
    if (-not (Test-Path $exePath)) {
        Write-Host "Publish produced no VinayakaAgencies.App.exe - refusing to package." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $PublishDir)) {
    Write-Host "No publish directory at $PublishDir. Run without -NoPublish first." -ForegroundColor Red
    exit 1
}

Write-Banner 'Package (WiX v5 MSI)'

# Trailing separator matters: the .wxs harvests "$(var.AppPublishDir)**".
$publishDirForWix = $PublishDir
if (-not $publishDirForWix.EndsWith('\')) {
    $publishDirForWix = $publishDirForWix + '\'
}

dotnet build $installerProject `
    -c $Configuration `
    -p:AppPublishDir=$publishDirForWix `
    -p:AppProductVersion=$productVersion

if ($LASTEXITCODE -ne 0) {
    Write-Host "Packaging failed (exit $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

$msi = Get-ChildItem -Path (Join-Path $PSScriptRoot 'VinayakaAgencies.Installer\bin') -Filter '*.msi' -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $msi) {
    Write-Host 'Packaging reported success but produced no .msi.' -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------------ signing (section 4a D4)

if ([string]::IsNullOrWhiteSpace($SigningThumbprint)) {
    $SigningThumbprint = $env:VA_SIGNING_THUMBPRINT
}

if ([string]::IsNullOrWhiteSpace($SigningThumbprint)) {
    Write-Host ''
    Write-Host 'Not signed (no certificate thumbprint supplied).' -ForegroundColor Yellow
    Write-Host 'Windows will show "Windows protected your PC" on a fresh machine. That is expected:' -ForegroundColor Yellow
    Write-Host 'choose More info, then Run anyway. See docs/user-guide/phase-10-install-and-backup.md.' -ForegroundColor Yellow
}
else {
    Write-Banner 'Sign'

    $signtool = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue

    if ($null -eq $signtool) {
        Write-Host 'A thumbprint was supplied but signtool.exe is not on PATH.' -ForegroundColor Red
        Write-Host 'Install the Windows SDK, or run this from a Developer Command Prompt.' -ForegroundColor Red
        exit 1
    }

    # /fd sha256 and an RFC 3161 timestamp: without the timestamp the signature stops validating
    # the day the certificate expires, which for an installer kept on a USB stick is not a
    # theoretical concern.
    & $signtool.Source sign `
        /sha1 $SigningThumbprint `
        /fd sha256 `
        /tr http://timestamp.digicert.com `
        /td sha256 `
        $msi.FullName

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Signing failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Banner 'Done'
Write-Host ("Installer : " + $msi.FullName)
Write-Host ("Size      : {0:N2} MB" -f ($msi.Length / 1MB))
Write-Host ("Version   : " + $productVersion)
exit 0
