using System.Linq.Expressions;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.Application.Interop;

/// <summary>The saved column mappings (§2) — list, edit, and a first guess at a new one.</summary>
public interface IExcelMappingService
{
    Task<IReadOnlyList<ExcelColumnMappingSummary>> ListAsync(
        ExcelImportTarget? target = null,
        CancellationToken cancellationToken = default);

    Task<ExcelColumnMappingDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ExcelColumnMappingDetail> SaveAsync(
        SaveExcelColumnMappingRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The sheets in a workbook, for the screen's sheet picker.</summary>
    Task<IReadOnlyList<string>> ListSheetsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a sheet's headers and proposes a field for each.
    /// </summary>
    Task<IReadOnlyList<SuggestedColumn>> SuggestAsync(
        string path,
        ExcelImportTarget target,
        string? sheetName = null,
        int headerRowNumber = 1,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IExcelMappingService" />
public sealed class ExcelMappingService : IExcelMappingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICompanyContext _companyContext;
    private readonly IWorkbookReader _workbookReader;

    public ExcelMappingService(
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

    public async Task<IReadOnlyList<ExcelColumnMappingSummary>> ListAsync(
        ExcelImportTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var mappings = await LoadAllAsync(companyId, cancellationToken).ConfigureAwait(false);

        return
        [
            .. mappings
                .Where(mapping => target is null || mapping.Target == target)
                .OrderBy(mapping => mapping.NameNormalised, StringComparer.Ordinal)
                .Select(Summarise),
        ];
    }

    public async Task<ExcelColumnMappingDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mapping = await LoadAsync(id, cancellationToken).ConfigureAwait(false);

        return mapping is null ? null : Detail(mapping);
    }

    public async Task<ExcelColumnMappingDetail> SaveAsync(
        SaveExcelColumnMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        // Checked here rather than in the entity because the valid set depends on the target, and the
        // catalogue that knows it is an Application concept. A mapping saved against a field its
        // target does not have would look correct in the list and fail every import.
        var unknown = request.Columns
            .Where(column => !InteropFieldCatalogue.Knows(request.Target, column.TargetField))
            .Select(column => column.TargetField)
            .ToList();

        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", unknown)} — not a field a {request.Target} mapping can fill. "
                + $"Available: {string.Join(", ", InteropFieldCatalogue.For(request.Target).Select(f => f.Name))}.");
        }

        var mapping = request.Id is { } id
            ? await LoadAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("That mapping no longer exists.")
            : null;

        if (mapping is null)
        {
            mapping = new ExcelColumnMapping(
                companyId,
                request.Name,
                request.Target,
                request.SheetName,
                request.HeaderRowNumber);

            await _unitOfWork.Repository<ExcelColumnMapping>()
                .AddAsync(mapping, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            mapping.Rename(request.Name);
            mapping.SetSheet(request.SheetName);
            mapping.SetHeaderRow(request.HeaderRowNumber);
        }

        mapping.SetColumns(request.Columns.Select(column =>
            new ExcelColumnMappingColumn(companyId, mapping.Id, column.SourceHeader, column.TargetField)));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Detail(mapping);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mapping = await LoadAsync(id, cancellationToken).ConfigureAwait(false);

        if (mapping is null)
        {
            return;
        }

        // Deactivated, not removed. A mapping names the layout a real file arrived in, and the edit
        // log's record of an import that used it is worth more than the row it costs to keep.
        mapping.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListSheetsAsync(string path, CancellationToken cancellationToken = default) =>
        _workbookReader.ListSheetsAsync(path, cancellationToken);

    public async Task<IReadOnlyList<SuggestedColumn>> SuggestAsync(
        string path,
        ExcelImportTarget target,
        string? sheetName = null,
        int headerRowNumber = 1,
        CancellationToken cancellationToken = default)
    {
        var fields = InteropFieldCatalogue.For(target);
        var headers = await HeadersAsync(path, sheetName, headerRowNumber, cancellationToken).ConfigureAwait(false);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<SuggestedColumn>(headers.Count);

        foreach (var header in headers)
        {
            var match = Suggest(header, fields, taken);

            if (match is not null)
            {
                taken.Add(match.Name);
            }

            suggestions.Add(new SuggestedColumn(header, match?.Name, match?.IsRequired ?? false));
        }

        return suggestions;
    }

    /// <summary>
    /// The field a header most likely feeds.
    /// </summary>
    /// <remarks>
    /// Exact normalised match first, then a contains match in either direction — "Item Name" against
    /// <c>Name</c>, "Unit" against <c>BaseUnit</c>. A field already claimed by an earlier column is
    /// not offered twice, because two columns cannot feed one field and the first one to match is the
    /// leftmost, which is the one a person reading the sheet would also pick.
    /// </remarks>
    private static InteropField? Suggest(
        string header,
        IReadOnlyList<InteropField> fields,
        HashSet<string> taken)
    {
        var normalised = Squash(header);
        var available = fields.Where(field => !taken.Contains(field.Name)).ToList();

        var exact = available.FirstOrDefault(field => Squash(field.Name) == normalised);

        if (exact is not null)
        {
            return exact;
        }

        return available.FirstOrDefault(field =>
        {
            var fieldName = Squash(field.Name);

            return normalised.Contains(fieldName, StringComparison.Ordinal)
                || fieldName.Contains(normalised, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Letters and digits only, upper-cased.
    /// </summary>
    /// <remarks>
    /// Harsher than <see cref="NameNormaliser"/>, which keeps the spaces between words because two
    /// master names differing only by a space are two different masters. Here the opposite is
    /// wanted: "Base Unit" and "BaseUnit" are the same column, and so are "HSN/SAC" and "HSN SAC".
    /// This is only ever used to propose a mapping the owner then confirms, so being generous costs
    /// a correction and being strict costs them the whole grid.
    /// </remarks>
    private static string Squash(string value) =>
        new([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private async Task<IReadOnlyList<string>> HeadersAsync(
        string path,
        string? sheetName,
        int headerRowNumber,
        CancellationToken cancellationToken)
    {
        await foreach (var row in _workbookReader.ReadRowsAsync(path, sheetName, cancellationToken).ConfigureAwait(false))
        {
            if (row.RowNumber == headerRowNumber)
            {
                return [.. row.Cells.Where(cell => !string.IsNullOrWhiteSpace(cell)).Select(cell => cell!.Trim())];
            }

            if (row.RowNumber > headerRowNumber)
            {
                break;
            }
        }

        return [];
    }

    private async Task<ExcelColumnMapping?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var mappings = await LoadAllAsync(companyId, cancellationToken).ConfigureAwait(false);

        return mappings.SingleOrDefault(mapping => mapping.Id == id);
    }

    private Task<IReadOnlyList<ExcelColumnMapping>> LoadAllAsync(Guid companyId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<ExcelColumnMapping>().ListAsync(
            new WithColumns(mapping => mapping.CompanyId == companyId && mapping.IsActive),
            cancellationToken);

    private static ExcelColumnMappingSummary Summarise(ExcelColumnMapping mapping) => new(
        mapping.Id,
        mapping.Name,
        mapping.Target,
        mapping.SheetName,
        mapping.HeaderRowNumber,
        mapping.Columns.Count,
        mapping.IsActive);

    private static ExcelColumnMappingDetail Detail(ExcelColumnMapping mapping) => new(
        mapping.Id,
        mapping.Name,
        mapping.Target,
        mapping.SheetName,
        mapping.HeaderRowNumber,
        [.. mapping.Columns.Select(column => new ExcelColumnMappingLine(column.SourceHeader, column.TargetField))],
        mapping.IsActive);

    private sealed class WithColumns : Specification<ExcelColumnMapping>
    {
        public WithColumns(Expression<Func<ExcelColumnMapping, bool>> criteria)
        {
            Where(criteria);
            AddInclude(mapping => mapping.Columns);
        }
    }
}
