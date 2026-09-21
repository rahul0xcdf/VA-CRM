using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.Application.Tds;

/// <summary>
/// Asks what would be deducted from a payment, without posting anything
/// (phase-7-tds-payroll-banking.md §2, checkpoints C7.1 and C7.3).
/// </summary>
/// <remarks>
/// <see cref="TaxSectionId"/> is the owner's choice on the voucher, not something the software
/// infers from the ledger or the narration. Guessing the section is guessing the rate, and a wrong
/// section on a filed return is a notice.
/// </remarks>
public sealed record ComputeTaxDeductionRequest(
    Guid LedgerId,
    Guid TaxSectionId,
    DateOnly PaymentDate,
    decimal BaseAmount);

/// <summary>
/// What the engine decided and why, in the form the entry screen can show the operator before they
/// commit.
/// </summary>
/// <remarks>
/// Carries the reason even when nothing was deducted. "Below threshold" and "no rate configured"
/// look identical on a voucher — a zero — and they mean opposite things: the first is correct
/// behaviour, the second is a gap in setup that will under-deduct for the rest of the year.
/// </remarks>
public sealed record TaxDeductionPreview(
    bool IsApplicable,
    Guid TaxSectionId,
    string SectionCode,
    TaxSectionKind Kind,
    PayeeCategory PayeeCategory,
    decimal BaseAmount,
    decimal TaxableBase,
    decimal DeductedAmount,
    long AppliedRatePercentMinor,
    decimal CumulativeBaseBefore,
    decimal CumulativeBaseAfter,
    bool IsCertificateApplied,
    string? CertificateNumber,
    bool IsThresholdCrossingPayment,
    bool PayeeHadPan,
    bool IsNoPanRate,
    NoDeductionReason Reason,
    string Explanation);

/// <summary>
/// Posts a purchase or expense voucher that deducts tax at source
/// (phase-7-tds-payroll-banking.md §2, checkpoint C7.1).
/// </summary>
/// <remarks>
/// <para>
/// The caller supplies the gross expense side; the service supplies the two credit lines. Modelled
/// this way rather than as "give me the whole voucher and I will check it" because the deduction is
/// arithmetic the operator should not be re-keying, and a voucher whose TDS line disagrees with the
/// configured rate is the defect C7.1 exists to rule out.
/// </para>
/// <para>
/// <see cref="ExpenseLines"/> are the debits — the expense or asset heads the bill hits.
/// <see cref="PartyLedgerId"/> is credited with the gross less the deduction, and the TDS payable
/// ledger with the deduction. The three always balance because the service computes the party line
/// rather than accepting one.
/// </para>
/// </remarks>
public sealed record PostDeductingVoucherRequest(
    Guid VoucherTypeId,
    DateOnly VoucherDate,
    Guid PartyLedgerId,
    Guid TaxSectionId,
    IReadOnlyList<DeductingVoucherLine> ExpenseLines,
    string? Narration = null,
    string? ReferenceNumber = null,
    DateOnly? ReferenceDate = null,
    string? ManualNumber = null);

/// <summary>One debit line of a deducting voucher: the head the bill is charged to.</summary>
public sealed record DeductingVoucherLine(
    Guid LedgerId,
    decimal Amount,
    string? Narration = null,
    Guid? CostCentreId = null);

/// <summary>The posted result: the voucher, and what it deducted.</summary>
public sealed record PostedDeductingVoucherSummary(
    Guid VoucherId,
    string Number,
    DateOnly VoucherDate,
    decimal GrossAmount,
    decimal DeductedAmount,
    decimal NetPayableToParty,
    TaxDeductionPreview Deduction);

/// <summary>One row of the deduction register / challan report.</summary>
public sealed record TaxDeductionRow(
    Guid Id,
    Guid VoucherId,
    DateOnly DeductionDate,
    string SectionCode,
    TaxSectionKind Kind,
    Guid LedgerId,
    string LedgerName,
    string? Pan,
    decimal BaseAmount,
    decimal TaxableBase,
    decimal DeductedAmount,
    long AppliedRatePercentMinor,
    bool IsCertificateApplied,
    string? CertificateNumber,
    bool PayeeHadPan);

/// <summary>
/// What has to be deposited for one section in one period — the figure the owner pays from
/// (§4a D3's "challan-ready output", applied to TDS).
/// </summary>
public sealed record TaxChallanLine(
    Guid TaxSectionId,
    string SectionCode,
    TaxSectionKind Kind,
    string? StatutePaymentCode,
    int DeducteeCount,
    decimal TotalBase,
    decimal TotalDeducted);
