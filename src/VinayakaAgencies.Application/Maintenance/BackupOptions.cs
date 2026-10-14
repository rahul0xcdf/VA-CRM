namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Backup settings bound from the <c>Backup</c> configuration section
/// (phase-10-hardening.md §4a D1).
/// </summary>
/// <remarks>
/// Every default here is chosen so that an install nobody has configured still takes verified
/// backups. A backup feature that needs setting up before it works is a backup feature that is off
/// on the day it was needed.
/// </remarks>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>The default number of archives kept (§4a D1).</summary>
    public const int DefaultRetainedBackupCount = 30;

    /// <summary>
    /// Optional second destination — a network share or an external drive.
    /// </summary>
    /// <remarks>
    /// Empty by default, and §4a D2 spells out what that costs: with only the local copy, fire,
    /// theft or a dead disk takes the books with the machine. Failing to reach this destination is
    /// reported but does not fail the backup — losing the off-machine copy is bad, losing the local
    /// one because a USB drive was unplugged is worse.
    /// </remarks>
    public string? SecondDestination { get; set; }

    /// <summary>How many archives to keep at each destination.</summary>
    public int RetainedBackupCount { get; set; } = DefaultRetainedBackupCount;

    /// <summary>Whether the in-application timer takes scheduled backups.</summary>
    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>Hours between scheduled backups while the application is open.</summary>
    public int ScheduleIntervalHours { get; set; } = 24;

    /// <summary>
    /// Take a backup when the application closes cleanly.
    /// </summary>
    /// <remarks>
    /// On by default because it is the one moment the books are known to be quiet and the operator
    /// is known to be finished. §5 records why there is no Windows service behind this: a day
    /// nobody opened the application is a day with nothing to back up.
    /// </remarks>
    public bool BackupOnShutdown { get; set; } = true;

    /// <summary>
    /// Verify every archive by reopening it and checking it, rather than trusting the write.
    /// </summary>
    /// <remarks>
    /// Configurable only so a very large book can turn it off deliberately; §2 requires verified
    /// backups, so the default is on and the evidence is taken with it on. An unverified archive
    /// is recorded as unverified rather than counted as a backup.
    /// </remarks>
    public bool VerifyAfterWrite { get; set; } = true;
}
