# Installer & Packaging

**Phase 10** — `specs/phase-10-hardening.md` §2, checkpoints **C10.2**, **C10.3**, **C10.4**.

A per-machine Windows MSI that installs the whole application onto a computer with nothing on it —
no .NET runtime, no prerequisites, no internet.

## Building it

```powershell
.\installer\build-installer.ps1              # publish + package
.\installer\build-installer.ps1 -NoPublish   # repackage what is already staged
.\installer\verify-installer.ps1             # check the built MSI without installing it
```

Output: `installer\VinayakaAgencies.Installer\bin\Release\VinayakaAgencies-Setup.msi` (~64 MB).

Two steps in a fixed order. The installer project packages a **published directory**, not a project
reference — a project reference would hand it the framework-dependent build output, and the point of
§4a D6 is that the target machine needs no runtime installed.

> The `.wixproj` is deliberately **not in `VinayakaAgencies.sln`**. It can only build after a publish
> has staged the application, so including it would make `dotnet build` on the solution fail from a
> clean checkout. `build.ps1` is unaffected.

## Decisions behind the shape

| | Decision | Why |
|---|---|---|
| D6 | **MSI via WiX v5**, not MSIX | MSIX needs a signing certificate to install at all, and its per-user sandbox fights both the per-machine install §2 asks for and the shared `%ProgramData%` of D7 |
| D6 | **Self-contained `win-x64` publish** | The alternative is a .NET 8 Desktop Runtime prerequisite the installer downloads — the exact step that fails on the fresh machine of C10.2 when that machine has no internet. Costs ~70 MB, removes the prerequisite entirely |
| D7 | **Company data in `%ProgramData%`** | `%LOCALAPPDATA%` is per Windows user; see below |
| D4 | **Unsigned at launch**, signing wired | The certificate is a purchase the owner has to make, and an OV certificate's SmartScreen reputation is earned over downloads rather than granted at purchase — so buying one does not remove the day-one warning either |

Not trimmed. WPF resolves XAML types by name, so trimming removes types nothing statically
references — and the failure appears as a screen that will not load, at run time, on the customer's
machine.

## The data location, and why it changed (D7)

Phases 0–9 kept the database at `%LOCALAPPDATA%\VinayakaAgencies\motorpump.db`. `LOCALAPPDATA` is
**per Windows user**. A counter PC where two staff sign in under two Windows accounts would keep
**two different sets of books**, each invisible from the other, and neither would look broken — the
second user would simply be walked through first-run company creation and start billing into an
empty company. The per-machine install this phase adds is what makes that reachable.

| Location | Holds | Scope |
|---|---|---|
| `%ProgramData%\VinayakaAgencies\` | database, backups, logo, `appsettings.Local.json` | machine-wide |
| `%LOCALAPPDATA%\VinayakaAgencies\logs\` | Serilog rolling files | per user — correct, a log is one operator's |
| `%LOCALAPPDATA%\VinayakaAgencies\diagnostics\` | crash reports, generated bundles | per user |

**The ACL is not optional.** `%ProgramData%`'s default permissions let `Users` create files but not
modify files another account created — so without an explicit grant the first Windows account to run
the program would own `motorpump.db` and the second would be unable to post a voucher into it. The
MSI applies `util:PermissionEx` to the data folder and the backups folder, granting `Users` rather
than `Everyone` (which would include Guest, and these are the company's books).

**Existing installs are relocated, not abandoned.** `LegacyDataRelocator` runs during startup before
anything opens the database, copies the old root to the new one, verifies the database arrived, then
removes the originals and leaves a `moved-to-programdata.txt` marker. It refuses to merge: if both
locations hold a database it reports `AmbiguousLeftAlone` and touches neither, because two real sets
of books is exactly the state D7 predicts and choosing between them could discard a day's invoices.
Logs are never moved. A deployment that pinned `Database:ConnectionString` is skipped entirely.

## C10.3 — upgrade preserves data

Two independent mechanisms, and the first is the one that matters:

1. **The installer never owns `%ProgramData%`.** The database is created by the application at run
   time, so the MSI has no file record under the data folder to replace or remove. Confirmed by
   `verify-installer.ps1`, which asserts no installed file lives there.
2. `MajorUpgrade` with `AllowSameVersionUpgrades` and `Schedule="afterInstallInitialize"` — the old
   files go before the new ones land, so no stale assembly survives to be loaded beside a new one.

Pending **schema** migrations are applied by the Phase 9 gated upgrade window at next launch
(`ApplyMigrationsOnStartup` stays off), as one operator's deliberate act. The installer does not
touch the schema.

The MSI version is read from `Directory.Build.props` by the build script rather than restated —
an installer whose version does not advance will not upgrade an existing install, and two places to
edit is how that gets forgotten.

## C10.4 — uninstall preserves data

The two data-folder components are `Permanent="yes"`, which tells Windows Installer not to reverse
their actions. The folder and everything the application put in it survive an uninstall.
`NeverOverwrite="yes"` as well, so a repair cannot reset the ACL out from under a working install.

## ICE validation

Four checks are suppressed in the `.wixproj`, each with its reason recorded there:

- **ICE61** — fires by definition when `AllowSameVersionUpgrades` is set; WiX's own documentation
  says to suppress it with that attribute.
- **ICE38, ICE43, ICE57** — all three are written for *per-user* installs and demand an HKCU key
  path for components that touch a profile directory. This package is `perMachine`, so its Start
  Menu and Desktop shortcuts land in the All Users profile and the correct key path is the
  executable in Program Files; HKCU during an elevated install is the administrator's hive, not the
  operator's. ICE validation runs before `ALLUSERS` is resolved and cannot see this.

Everything else still runs, and it earns its keep: **ICE57 and ICE90 each caught a real mistake** in
the first draft — an ambiguous `HKMU` key path, and a shortcut hung off an ALL-CAPS public-property
directory. ICE38/43/57 are suppressed only *after* the shortcuts were restructured onto the
executable's own component, not instead of restructuring them.

## Signing (D4)

```powershell
$env:VA_SIGNING_THUMBPRINT = '<certificate thumbprint>'
.\installer\build-installer.ps1
```

When a thumbprint is present the script signs with `signtool` using SHA-256 and an RFC 3161
timestamp (without the timestamp the signature stops validating the day the certificate expires —
not theoretical for an installer kept on a USB stick). With no thumbprint it prints what the operator
will see instead, and the user guide documents the *More info → Run anyway* path.

## Developer notes

```
installer/
  build-installer.ps1                         publish, package, optionally sign
  verify-installer.ps1                        read the MSI tables and assert on them
  VinayakaAgencies.Installer/
    VinayakaAgencies.Installer.wixproj        WiX SDK project, ICE suppressions
    Package.wxs                               the package
    License.rtf                               shown by the wizard
```

`<Files Include="…**">` harvests the publish directory at build time, with
`<Exclude Files="…VinayakaAgencies.App.exe" />` (a **child element** — `Files` has no `Exclude`
attribute) because the executable is authored by hand so the shortcuts can hang off its component.
A hand-maintained manifest of 528 files would go stale the first time a package reference changed,
shipping an installer missing one DLL that only one screen needs.

## Verification

`verify-installer.ps1` reads the built MSI's tables and asserts 19 structural facts without
installing anything — the executable is present, `coreclr.dll` and `hostfxr.dll` are bundled, there
is no .NET launch condition, the data folder is under `CommonAppDataFolder` and is `Permanent`, no
installed file lives under it, an `Upgrade` row exists, `ALLUSERS=1`, and both shortcuts are
authored. It exists because most of the ways an installer fails on a fresh machine are visible in
its tables beforehand, which turns the owner's walkthrough into a confirmation rather than a first
discovery.

`tests/VinayakaAgencies.Application.Tests/Maintenance/LegacyDataRelocatorTests.cs` — 11 tests over
the relocation, including the three refusals (both locations populated, logs-only directory,
identical roots).

**Still outstanding:** C10.2, C10.3 and C10.4 need an install, an upgrade install and an uninstall on
a genuinely fresh Windows machine. That is the owner's step, and nothing above substitutes for it.
