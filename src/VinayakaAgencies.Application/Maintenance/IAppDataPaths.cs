namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Where this install keeps the things it owns: the company database, its backups, the operator's
/// logs (phase-10-hardening.md §4a D7).
/// </summary>
/// <remarks>
/// One place answers the question, because Phases 0–9 answered it in two — the SQLite resolver and
/// the Serilog configuration each built their own path from <c>%LOCALAPPDATA%</c> — and two
/// independent derivations of the same path are two things that can drift.
/// <para>
/// The split between the two roots is deliberate and is the point of D7. <see cref="DataRoot"/> is
/// machine-wide because the books belong to the business: a counter PC where two staff sign in
/// under two Windows accounts must not end up with two sets of them.
/// <see cref="LogDirectory"/> stays per-user because a log describes what one operator did.
/// </para>
/// </remarks>
public interface IAppDataPaths
{
    /// <summary>
    /// Machine-wide root for company data — database, backups, logo, local configuration.
    /// </summary>
    string DataRoot { get; }

    /// <summary>
    /// The per-user root Phases 0–9 used, kept only so an upgrade can find what it must move.
    /// </summary>
    /// <remarks>
    /// Read by <c>LegacyDataRelocator</c> and by nothing else. Writing here again would recreate
    /// the defect D7 exists to fix.
    /// </remarks>
    string LegacyDataRoot { get; }

    /// <summary>Per-user log directory.</summary>
    string LogDirectory { get; }

    /// <summary>Per-user directory holding crash reports and generated diagnostic bundles.</summary>
    string DiagnosticsDirectory { get; }

    /// <summary>Backups, beside the database by default (§4a D1).</summary>
    string BackupDirectory { get; }

    /// <summary>
    /// The optional <c>appsettings.Local.json</c> under <see cref="DataRoot"/>: operator-owned
    /// configuration, and therefore something a backup has to carry.
    /// </summary>
    string LocalSettingsPath { get; }

    /// <summary>
    /// Creates every directory above that does not yet exist, and returns <see cref="DataRoot"/>.
    /// </summary>
    string EnsureCreated();
}
