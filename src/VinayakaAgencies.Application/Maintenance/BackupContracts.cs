namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// What a backup archive says about itself. Written into the archive as <c>manifest.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The manifest exists so that an archive can be judged without being restored. A restore is a
/// destructive act — it replaces the live books — so the operator has to be able to see what they
/// are about to get: when it was taken, by whom, against which schema, and whether its contents
/// still hash to what they hashed when written.
/// </para>
/// <para>
/// <see cref="SchemaMigrations"/> is the field that prevents the worst outcome. Restoring a database
/// taken from a newer schema into an older build, or the reverse, produces a program that runs and
/// then fails somewhere in the middle of billing on a column that is not there. Recording the
/// applied migrations lets the restore refuse up front.
/// </para>
/// </remarks>
public sealed record BackupManifest(
    int FormatVersion,
    string AppVersion,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    string MachineName,
    string? CompanyName,
    Guid? CompanyId,
    string DatabaseFileName,
    IReadOnlyList<string> SchemaMigrations,
    IReadOnlyList<BackupEntry> Entries,
    BookFingerprint Fingerprint,
    string Reason)
{
    /// <summary>
    /// Version of the archive layout, not of the application.
    /// </summary>
    /// <remarks>
    /// Checked on restore so that a future layout change is a clear refusal rather than a
    /// misread archive. Bump it only when the <i>shape</i> changes.
    /// </remarks>
    public const int CurrentFormatVersion = 1;

    /// <summary>Name of the manifest entry inside the archive.</summary>
    public const string EntryName = "manifest.json";
}

/// <summary>One file inside the archive, with the hash it had when it was written.</summary>
/// <param name="Path">Path within the archive, forward-slashed.</param>
/// <param name="Length">Length in bytes.</param>
/// <param name="Sha256">Upper-case hex SHA-256 of the content.</param>
public sealed record BackupEntry(string Path, long Length, string Sha256);

/// <summary>An archive on disk, as the backup screen lists it.</summary>
public sealed record BackupSummary(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    BackupManifest? Manifest,
    BackupVerification? Verification,
    IReadOnlyList<BackupDestinationResult> Destinations)
{
    /// <summary>File name alone, which is what the screen shows.</summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);

    /// <summary>
    /// True only when a verification has actually run and passed.
    /// </summary>
    /// <remarks>
    /// Deliberately false for "not verified" as well as for "failed". §2 asks for verified
    /// backups, and an archive nobody checked is not one — presenting it as good because nothing
    /// went wrong while writing it is the exact assurance a backup is supposed to replace.
    /// </remarks>
    public bool IsVerified => Verification is { IsValid: true };
}

/// <summary>The result of reopening an archive and checking it.</summary>
public sealed record BackupVerification(
    bool IsValid,
    IReadOnlyList<string> Problems,
    bool DatabaseIntegrityIntact,
    BookFingerprint? Fingerprint)
{
    public static BackupVerification Valid(bool integrityIntact, BookFingerprint? fingerprint) =>
        new(IsValid: true, Problems: [], integrityIntact, fingerprint);

    public static BackupVerification Invalid(params string[] problems) =>
        new(IsValid: false, problems, DatabaseIntegrityIntact: false, Fingerprint: null);
}

/// <summary>Whether one destination received a copy of the archive (§4a D1).</summary>
public sealed record BackupDestinationResult(string Path, bool Succeeded, string? Problem)
{
    public static BackupDestinationResult Written(string path) => new(path, Succeeded: true, null);

    public static BackupDestinationResult Failed(string path, string problem) =>
        new(path, Succeeded: false, problem);
}

/// <summary>What a restore did, and whether the books came back identical (C10.6).</summary>
/// <remarks>
/// <see cref="SafetyCopyPath"/> is not a nicety. A restore overwrites the live database, and an
/// operator who picks the wrong archive has destroyed today's billing unless the thing they
/// overwrote was kept. That copy is taken before the replacement and its path is reported, so the
/// undo is a file copy the owner can be talked through on the telephone.
/// </remarks>
public sealed record RestoreOutcome(
    string ArchivePath,
    string SafetyCopyPath,
    BookFingerprint ExpectedFingerprint,
    BookFingerprint ActualFingerprint,
    IReadOnlyList<string> RestoredEntries,
    string Explanation)
{
    /// <summary>C10.6: the restored books render identically to the backed-up ones.</summary>
    public bool FingerprintMatches => ActualFingerprint.Matches(ExpectedFingerprint);
}
