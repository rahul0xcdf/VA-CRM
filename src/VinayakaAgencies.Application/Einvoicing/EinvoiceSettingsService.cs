using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Manages the e-invoicing configuration and the encrypted NIC credentials
/// (phase-6-einvoice-eway.md §4a).
/// </summary>
/// <remarks>
/// Plaintext credentials exist in exactly two places in this class: the argument to
/// <see cref="SaveCredentialsAsync"/>, on its way into the protector, and the record returned by
/// <see cref="ResolveCredentialsAsync"/>, on its way to one call. Nothing is cached between them and
/// no field holds either. That is a constraint rather than a preference — checkpoint C6.6 asserts
/// credentials never reach logs, exports or the audit trail, and the surest way to keep that true is
/// for there to be no long-lived place a secret sits.
/// </remarks>
public sealed class EinvoiceSettingsService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    ICredentialProtector protector) : IEinvoiceSettingsService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly ICredentialProtector _protector = protector
        ?? throw new ArgumentNullException(nameof(protector));

    /// <inheritdoc />
    public async Task<EinvoiceSettingsSummary> GetAsync(CancellationToken cancellationToken = default) =>
        ToSummary(await GetEntityAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<EinvoiceSettings> GetEntityAsync(CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var existing = await _unitOfWork.Repository<EinvoiceSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        // Created on first read rather than provisioned at startup, matching how Phase 5 treats
        // GstSettings: a row of defaults nobody has looked at is not worth a migration step.
        var created = new EinvoiceSettings(companyId);

        await _unitOfWork.Repository<EinvoiceSettings>().AddAsync(created, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created;
    }

    /// <inheritdoc />
    public async Task<EinvoiceSettingsSummary> SetApplicabilityAsync(
        bool isApplicable,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetEntityAsync(cancellationToken).ConfigureAwait(false);

        settings.SetApplicability(isApplicable);

        return await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EinvoiceSettingsSummary> SetEnvironmentAsync(
        IrpEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetEntityAsync(cancellationToken).ConfigureAwait(false);

        settings.SetEnvironment(environment);

        return await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EinvoiceSettingsSummary> SetEwayBillThresholdAsync(
        decimal threshold,
        CancellationToken cancellationToken = default)
    {
        if (decimal.IsNegative(threshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold), threshold, "A threshold cannot be negative.");
        }

        var settings = await GetEntityAsync(cancellationToken).ConfigureAwait(false);

        settings.SetEwayBillThresholdMinor(MoneyScale.ToMinor(MoneyScale.Round(threshold)));

        return await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EinvoiceSettingsSummary> SaveCredentialsAsync(
        SaveIrpCredentialsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        var settings = await GetEntityAsync(cancellationToken).ConfigureAwait(false);

        settings.SetProtectedCredentials(
            _protector.Protect(request.ClientId),
            _protector.Protect(request.ClientSecret),
            _protector.Protect(request.Username),
            _protector.Protect(request.Password));

        return await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EinvoiceSettingsSummary> ClearCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetEntityAsync(cancellationToken).ConfigureAwait(false);

        settings.ClearCredentials();

        return await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IrpCredentials> ResolveCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var settings = await GetEntityAsync(cancellationToken).ConfigureAwait(false);

        if (!settings.HasCredentials)
        {
            throw new EinvoiceNotSubmittableException(
                "No NIC credentials have been entered. Add the client id, client secret, username "
                + "and password on the E-invoice settings screen (open-questions.md #10).");
        }

        var company = await _unitOfWork.Repository<Company>()
            .GetByIdAsync(companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EinvoiceNotSubmittableException("This company no longer exists.");

        if (string.IsNullOrWhiteSpace(company.Gstin))
        {
            throw new EinvoiceNotSubmittableException(
                "The company has no GSTIN, so there is nothing to authenticate as.");
        }

        return new IrpCredentials(
            settings.Environment,
            _protector.Unprotect(settings.ProtectedClientId!),
            _protector.Unprotect(settings.ProtectedClientSecret!),
            _protector.Unprotect(settings.ProtectedUsername!),
            _protector.Unprotect(settings.ProtectedPassword!),
            company.Gstin);
    }

    private async Task<EinvoiceSettingsSummary> SaveAsync(
        EinvoiceSettings settings,
        CancellationToken cancellationToken)
    {
        _unitOfWork.Repository<EinvoiceSettings>().Update(settings);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToSummary(settings);
    }

    private static EinvoiceSettingsSummary ToSummary(EinvoiceSettings settings) =>
        new(settings.IsEinvoicingApplicable,
            settings.Environment,
            settings.EwayBillThresholdMinor,
            settings.HasCredentials);

    private sealed class SettingsOfCompany : Specification<EinvoiceSettings>
    {
        public SettingsOfCompany(Guid companyId) =>
            Where(settings => settings.CompanyId == companyId);
    }
}
