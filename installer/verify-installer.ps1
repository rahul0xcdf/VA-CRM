# verify-installer.ps1 - reads a built MSI and checks the things checkpoints C10.2/C10.3/C10.4
# depend on, without installing anything.
#
# Why this exists: C10.2 ("runs clean on a fresh Windows machine") can only finally be answered on a
# fresh machine, which is the owner's step. But most of the ways an installer fails there are
# visible in its tables beforehand - a missing executable, a runtime prerequisite that was supposed
# to have been bundled, a data folder that is not marked permanent and would take the books with it
# on uninstall. Checking those here turns the owner's walkthrough into a confirmation rather than a
# first discovery.
#
# PS 5.1 compatible - no &&, ||, ternary, ?? or ?. .

param(
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = Join-Path $PSScriptRoot 'VinayakaAgencies.Installer\bin\Release\VinayakaAgencies-Setup.msi'
}

if (-not (Test-Path $MsiPath)) {
    Write-Host "No MSI at $MsiPath. Run build-installer.ps1 first." -ForegroundColor Red
    exit 1
}

$msi = Get-Item $MsiPath

Write-Host ''
Write-Host '=== Package ===' -ForegroundColor Cyan
Write-Host ("File    : " + $msi.FullName)
Write-Host ("Size    : {0:N2} MB" -f ($msi.Length / 1MB))
Write-Host ("Written : " + $msi.LastWriteTimeUtc.ToString('u'))

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    'OpenDatabase', 'InvokeMethod', $null, $installer, @($msi.FullName, 0))

function Invoke-MsiQuery([string]$Sql) {
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null

    $rows = New-Object System.Collections.ArrayList

    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)

        if ($null -eq $record) {
            break
        }

        $fieldCount = $record.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $record, $null)
        $fields = New-Object System.Collections.ArrayList

        for ($i = 1; $i -le $fieldCount; $i++) {
            $value = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @($i))
            $fields.Add($value) | Out-Null
        }

        $rows.Add($fields) | Out-Null
    }

    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return $rows
}

$failures = New-Object System.Collections.ArrayList

function Assert-That([bool]$Condition, [string]$Description) {
    if ($Condition) {
        Write-Host ("  [ok]   " + $Description) -ForegroundColor Green
    }
    else {
        Write-Host ("  [FAIL] " + $Description) -ForegroundColor Red
        $script:failures.Add($Description) | Out-Null
    }
}

# ------------------------------------------------------------------ identity and upgrade (C10.3)

Write-Host ''
Write-Host '=== Identity and upgrade path (C10.3) ===' -ForegroundColor Cyan

$properties = @{}
foreach ($row in (Invoke-MsiQuery 'SELECT `Property`, `Value` FROM `Property`')) {
    $properties[$row[0]] = $row[1]
}

Write-Host ("ProductName    : " + $properties['ProductName'])
Write-Host ("ProductVersion : " + $properties['ProductVersion'])
Write-Host ("UpgradeCode    : " + $properties['UpgradeCode'])
Write-Host ("ALLUSERS       : " + $properties['ALLUSERS'])

Assert-That ($properties['ProductName'] -eq 'Vinayaka Agencies') 'ProductName is "Vinayaka Agencies"'
Assert-That (-not [string]::IsNullOrWhiteSpace($properties['ProductVersion'])) 'ProductVersion is set'
Assert-That ($properties['ALLUSERS'] -eq '1') 'ALLUSERS=1, so this is a per-machine install'

# An Upgrade row is what makes an install-over-the-top replace rather than duplicate. Without it,
# C10.3 cannot hold: the owner would end up with two entries in Apps and two copies on disk.
$upgradeRows = Invoke-MsiQuery 'SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `ActionProperty` FROM `Upgrade`'
Assert-That ($upgradeRows.Count -gt 0) 'An Upgrade table row exists, so an upgrade replaces the old version'

foreach ($row in $upgradeRows) {
    Write-Host ("  upgrade range  : " + $row[1] + " .. " + $row[2] + "  -> " + $row[3])
}

# ------------------------------------------------------------------ payload (C10.2, D6)

Write-Host ''
Write-Host '=== Payload (C10.2, self-contained per D6) ===' -ForegroundColor Cyan

$files = Invoke-MsiQuery 'SELECT `File`, `FileName`, `Component_` FROM `File`'
Write-Host ("Files in package : " + $files.Count)

# MSI short|long file name pairs: take the long half when present.
$fileNames = New-Object System.Collections.ArrayList
foreach ($row in $files) {
    $name = $row[1]
    if ($name -like '*|*') {
        $name = $name.Substring($name.IndexOf('|') + 1)
    }
    $fileNames.Add($name) | Out-Null
}

Assert-That ($fileNames -contains 'VinayakaAgencies.App.exe') 'The application executable is in the package'
Assert-That ($fileNames -contains 'appsettings.json') 'appsettings.json ships, so Backup defaults are present'

# D6: self-contained. If the publish were framework-dependent these would be absent and the
# installer would need a .NET Desktop Runtime the fresh machine may have no way to download.
Assert-That ($fileNames -contains 'hostfxr.dll') 'hostfxr.dll present (self-contained host)'
Assert-That ($fileNames -contains 'coreclr.dll') 'coreclr.dll present (the runtime is bundled)'
Assert-That (($fileNames | Where-Object { $_ -eq 'PresentationFramework.dll' }).Count -gt 0) 'WPF assemblies are bundled'
Assert-That ($files.Count -gt 400) 'Payload is a full self-contained publish, not a thin build'

# A bundled runtime is the whole of the prerequisite story: there must be no launch condition
# demanding one be installed first.
$launchConditions = Invoke-MsiQuery 'SELECT `Condition`, `Description` FROM `LaunchCondition`'
$runtimeConditions = @($launchConditions | Where-Object { $_[0] -match 'NETFRAMEWORK|DOTNET|NetCore' })
Assert-That ($runtimeConditions.Count -eq 0) 'No .NET runtime launch condition (nothing to download on a fresh machine)'

# ------------------------------------------------------------------ data folder (C10.4, D7)

Write-Host ''
Write-Host '=== Company data folder (C10.4, D7) ===' -ForegroundColor Cyan

$directories = @{}
foreach ($row in (Invoke-MsiQuery 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`')) {
    $directories[$row[0]] = @{ Parent = $row[1]; DefaultDir = $row[2] }
}

Assert-That ($directories.ContainsKey('DataFolder')) 'A DataFolder directory is authored'
Assert-That ($directories.ContainsKey('DataBackupFolder')) 'A backups subfolder is authored'

if ($directories.ContainsKey('DataFolder')) {
    Write-Host ("DataFolder parent : " + $directories['DataFolder'].Parent)
    Assert-That ($directories['DataFolder'].Parent -eq 'CommonAppDataFolder') `
        'DataFolder sits under CommonAppDataFolder (%ProgramData%), not a user profile'
}

# msidbComponentAttributesPermanent = 16. This bit is the entire C10.4 mechanism: a permanent
# component is not reversed on uninstall, so the folder and the books inside it stay.
$permanentBit = 16
$components = Invoke-MsiQuery 'SELECT `Component`, `Attributes`, `Directory_` FROM `Component`'

foreach ($name in @('CompanyData', 'CompanyBackups')) {
    $component = @($components | Where-Object { $_[0] -eq $name })

    if ($component.Count -eq 0) {
        Assert-That $false ("Component '" + $name + "' exists")
        continue
    }

    $attributes = [int]$component[0][1]
    Assert-That (($attributes -band $permanentBit) -eq $permanentBit) `
        ("Component '" + $name + "' is Permanent, so uninstall leaves company data intact")
}

# Nothing under the data folder may be an installed file. An installed file there would be removed
# on uninstall even though the folder is permanent - and would be overwritten on upgrade.
$dataComponentIds = @($components | Where-Object { $_[2] -eq 'DataFolder' -or $_[2] -eq 'DataBackupFolder' } | ForEach-Object { $_[0] })
$filesUnderData = @($files | Where-Object { $dataComponentIds -contains $_[2] })

Assert-That ($filesUnderData.Count -eq 0) `
    'No installed files live under the data folder (the database is created at run time)'

# ------------------------------------------------------------------ shortcuts

Write-Host ''
Write-Host '=== Shortcuts ===' -ForegroundColor Cyan

$shortcuts = Invoke-MsiQuery 'SELECT `Shortcut`, `Directory_`, `Name`, `Target` FROM `Shortcut`'
foreach ($row in $shortcuts) {
    Write-Host ("  " + $row[0] + " -> " + $row[1])
}

Assert-That (@($shortcuts | Where-Object { $_[1] -eq 'AppShortcutFolder' }).Count -gt 0) 'A Start Menu shortcut is authored'
Assert-That (@($shortcuts | Where-Object { $_[1] -eq 'DesktopFolder' }).Count -gt 0) 'A Desktop shortcut is authored'

# ------------------------------------------------------------------ signing (section 4a D4)

Write-Host ''
Write-Host '=== Signature (D4) ===' -ForegroundColor Cyan

$signature = Get-AuthenticodeSignature -FilePath $msi.FullName

Write-Host ("Status : " + $signature.Status)

if ($signature.Status -eq 'Valid') {
    Write-Host ("Signer : " + $signature.SignerCertificate.Subject)
}
else {
    Write-Host 'Unsigned, as recorded in phase-10-hardening.md section 4a D4. SmartScreen warns once' -ForegroundColor Yellow
    Write-Host 'per machine; the user guide documents the More info / Run anyway path.' -ForegroundColor Yellow
}

# ------------------------------------------------------------------ verdict

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host '=== All structural checks passed ===' -ForegroundColor Green
    Write-Host 'Still outstanding for C10.2: an install on a genuinely fresh Windows machine.' -ForegroundColor Yellow
    exit 0
}

Write-Host ("=== " + $failures.Count + " check(s) failed ===") -ForegroundColor Red
foreach ($failure in $failures) {
    Write-Host ("  " + $failure) -ForegroundColor Red
}

exit 1
