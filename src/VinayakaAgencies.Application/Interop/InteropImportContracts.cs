namespace VinayakaAgencies.Application.Interop;

/// <summary>What an import should bring in.</summary>
/// <remarks>
/// Masters and vouchers are separately selectable because a migration is usually run in that order:
/// masters first so they can be reviewed and corrected, transactions afterwards once the chart of
/// accounts reads the way the owner expects.
/// </remarks>
public sealed record InteropImportOptions(
    bool ImportMasters = true,
    bool ImportVouchers = true)
{
    public static InteropImportOptions MastersOnly() => new(ImportMasters: true, ImportVouchers: false);

    public static InteropImportOptions Everything() => new();
}

/// <summary>
/// One reason a record cannot be imported, tied to where it sits in the file.
/// </summary>
/// <remarks>
/// C8.4: an import with one invalid row commits nothing and reports the offending row and reason.
/// Both halves are here — <see cref="Location"/> is how the owner finds it in the file, and
/// <see cref="Reason"/> is written to be read by the person who has to fix the source, not by a
/// developer reading a log.
/// </remarks>
public sealed record InteropImportProblem(
    InteropSourceLocation Location,
    string RecordKind,
    string RecordLabel,
    string Reason)
{
    public override string ToString() =>
        string.IsNullOrWhiteSpace(RecordLabel)
            ? $"{Location}: {RecordKind} — {Reason}"
            : $"{Location}: {RecordKind} '{RecordLabel}' — {Reason}";
}

/// <summary>
/// Something the owner should know about a record that is nonetheless going to be imported.
/// </summary>
/// <remarks>
/// Distinct from a problem because it does not block the commit. The case this exists for is the
/// serial-tracked item: a Tally file carries the quantity but not the serial numbers, and refusing
/// the row would make a pump business unable to migrate at all, while importing it silently would
/// leave stock on hand that the serial lifecycle knows nothing about. Saying so and letting the
/// owner decide is the only honest third option.
/// </remarks>
public sealed record InteropImportWarning(
    InteropSourceLocation Location,
    string RecordKind,
    string RecordLabel,
    string Note)
{
    public override string ToString() => $"{Location}: {RecordKind} '{RecordLabel}' — {Note}";
}

/// <summary>How many records of one kind the file holds, split by what will happen to them.</summary>
/// <remarks>
/// The created/updated split is what tells the owner a re-import is behaving. Running the same file
/// twice should report everything as updated the second time; anything still counted as created is
/// C8.2 failing, and it is visible before the commit rather than afterwards in a duplicated ledger.
/// </remarks>
public sealed record InteropRecordTally(string Kind, int Created, int Updated)
{
    public int Total => Created + Updated;
}

/// <summary>The result of the validation pass: everything wrong with the file, and what it holds.</summary>
public sealed record InteropImportPreview(
    string SourceDescription,
    string FormatName,
    IReadOnlyList<InteropImportProblem> Problems,
    IReadOnlyList<InteropImportWarning> Warnings,
    IReadOnlyList<InteropRecordTally> Tallies,
    InteropTotals ParsedTotals)
{
    public int RecordCount => Tallies.Sum(tally => tally.Total);

    public int CreatedCount => Tallies.Sum(tally => tally.Created);

    public int UpdatedCount => Tallies.Sum(tally => tally.Updated);

    /// <summary>
    /// Whether the commit may proceed.
    /// </summary>
    /// <remarks>
    /// One problem is enough to stop the whole file (§2, C8.4). Partial imports are the thing a
    /// migration cannot recover from: the owner cannot tell which half went in, and running the
    /// corrected file again would duplicate whatever succeeded the first time.
    /// </remarks>
    public bool CanCommit => Problems.Count == 0 && RecordCount > 0;

    public static InteropImportPreview Empty(string sourceDescription, string formatName) =>
        new(sourceDescription, formatName, [], [], [], new InteropTotals(0, 0));
}

/// <summary>Progress during a commit, for a file large enough that the screen has to say something.</summary>
public sealed record InteropImportProgress(int RecordsWritten, int RecordsTotal, string CurrentKind)
{
    public double Fraction => RecordsTotal == 0 ? 0d : (double)RecordsWritten / RecordsTotal;
}

/// <summary>What the import did.</summary>
public sealed record InteropImportResult(
    InteropImportPreview Preview,
    bool Committed,
    string? RefusalReason = null,
    InteropReconciliation? Reconciliation = null)
{
    public static InteropImportResult Refused(InteropImportPreview preview, string reason) =>
        new(preview, Committed: false, reason);
}
