using System.Linq.Expressions;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.Application.Interop;

/// <summary>Turns a file the owner picked into something the importer can read.</summary>
/// <remarks>
/// This exists so the screen never holds a domain entity or decides a file format. It hands over a
/// path and, for a spreadsheet, the mapping the owner chose; what comes back is an
/// <see cref="IInteropSource"/> that the import service treats identically whatever it came from.
/// </remarks>
public interface IInteropSourceFactory
{
    /// <summary>A Tally file, XML or JSON, chosen by its extension.</summary>
    IInteropSource ForTallyFile(string path);

    /// <summary>A spreadsheet, read through a saved mapping.</summary>
    Task<IInteropSource> ForWorkbookAsync(
        string path,
        Guid mappingId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IInteropSourceFactory" />
public sealed class InteropSourceFactory : IInteropSourceFactory
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICompanyContext _companyContext;
    private readonly IWorkbookReader _workbookReader;

    public InteropSourceFactory(
        IUnitOfWork unitOfWork,
        ICompanyContext companyContext,
        IWorkbookReader workbookReader)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(workbookReader);

        _unitOfWork = unitOfWork;
        _companyContext = companyContext;
        _workbookReader = workbookReader;
    }

    public IInteropSource ForTallyFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // By extension, not by sniffing the content. A file whose name says .xml and whose bytes are
        // JSON is a mistake worth reporting rather than working around, and the reader reports it
        // clearly — "the file is not valid XML" names the actual problem.
        return Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".JSON" => TallyJsonSource.FromFile(path),
            ".XML" => TallyXmlSource.FromFile(path),
            var other => throw new InvalidOperationException(
                $"'{other}' is not a Tally interchange file. Expected .xml, or .json from TallyPrime 7.0 "
                + "onwards."),
        };
    }

    public async Task<IInteropSource> ForWorkbookAsync(
        string path,
        Guid mappingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var mapping = await _unitOfWork.Repository<ExcelColumnMapping>()
            .SingleOrDefaultAsync(
                new WithColumns(candidate => candidate.CompanyId == companyId && candidate.Id == mappingId),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "That column mapping no longer exists. Pick another, or build one from the sheet.");

        return new ExcelInteropSource(_workbookReader, path, mapping);
    }

    private sealed class WithColumns : Specification<ExcelColumnMapping>
    {
        public WithColumns(Expression<Func<ExcelColumnMapping, bool>> criteria)
        {
            Where(criteria);
            AddInclude(mapping => mapping.Columns);
        }
    }
}
