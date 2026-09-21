using System.Text.Json;
using System.Text.Json.Serialization;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Printing;

namespace VinayakaAgencies.Application.Printing;

/// <summary>
/// Keeps template data honest: versions append, the current-revision pointer advances, and history
/// never edits (phase-3-sales-purchase-pipeline.md §2).
/// </summary>
public sealed class PrintTemplateService(IUnitOfWork unitOfWork, ICompanyContext companyContext)
    : IPrintTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintTemplateSummary>> ListAsync(
        PrintDocumentKind? documentKind = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var templates = await _unitOfWork.Repository<PrintTemplate>()
            .ListAsync(new TemplatesOfCompany(companyId, documentKind), cancellationToken)
            .ConfigureAwait(false);

        return templates
            .OrderBy(template => template.DocumentKind)
            .ThenBy(template => template.Priority)
            .ThenBy(template => template.NameNormalised, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PrintTemplateSummary?> ResolveDefaultAsync(
        PrintDocumentKind documentKind,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var templates = await _unitOfWork.Repository<PrintTemplate>()
            .ListAsync(new ActiveTemplatesOfKind(companyId, documentKind), cancellationToken)
            .ConfigureAwait(false);

        return templates
            .OrderBy(template => template.Priority)
            .ThenBy(template => template.CreatedAtUtc)
            .Select(ToSummary)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<PrintTemplateDetail?> GetAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var template = await _unitOfWork.Repository<PrintTemplate>()
            .GetByIdAsync(templateId, cancellationToken)
            .ConfigureAwait(false);

        if (template is null || template.CompanyId != companyId || template.CurrentRevisionId == Guid.Empty)
        {
            return null;
        }

        var currentRevision = await _unitOfWork.Repository<PrintTemplateRevision>()
            .GetByIdAsync(template.CurrentRevisionId, cancellationToken)
            .ConfigureAwait(false);

        if (currentRevision is null)
        {
            throw new InvalidOperationException(
                $"Template '{template.Name}' points at a revision that does not exist; the row is "
                + "corrupt and should be investigated before printing.");
        }

        var revisions = await _unitOfWork.Repository<PrintTemplateRevision>()
            .ListAsync(new RevisionsOf(templateId), cancellationToken)
            .ConfigureAwait(false);

        return ToDetail(template, Deserialise(currentRevision.LayoutJson), revisions);
    }

    /// <inheritdoc />
    public async Task<PrintTemplateDetail> CreateAsync(
        CreatePrintTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var normalised = NameNormaliser.Normalise(request.Name);

        if (await _unitOfWork.Repository<PrintTemplate>()
                .AnyAsync(new TemplateByName(companyId, request.DocumentKind, normalised), cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"A '{request.Name}' template already exists for {request.DocumentKind} documents.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var template = new PrintTemplate(
            companyId,
            request.DocumentKind,
            request.Name,
            request.Priority,
            request.Description);

        var revision = AppendFirstRevision(template, request.Layout);

        await _unitOfWork.Repository<PrintTemplate>()
            .AddAsync(template, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.Repository<PrintTemplateRevision>()
            .AddAsync(revision, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new PrintTemplateDetail(
            template.Id,
            template.DocumentKind,
            template.Name,
            template.Description,
            template.Priority,
            template.CurrentVersion,
            template.IsActive,
            request.Layout,
            [new PrintTemplateRevisionSummary(revision.Version, revision.CreatedAtUtc, revision.CreatedBy)]);
    }

    /// <inheritdoc />
    public async Task<PrintTemplateDetail> SaveLayoutAsync(
        Guid templateId,
        PrintTemplateLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var template = await _unitOfWork.Repository<PrintTemplate>()
            .GetByIdAsync(templateId, cancellationToken)
            .ConfigureAwait(false);

        if (template is null || template.CompanyId != companyId)
        {
            throw new InvalidOperationException("That print template does not exist in this company's books.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var revision = new PrintTemplateRevision(
            companyId,
            template.Id,
            template.CurrentVersion + 1,
            Serialise(layout));

        template.AttachRevision(revision.Id, revision.Version);

        await _unitOfWork.Repository<PrintTemplateRevision>()
            .AddAsync(revision, cancellationToken)
            .ConfigureAwait(false);
        _unitOfWork.Repository<PrintTemplate>().Update(template);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var revisions = await _unitOfWork.Repository<PrintTemplateRevision>()
            .ListAsync(new RevisionsOf(templateId), cancellationToken)
            .ConfigureAwait(false);

        return ToDetail(template, layout, revisions);
    }

    /// <inheritdoc />
    public async Task<PrintTemplateSummary> SetActiveAsync(
        Guid templateId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var template = await _unitOfWork.Repository<PrintTemplate>()
            .GetByIdAsync(templateId, cancellationToken)
            .ConfigureAwait(false);

        if (template is null || template.CompanyId != companyId)
        {
            throw new InvalidOperationException("That print template does not exist in this company's books.");
        }

        if (isActive)
        {
            template.Activate();
        }
        else
        {
            if (template.IsActive
                && (await CountActiveTemplatesOfKindAsync(template, cancellationToken).ConfigureAwait(false)) <= 1)
            {
                throw new InvalidOperationException(
                    $"'{template.Name}' is the last active template for {template.DocumentKind} "
                    + "documents; activate another one before retiring it, so the kind always has "
                    + "something to print with.");
            }

            template.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(template);
    }

    private async Task<int> CountActiveTemplatesOfKindAsync(
        PrintTemplate template,
        CancellationToken cancellationToken)
    {
        var active = await _unitOfWork.Repository<PrintTemplate>()
            .ListAsync(
                new ActiveTemplatesOfKind(template.CompanyId, template.DocumentKind),
                cancellationToken)
            .ConfigureAwait(false);

        return active.Count;
    }

    /// <summary>The stored form of a layout. One serialiser, so writer and reader cannot drift.</summary>
    public static string Serialise(PrintTemplateLayout layout) =>
        JsonSerializer.Serialize(layout, JsonOptions);

    public static PrintTemplateLayout Deserialise(string json) =>
        JsonSerializer.Deserialize<PrintTemplateLayout>(json, JsonOptions)
        ?? throw new InvalidOperationException("The stored template layout is empty or malformed.");

    private static PrintTemplateRevision AppendFirstRevision(
        PrintTemplate template,
        PrintTemplateLayout layout)
    {
        var revision = new PrintTemplateRevision(
            template.CompanyId,
            template.Id,
            version: 1,
            Serialise(layout));

        template.AttachRevision(revision.Id, revision.Version);
        return revision;
    }

    private static PrintTemplateSummary ToSummary(PrintTemplate template) => new(
        template.Id,
        template.DocumentKind,
        template.Name,
        template.Description,
        template.Priority,
        template.CurrentVersion,
        template.IsActive);

    private static PrintTemplateDetail ToDetail(
        PrintTemplate template,
        PrintTemplateLayout layout,
        IEnumerable<PrintTemplateRevision> revisions) => new(
        template.Id,
        template.DocumentKind,
        template.Name,
        template.Description,
        template.Priority,
        template.CurrentVersion,
        template.IsActive,
        layout,
        revisions
            .OrderByDescending(revision => revision.Version)
            .Select(revision => new PrintTemplateRevisionSummary(
                revision.Version,
                revision.CreatedAtUtc,
                revision.CreatedBy))
            .ToList());

    private sealed class TemplatesOfCompany : Specification<PrintTemplate>
    {
        public TemplatesOfCompany(Guid companyId, PrintDocumentKind? documentKind)
        {
            Where(template => template.CompanyId == companyId
                && (documentKind.HasValue == false || template.DocumentKind == documentKind.Value));
            ApplyNoTracking();
        }
    }

    private sealed class ActiveTemplatesOfKind : Specification<PrintTemplate>
    {
        public ActiveTemplatesOfKind(Guid companyId, PrintDocumentKind documentKind)
        {
            Where(template => template.CompanyId == companyId
                && template.DocumentKind == documentKind
                && template.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class TemplateByName : Specification<PrintTemplate>
    {
        public TemplateByName(Guid companyId, PrintDocumentKind documentKind, string nameNormalised)
        {
            Where(template => template.CompanyId == companyId
                && template.DocumentKind == documentKind
                && template.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class RevisionsOf : Specification<PrintTemplateRevision>
    {
        public RevisionsOf(Guid templateId)
        {
            Where(revision => revision.TemplateId == templateId);
            ApplyNoTracking();
        }
    }
}
