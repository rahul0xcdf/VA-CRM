namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// A file being imported, read as a lazy sequence of records.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that makes Tally XML, Tally JSON and a mapped Excel sheet one import rather than
/// three (§4a D3). Everything downstream — validation, the per-row error report, the commit — is
/// written against this interface and has never heard of a file format.
/// </para>
/// <para>
/// <see cref="ReadAsync"/> is called <b>twice</b> per import and must start from the beginning each
/// time. §2 requires the whole file to be validated before anything is written, and §4a D2 requires
/// a full history not to be held in memory: the only way to have both is to read the file once to
/// validate and again to commit. An implementation therefore holds the means of opening its source,
/// not an open handle.
/// </para>
/// </remarks>
public interface IInteropSource
{
    /// <summary>How this source names itself in the error report — a file name, usually.</summary>
    string Description { get; }

    /// <summary>The format, for the report header. "Tally XML", "Tally JSON", "Excel".</summary>
    string FormatName { get; }

    /// <summary>Reads every record, from the start, each time it is called.</summary>
    IAsyncEnumerable<InteropRecord> ReadAsync(CancellationToken cancellationToken = default);
}
