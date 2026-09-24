using System.IO;
using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.App.Tests.ViewModels;

/// <summary>Stands in for the export service, recording what the screen asked for.</summary>
internal sealed class FakeInteropExportService : IInteropExportService
{
    public InteropExportRequest? LastRequest { get; private set; }

    public InteropDocument Document { get; set; } = new() { CompanyName = "Vinayaka Agencies" };

    public Task<InteropDocument> BuildAsync(
        InteropExportRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Document);
    }

    public Task<InteropExportSummary> WriteAsync(
        InteropDocument document,
        InteropFormat format,
        Stream destination,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InteropExportSummary.For(document, format));
}

internal sealed class FakeInteropWorkbookExporter : IInteropWorkbookExporter
{
    public Task<InteropExportSummary> WriteAsync(
        InteropDocument document,
        Stream destination,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InteropExportSummary.For(document, InteropFormat.Excel));
}

/// <summary>Returns whatever preview and result a test stages.</summary>
internal sealed class FakeInteropImportService : IInteropImportService
{
    public InteropImportPreview Preview { get; set; } = InteropImportPreview.Empty("file.xml", "Tally XML");

    public InteropImportResult? Result { get; set; }

    public Task<InteropImportPreview> ValidateAsync(
        IInteropSource source,
        InteropImportOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Preview);

    public Task<InteropImportResult> CommitAsync(
        IInteropSource source,
        InteropImportOptions options,
        IProgress<InteropImportProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result ?? new InteropImportResult(Preview, Committed: true));
}

internal sealed class FakeInteropSourceFactory : IInteropSourceFactory
{
    public IInteropSource ForTallyFile(string path) => TallyXmlSource.FromText("<ENVELOPE />", path);

    public Task<IInteropSource> ForWorkbookAsync(
        string path,
        Guid mappingId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IInteropSource>(TallyXmlSource.FromText("<ENVELOPE />", path));
}

/// <summary>An in-memory mapping store, enough for the screen's own rules.</summary>
internal sealed class FakeExcelMappingService : IExcelMappingService
{
    private readonly List<ExcelColumnMappingDetail> _mappings = [];

    public IReadOnlyList<SuggestedColumn> Suggestions { get; set; } = [];

    public IReadOnlyList<string> Sheets { get; set; } = ["Sheet1"];

    public IReadOnlyList<ExcelColumnMappingDetail> Saved => _mappings;

    public Task<IReadOnlyList<ExcelColumnMappingSummary>> ListAsync(
        ExcelImportTarget? target = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExcelColumnMappingSummary>>(
        [
            .. _mappings
                .Where(mapping => target is null || mapping.Target == target)
                .Select(mapping => new ExcelColumnMappingSummary(
                    mapping.Id,
                    mapping.Name,
                    mapping.Target,
                    mapping.SheetName,
                    mapping.HeaderRowNumber,
                    mapping.Columns.Count,
                    mapping.IsActive)),
        ]);

    public Task<ExcelColumnMappingDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_mappings.FirstOrDefault(mapping => mapping.Id == id));

    public Task<ExcelColumnMappingDetail> SaveAsync(
        SaveExcelColumnMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        var unknown = request.Columns
            .Where(column => !InteropFieldCatalogue.Knows(request.Target, column.TargetField))
            .ToList();

        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"{unknown[0].TargetField} — not a field a {request.Target} mapping can fill.");
        }

        var saved = new ExcelColumnMappingDetail(
            request.Id ?? Guid.NewGuid(),
            request.Name,
            request.Target,
            request.SheetName,
            request.HeaderRowNumber,
            request.Columns,
            IsActive: true);

        _mappings.RemoveAll(mapping => mapping.Id == saved.Id);
        _mappings.Add(saved);

        return Task.FromResult(saved);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _mappings.RemoveAll(mapping => mapping.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListSheetsAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sheets);

    public Task<IReadOnlyList<SuggestedColumn>> SuggestAsync(
        string path,
        ExcelImportTarget target,
        string? sheetName = null,
        int headerRowNumber = 1,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Suggestions);
}
