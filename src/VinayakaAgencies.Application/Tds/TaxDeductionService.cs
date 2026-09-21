using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Application.Tds;

/// <summary>
/// Computes and posts tax deducted at source (phase-7-tds-payroll-banking.md §2, checkpoints C7.1,
/// C7.3, C7.4 and C7.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this file does and does not know.</b> It knows how to find the rate row that covers a
/// payment, how to add up what a payee has already received this year, and how to hand both to
/// <c>TdsMath</c>. It does not know a single section number, rate or threshold — every figure
/// arrives from a data row (C7.5). That separation is why the April 2026 renumbering is a data
/// update: nothing here would notice.
/// </para>
/// <para>
/// <b>The cumulative total is read, not remembered.</b> C7.3's annual threshold is "what this payee
/// has received under this section this financial year", and the answer is the sum of the
/// <c>TaxDeduction</c> rows already posted. Reading it per posting rather than caching a running
/// figure is what keeps a cancelled voucher from silently corrupting every later deduction.
/// </para>
/// <para>
/// Posting composes <c>IVoucherPostingService</c> exactly as <c>StockVoucherService</c> does. A
/// deduction reaches the books as journal lines through the Phase 1 engine or it does not reach
/// them at all.
/// </para>
/// </remarks>
public sealed class TaxDeductionService(
    IUnitOfWork unitOfWork,
    ICompanyContext companyContext,
    ITaxSectionService taxSections,
    IVoucherPostingService voucherPostingService,
    IFinancialYearProvider financialYears) : ITaxDeductionService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly ITaxSectionService _taxSections = taxSections
        ?? throw new ArgumentNullException(nameof(taxSections));

    private readonly IVoucherPostingService _voucherPostingService = voucherPostingService
        ?? throw new ArgumentNullException(nameof(voucherPostingService));

    private readonly IFinancialYearProvider _financialYears = financialYears
        ?? throw new ArgumentNullException(nameof(financialYears));

    /// <inheritdoc />
    public async Task<TaxDeductionPreview> ComputeAsync(
        ComputeTaxDeductionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);
        var financialYear = await _financialYears
            .GetOrCreateAsync(companyId, request.PaymentDate, cancellationToken)
            .ConfigureAwait(false);

        return await ComputeCoreAsync(companyId, financialYear.Id, request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PostedDeductingVoucherSummary> PostAsync(
        PostDeductingVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExpenseLines is null || request.ExpenseLines.Count == 0)
        {
            throw new InvalidOperationException("A deducting voucher needs at least one expense line.");
        }

        var gross = request.ExpenseLines.Sum(line => line.Amount);

        if (gross <= 0m)
        {
            throw new InvalidOperationException(
                "The gross amount of a deducting voucher must be positive.");
        }

        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var financialYear = await _financialYears
            .GetOrCreateAsync(companyId, request.VoucherDate, cancellationToken)
            .ConfigureAwait(false);

        var preview = await ComputeCoreAsync(
                companyId,
                financialYear.Id,
                new ComputeTaxDeductionRequest(
                    request.PartyLedgerId,
                    request.TaxSectionId,
                    request.VoucherDate,
                    gross),
                cancellationToken)
            .ConfigureAwait(false);

        var settings = await RequireSettingsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var taxLedger = preview.Kind == TaxSectionKind.Tcs
            ? settings.TcsPayableLedgerName
            : settings.TdsPayableLedgerName;

        var lines = new List<PostVoucherLine>(request.ExpenseLines.Count + 2);

        lines.AddRange(request.ExpenseLines.Select(line =>
            new PostVoucherLine(line.LedgerId, line.Amount, 0m, line.Narration, line.CostCentreId)));

        // The party is credited net of the deduction, and the deduction is credited to the
        // liability head. Computed here rather than accepted from the caller: a voucher whose
        // party line disagrees with the rate table is exactly what C7.1 rules out.
        lines.Add(new PostVoucherLine(
            request.PartyLedgerId,
            0m,
            gross - preview.DeductedAmount,
            Narration: null));

        if (preview.DeductedAmount > 0m)
        {
            var taxLedgerId = await RequireLedgerIdByNameAsync(companyId, taxLedger, cancellationToken)
                .ConfigureAwait(false);

            lines.Add(new PostVoucherLine(
                taxLedgerId,
                0m,
                preview.DeductedAmount,
                Narration: $"{preview.SectionCode} @ {FormatRate(preview.AppliedRatePercentMinor)}"));
        }

        var posted = await _voucherPostingService.PostAsync(
                new PostVoucherRequest(
                    request.VoucherTypeId,
                    request.VoucherDate,
                    lines,
                    request.Narration,
                    request.ReferenceNumber,
                    request.ReferenceDate,
                    request.ManualNumber),
                cancellationToken)
            .ConfigureAwait(false);

        // The snapshot row is written whenever a section was resolved at all — including when the
        // outcome was nil. A nil deduction under a certificate has to appear in the return, and
        // "below threshold this time" is what the next payment's cumulative figure is built from.
        if (preview.IsApplicable || preview.Reason != NoDeductionReason.NoScheduleInForce)
        {
            await _unitOfWork.Repository<TaxDeduction>().AddAsync(
                    BuildSnapshot(companyId, posted.VoucherId, financialYear.Id, request, preview),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new PostedDeductingVoucherSummary(
            posted.VoucherId,
            posted.Number,
            posted.VoucherDate,
            gross,
            preview.DeductedAmount,
            gross - preview.DeductedAmount,
            preview);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxDeductionRow>> ListAsync(
        DateOnly fromDate,
        DateOnly toDate,
        Guid? taxSectionId = null,
        Guid? ledgerId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var deductions = await _unitOfWork.Repository<TaxDeduction>()
            .ListAsync(
                new DeductionsInPeriod(companyId, fromDate, toDate, taxSectionId, ledgerId),
                cancellationToken)
            .ConfigureAwait(false);

        var names = await LedgerNameLookupAsync(companyId, cancellationToken).ConfigureAwait(false);
        var pans = await PanLookupAsync(companyId, cancellationToken).ConfigureAwait(false);

        return deductions
            .OrderByDescending(deduction => deduction.DeductionDate)
            .ThenBy(deduction => deduction.SectionCode, StringComparer.Ordinal)
            .Select(deduction => new TaxDeductionRow(
                deduction.Id,
                deduction.VoucherId,
                deduction.DeductionDate,
                deduction.SectionCode,
                deduction.Kind,
                deduction.LedgerId,
                names.TryGetValue(deduction.LedgerId, out var name) ? name : "(unknown)",
                pans.TryGetValue(deduction.LedgerId, out var pan) ? pan : null,
                MoneyScale.FromMinor(deduction.BaseAmountMinor),
                MoneyScale.FromMinor(deduction.TaxableBaseMinor),
                MoneyScale.FromMinor(deduction.DeductedAmountMinor),
                deduction.AppliedRatePercentMinor,
                deduction.IsCertificateApplied,
                deduction.CertificateNumber,
                deduction.PayeeHadPan))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxChallanLine>> ChallanSummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var deductions = await _unitOfWork.Repository<TaxDeduction>()
            .ListAsync(
                new DeductionsInPeriod(companyId, fromDate, toDate, null, null),
                cancellationToken)
            .ConfigureAwait(false);

        var sections = (await _unitOfWork.Repository<TaxSection>()
                .ListAsync(new SectionsOfCompany(companyId), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(section => section.Id);

        return deductions
            .GroupBy(deduction => deduction.TaxSectionId)
            .Select(group =>
            {
                var first = group.First();
                sections.TryGetValue(group.Key, out var section);

                return new TaxChallanLine(
                    group.Key,
                    first.SectionCode,
                    first.Kind,
                    section?.StatutePaymentCode,

                    // Deductees, not deductions: a challan reports how many parties were deducted
                    // from, and one contractor billing weekly is one deductee.
                    group.Select(deduction => deduction.LedgerId).Distinct().Count(),
                    MoneyScale.FromMinor(group.Sum(deduction => deduction.TaxableBaseMinor)),
                    MoneyScale.FromMinor(group.Sum(deduction => deduction.DeductedAmountMinor)));
            })
            .Where(line => line.TotalDeducted != 0m || line.TotalBase != 0m)
            .OrderBy(line => line.Kind)
            .ThenBy(line => line.SectionCode, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveSaleOfGoodsCollectionSectionAsync(
        Guid customerLedgerId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var settings = await _unitOfWork.Repository<TdsSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false);

        // Flag off, or on without a section: nothing collects. The second case cannot normally
        // arise — TdsSettings refuses it — but reading it as "no collection" rather than throwing
        // keeps a hand-edited row from blocking every sale in the company.
        if (settings is null
            || !settings.IsSaleOfGoodsCollectionEnabled
            || settings.SaleOfGoodsSectionId is not { } sectionId)
        {
            return null;
        }

        var profile = await _unitOfWork.Repository<TaxPayeeProfile>()
            .SingleOrDefaultAsync(new ProfileOfLedger(companyId, customerLedgerId), cancellationToken)
            .ConfigureAwait(false);

        // Checkpoint C7.6. Where the buyer has declared they deduct on their purchase, the seller
        // does not also collect on the same transaction — the two are mutually exclusive by
        // statute, and collecting anyway double-taxes the line.
        return profile?.DeclaresDeductingOnPurchases == true ? null : sectionId;
    }

    /// <summary>
    /// The computation itself: resolve the rate, read the year so far, find any certificate, and
    /// hand all of it to <c>TdsMath</c>.
    /// </summary>
    private async Task<TaxDeductionPreview> ComputeCoreAsync(
        Guid companyId,
        Guid financialYearId,
        ComputeTaxDeductionRequest request,
        CancellationToken cancellationToken)
    {
        var baseMinor = MoneyScale.ToMinor(request.BaseAmount);

        var section = await _unitOfWork.Repository<TaxSection>()
            .GetByIdAsync(request.TaxSectionId, cancellationToken)
            .ConfigureAwait(false);

        if (section is null || section.CompanyId != companyId)
        {
            throw new InvalidOperationException("That section does not exist in this company's books.");
        }

        var profile = await _unitOfWork.Repository<TaxPayeeProfile>()
            .SingleOrDefaultAsync(new ProfileOfLedger(companyId, request.LedgerId), cancellationToken)
            .ConfigureAwait(false);

        if (profile is { IsDeductionApplicable: false })
        {
            return NotDeducted(
                section,
                request,
                baseMinor,
                NoDeductionReason.PayeeNotApplicable,
                profile.PayeeCategory,
                payeeHadPan: profile.HasPan,
                "This party is marked outside deduction on its payee profile.");
        }

        var rate = await _taxSections
            .ResolveRateAsync(request.LedgerId, request.TaxSectionId, request.PaymentDate, cancellationToken)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return NotDeducted(
                section,
                request,
                baseMinor,
                NoDeductionReason.NoScheduleInForce,
                profile?.PayeeCategory ?? PayeeCategory.Any,
                payeeHadPan: profile?.HasPan ?? false,
                $"No rate is configured for section '{section.Code}' on "
                + $"{request.PaymentDate:dd-MMM-yyyy}. Nothing was deducted — add a dated rate row.");
        }

        var (cumulativeBefore, taxedBefore, underCertificateBefore) = await ReadYearToDateAsync(
                companyId,
                request.LedgerId,
                request.TaxSectionId,
                financialYearId,
                cancellationToken)
            .ConfigureAwait(false);

        var certificate = await ResolveCertificateAsync(
                companyId,
                request.LedgerId,
                request.TaxSectionId,
                request.PaymentDate,
                cancellationToken)
            .ConfigureAwait(false);

        long? certificateRate = certificate?.RatePercentMinor;
        long? certificateRoom = certificate?.CeilingAmountMinor is { } ceiling
            ? Math.Max(0L, ceiling - underCertificateBefore)
            : null;

        var outcome = TdsMath.Compute(
            baseMinor,
            cumulativeBefore,
            taxedBefore,
            rate.RatePercentMinor,
            rate.SingleTransactionThresholdMinor,
            rate.AnnualThresholdMinor,
            certificateRate,
            certificateRoom);

        return new TaxDeductionPreview(
            IsApplicable: outcome.HasDeduction,
            section.Id,
            section.Code,
            section.Kind,
            rate.PayeeCategory,
            request.BaseAmount,
            MoneyScale.FromMinor(outcome.TaxableBaseMinor),
            MoneyScale.FromMinor(outcome.DeductedAmountMinor),
            outcome.EffectiveRatePercentMinor,
            MoneyScale.FromMinor(cumulativeBefore),
            MoneyScale.FromMinor(outcome.CumulativeBaseAfterMinor),
            outcome.IsCertificateApplied,
            outcome.IsCertificateApplied ? certificate?.CertificateNumber : null,
            outcome.IsThresholdCrossingPayment,
            rate.PayeeHadPan,
            rate.IsNoPanRate,
            outcome.Reason,
            Explain(section, rate, outcome, certificate));
    }

    /// <summary>
    /// What this payee has received under this section this year, and how much of it was taxed.
    /// </summary>
    /// <remarks>
    /// Three figures, because the arithmetic needs all three: the aggregate decides whether the
    /// annual threshold has been crossed, the taxed portion decides how much the crossing payment
    /// has to catch up on, and the certificate portion decides how much room a capped certificate
    /// has left.
    /// </remarks>
    private async Task<(long Cumulative, long Taxed, long UnderCertificate)> ReadYearToDateAsync(
        Guid companyId,
        Guid ledgerId,
        Guid taxSectionId,
        Guid financialYearId,
        CancellationToken cancellationToken)
    {
        var priors = await _unitOfWork.Repository<TaxDeduction>()
            .ListAsync(
                new DeductionsOfPayeeInYear(companyId, ledgerId, taxSectionId, financialYearId),
                cancellationToken)
            .ConfigureAwait(false);

        return (
            priors.Sum(deduction => deduction.BaseAmountMinor),
            priors.Sum(deduction => deduction.TaxableBaseMinor),
            priors.Where(deduction => deduction.IsCertificateApplied)
                .Sum(deduction => deduction.TaxableBaseMinor));
    }

    private async Task<LowerDeductionCertificate?> ResolveCertificateAsync(
        Guid companyId,
        Guid ledgerId,
        Guid taxSectionId,
        DateOnly onDate,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<LowerDeductionCertificate>()
            .ListAsync(
                new CertificatesOfLedgerAndSection(companyId, ledgerId, taxSectionId),
                cancellationToken)
            .ConfigureAwait(false))
        .FirstOrDefault(certificate => certificate.Covers(onDate));

    private static TaxDeduction BuildSnapshot(
        Guid companyId,
        Guid voucherId,
        Guid financialYearId,
        PostDeductingVoucherRequest request,
        TaxDeductionPreview preview) =>
        new(companyId,
            voucherId,
            request.PartyLedgerId,
            preview.TaxSectionId,
            financialYearId,
            preview.Kind,
            preview.SectionCode,
            preview.PayeeCategory,
            request.VoucherDate,
            MoneyScale.ToMinor(preview.BaseAmount),
            new TaxDeductionOutcome(
                MoneyScale.ToMinor(preview.TaxableBase),
                MoneyScale.ToMinor(preview.DeductedAmount),
                preview.AppliedRatePercentMinor,
                MoneyScale.ToMinor(preview.CumulativeBaseAfter),
                preview.IsCertificateApplied,
                preview.IsThresholdCrossingPayment,
                preview.Reason),
            preview.PayeeHadPan,
            preview.CertificateNumber);

    private async Task<TdsSettings> RequireSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<TdsSettings>()
            .SingleOrDefaultAsync(new SettingsOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException(
            "TDS settings have not been configured for this company. Set the TDS and TCS payable "
            + "ledgers before posting a deducting voucher.");

    private async Task<Guid> RequireLedgerIdByNameAsync(
        Guid companyId,
        string ledgerName,
        CancellationToken cancellationToken)
    {
        var normalised = NameNormaliser.Normalise(ledgerName);

        var ledger = await _unitOfWork.Repository<Ledger>()
            .SingleOrDefaultAsync(new LedgerByName(companyId, normalised), cancellationToken)
            .ConfigureAwait(false);

        return ledger?.Id
            ?? throw new InvalidOperationException(
                $"The TDS settings name '{ledgerName}' as the liability ledger, but no such ledger "
                + "exists. Create it, or point the setting at one that does.");
    }

    private async Task<Dictionary<Guid, string>> LedgerNameLookupAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<Ledger>()
            .ListAsync(new LedgersOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false))
        .ToDictionary(ledger => ledger.Id, ledger => ledger.Name);

    private async Task<Dictionary<Guid, string?>> PanLookupAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<TaxPayeeProfile>()
            .ListAsync(new ProfilesOfCompany(companyId), cancellationToken)
            .ConfigureAwait(false))
        .ToDictionary(profile => profile.LedgerId, profile => profile.Pan);

    private static TaxDeductionPreview NotDeducted(
        TaxSection section,
        ComputeTaxDeductionRequest request,
        long baseMinor,
        NoDeductionReason reason,
        PayeeCategory payeeCategory,
        bool payeeHadPan,
        string explanation) =>
        new(IsApplicable: false,
            section.Id,
            section.Code,
            section.Kind,
            payeeCategory,
            request.BaseAmount,
            TaxableBase: 0m,
            DeductedAmount: 0m,
            AppliedRatePercentMinor: 0L,
            CumulativeBaseBefore: 0m,
            CumulativeBaseAfter: MoneyScale.FromMinor(baseMinor),
            IsCertificateApplied: false,
            CertificateNumber: null,
            IsThresholdCrossingPayment: false,
            payeeHadPan,
            IsNoPanRate: false,
            reason,
            explanation);

    /// <summary>
    /// A sentence the operator can act on, assembled from what actually decided the outcome.
    /// </summary>
    /// <remarks>
    /// Worth the code. The difference between "nothing was deducted because the year is still under
    /// the limit" and "nothing was deducted because nobody set up a rate" is invisible on the
    /// voucher — both are a zero — and the second one compounds silently until somebody notices in
    /// March.
    /// </remarks>
    private static string Explain(
        TaxSection section,
        ResolvedTaxRate rate,
        TaxDeductionOutcome outcome,
        LowerDeductionCertificate? certificate) => outcome.Reason switch
        {
            NoDeductionReason.BelowThreshold =>
                $"Below the section '{section.Code}' threshold so far this year — nothing deducted.",

            NoDeductionReason.NilCertificate =>
                $"Certificate {certificate?.CertificateNumber} authorises nil deduction under "
                + $"section '{section.Code}'. Recorded at zero.",

            NoDeductionReason.PayeeNotApplicable =>
                "This party is marked outside deduction on its payee profile.",

            NoDeductionReason.NoScheduleInForce =>
                $"No rate covers section '{section.Code}' on this date.",

            NoDeductionReason.BuyerDeductsInstead =>
                "The buyer has declared they deduct on this purchase, so tax is not collected "
                + "on it as well.",

            _ => BuildDeductedExplanation(section, rate, outcome, certificate),
        };

    private static string BuildDeductedExplanation(
        TaxSection section,
        ResolvedTaxRate rate,
        TaxDeductionOutcome outcome,
        LowerDeductionCertificate? certificate)
    {
        var parts = new List<string>
        {
            $"Section '{section.Code}' at {FormatRate(outcome.EffectiveRatePercentMinor)}",
        };

        if (rate.IsNoPanRate)
        {
            parts.Add("higher rate — no PAN on file");
        }

        if (outcome.IsCertificateApplied && certificate is not null)
        {
            parts.Add($"certificate {certificate.CertificateNumber} applied");
        }

        if (outcome.IsThresholdCrossingPayment)
        {
            parts.Add("this payment crossed the annual limit, so earlier untaxed payments are "
                + "caught up here");
        }

        return string.Join("; ", parts) + ".";
    }

    /// <summary>Renders a 1/10,000-of-a-percent rate as a percentage for a human.</summary>
    private static string FormatRate(long ratePercentMinor) =>
        $"{ratePercentMinor / 10_000m:0.####}%";

    private sealed class SettingsOfCompany : Specification<TdsSettings>
    {
        public SettingsOfCompany(Guid companyId) => Where(settings => settings.CompanyId == companyId);
    }

    private sealed class ProfileOfLedger : Specification<TaxPayeeProfile>
    {
        public ProfileOfLedger(Guid companyId, Guid ledgerId)
        {
            Where(profile => profile.CompanyId == companyId && profile.LedgerId == ledgerId);
            ApplyNoTracking();
        }
    }

    private sealed class ProfilesOfCompany : Specification<TaxPayeeProfile>
    {
        public ProfilesOfCompany(Guid companyId)
        {
            Where(profile => profile.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class SectionsOfCompany : Specification<TaxSection>
    {
        public SectionsOfCompany(Guid companyId)
        {
            Where(section => section.CompanyId == companyId);
            ApplyNoTracking();
        }
    }

    private sealed class CertificatesOfLedgerAndSection : Specification<LowerDeductionCertificate>
    {
        public CertificatesOfLedgerAndSection(Guid companyId, Guid ledgerId, Guid taxSectionId)
        {
            Where(certificate => certificate.CompanyId == companyId
                && certificate.LedgerId == ledgerId
                && certificate.TaxSectionId == taxSectionId);
            ApplyNoTracking();
        }
    }

    private sealed class DeductionsOfPayeeInYear : Specification<TaxDeduction>
    {
        public DeductionsOfPayeeInYear(
            Guid companyId,
            Guid ledgerId,
            Guid taxSectionId,
            Guid financialYearId)
        {
            Where(deduction => deduction.CompanyId == companyId
                && deduction.LedgerId == ledgerId
                && deduction.TaxSectionId == taxSectionId
                && deduction.FinancialYearId == financialYearId);
            ApplyNoTracking();
        }
    }

    private sealed class DeductionsInPeriod : Specification<TaxDeduction>
    {
        public DeductionsInPeriod(
            Guid companyId,
            DateOnly fromDate,
            DateOnly toDate,
            Guid? taxSectionId,
            Guid? ledgerId)
        {
            Where(deduction => deduction.CompanyId == companyId
                && deduction.DeductionDate >= fromDate
                && deduction.DeductionDate <= toDate
                && (!taxSectionId.HasValue || deduction.TaxSectionId == taxSectionId.Value)
                && (!ledgerId.HasValue || deduction.LedgerId == ledgerId.Value));
            ApplyNoTracking();
        }
    }

    private sealed class LedgerByName : Specification<Ledger>
    {
        public LedgerByName(Guid companyId, string nameNormalised)
        {
            Where(ledger => ledger.CompanyId == companyId && ledger.NameNormalised == nameNormalised);
            ApplyNoTracking();
        }
    }

    private sealed class LedgersOfCompany : Specification<Ledger>
    {
        public LedgersOfCompany(Guid companyId)
        {
            Where(ledger => ledger.CompanyId == companyId);
            ApplyNoTracking();
        }
    }
}
