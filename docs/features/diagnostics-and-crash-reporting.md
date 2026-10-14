# Diagnostics & Crash Reporting

**Phase 10** — `specs/phase-10-hardening.md` §2, final bullet: "crash reporting / log collection the
owner can actually send when something goes wrong."

## What it does

Two things, deliberately separate:

**Crash reports** are written the moment an unhandled fault reaches the top — one small text file,
no ZIP, no database access. The moment of a crash is the worst possible time to build an archive:
the process may be seconds from death.

**A diagnostics bundle** is built on demand from the Backup & Restore screen. It is one ZIP the owner
can attach to an email:

| Inside the bundle | What it is |
|---|---|
| `report.txt` | App version, OS, culture, data and log paths, database size and last write, free disk space, applied migrations, backup count and the age of the newest one, plus the operator's own note |
| `logs/log-*.txt` | The last 7 rolling log files, **redacted** |
| `crashes/crash-*.txt` | Every crash report on record, **redacted** |
| `settings/appsettings.Local.json` | The operator's configuration, **redacted** |

`report.txt` leads with what the owner cannot report themselves. Two entries earn their place
repeatedly: **free disk space on the drive holding the books** ("it stopped saving" is very often
this and nothing else), and **the applied migration count** — a mismatch between the build and the
schema is the single most useful fact in a bundle and the one an operator is least able to describe.
An install that has never completed a backup is said in words, not left to be inferred from a zero.

## Redaction is the hard requirement

The bundle is the one artefact *designed* to leave the premises, which makes it the most likely way
for a credential to escape. §4a D5 extends the Phase 6 C6.6 credential scan to cover it.

Every text entry goes through `SensitiveText.Redact` — the same two-pass scrub Phase 6 built for the
IRP call log — and **both passes run**:

- The **key pass** blanks any value under a credential-ish JSON key (`client_secret`, `password`,
  `token`, …). This catches anything the portal sends back in a shape nobody anticipated.
- The **value pass** replaces the exact secret strings this install holds. `DiagnosticsBundleService`
  resolves the stored NIC credentials for precisely this purpose. It catches a secret quoted back
  inside an error message under a field name nobody thought of — a log line reading
  `the portal echoed back <password> in the detail field` has no key at all, and the key pass alone
  would let it through intact.

Resolving the secrets can fail — DPAPI decrypts for one Windows account, so credentials saved by the
owner cannot be unprotected by a second operator on the same machine. Every failure returns an empty
list rather than propagating: the bundle is what somebody reaches for when the application is
already misbehaving, and withholding the diagnostics because the credential store could not be read
would fail at the exact moment it is needed. The key pass stays in force regardless.

`RecordCrashAsync` runs the key pass only, by design — it must not touch the database while the
process is dying.

## The three fault paths

Phases 0–9 handled one of three ways an unhandled fault reaches the top. All three are now covered:

| Handler | Fault | Outcome |
|---|---|---|
| `DispatcherUnhandledException` | UI thread — the common case | Logged `Fatal`, crash report written, process dies |
| `AppDomain.CurrentDomain.UnhandledException` | A background thread, bypassing the dispatcher | Logged `Fatal`, crash report written, process dies |
| `TaskScheduler.UnobservedTaskException` | A faulted `Task` nobody awaited | Logged `Error`, crash report written, **marked observed** |

The third is deliberately different. It is the one unhandled fault that is not fatal by default, and
swallowing it silently is how a failed background save becomes a mystery; but nothing is known to be
mid-write, so it is observed and the application continues.

The first two are **not** swallowed, which is the Phase 0 position restated: continuing after an
unhandled fault risks writing a half-posted voucher, and a crash with a logged cause is a far better
outcome than corrupt books.

## User workflow

**Gateway → Administration → Backup & Restore → Collect diagnostics.** The screen reports the path
and notes that credentials have been removed. The owner sends that one file.

Logs live at `%LOCALAPPDATA%\VinayakaAgencies\logs`, crash reports and bundles at
`%LOCALAPPDATA%\VinayakaAgencies\diagnostics` — per user, because they describe what one operator
did (§4a D7).

## Developer notes

```
Application/Maintenance/
  IDiagnosticsBundleService.cs    the port, plus the DiagnosticsBundle result
  DiagnosticsBundleService.cs     collection, the report, and the redaction
App/
  App.xaml.cs                     the three handlers, RecordCrash, and the log path
```

One subtlety worth keeping: `ReadAllTextSafelyAsync` opens with
`FileShare.ReadWrite | FileShare.Delete`. `File.ReadAllTextAsync` opens without sharing and would
fail against the live Serilog sink — today's log, the one file a diagnostics bundle most needs.

`App.ConfigureLogging` now takes its directory from `IAppDataPaths` rather than building the path
itself. Phases 0–9 derived it independently in two places; §4a D7 then moved one of them, and two
derivations of one location is how a bundle ends up collecting logs from a directory nothing writes
to.

## Verification

`StoredCredentials_NeverAppearInADiagnosticsBundle` in
`tests/VinayakaAgencies.Infrastructure.Tests/Einvoicing/EinvoiceWorkflowTests.cs`. It plants a secret
twice — once under a credential-ish key in the settings file, and once in plain prose in a log line
under no key at all — then reads every entry of the produced bundle as Latin-1 and asserts no stored
secret survives. The second plant is what makes the value pass non-decorative: with the key pass
alone, that log line leaves the premises intact.

**Not covered by a test:** the three fault handlers themselves. Deliberately — each one is a
process-level event, and a test that genuinely triggered an `AppDomain` unhandled exception would
take the test host down with it. They are three lines of registration in `BuildHost` calling one
method that *is* covered (`RecordCrashAsync`, via the bundle test). Flagged here rather than left
implied.
