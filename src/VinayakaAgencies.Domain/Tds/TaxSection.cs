using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// One TDS or TCS section master row: the statutory head a deduction is made under
/// (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The section code is data and nothing else.</b> Checkpoint C7.5 forbids a section number
/// appearing as a constant, an enum member or a <c>switch</c> arm anywhere in <c>src/</c>, and this
/// row is what makes that possible: "194C" is a string typed by the owner, resolved at posting time,
/// and known to no code path by name. Adding a section is inserting a row (C7.2).
/// </para>
/// <para>
/// <b>Why <see cref="StatutePaymentCode"/> exists.</b> The Income Tax Act 2025 renumbers the
/// 194-series into Sections 392/393/394 with numeric payment codes from 1 April 2026. Both numbering
/// schemes must be usable at once across the cutover, because a payment made in March is filed under
/// the old number and one made in April under the new. So a section carries its own effective window
/// and its new-regime code side by side: the renumbering lands as a data update — deactivate the old
/// rows, insert the new ones — with no code change and no migration.
/// </para>
/// <para>
/// Uniqueness is soft, matching <c>HsnSac</c>: the <c>(CompanyId, Kind, Code)</c> index is unique
/// among <em>active</em> rows only, so a section retired by notification can be deactivated and a row
/// bearing the same code created later without fighting history.
/// </para>
/// </remarks>
public sealed class TaxSection : Entity, IAuditStamped
{
    private TaxSection()
    {
        // EF Core materialisation.
    }

    public TaxSection(
        Guid companyId,
        TaxSectionKind kind,
        string code,
        string name,
        string? statutePaymentCode = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedCode = code.Trim();

        if (trimmedCode.Length > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "A section code is at most 16 characters — long enough for the widest code the "
                + "statute uses, including a bracketed sub-clause.");
        }

        CompanyId = companyId;
        Kind = kind;
        Code = trimmedCode.ToUpperInvariant();
        Name = name.Trim();
        SetStatutePaymentCode(statutePaymentCode);
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Deducted at source, or collected at source.</summary>
    public TaxSectionKind Kind { get; private set; }

    /// <summary>The section as it is written and filed — "194C", "194Q", "206C(1H)".</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What the section covers, in the owner's words: "Payments to contractors".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The numeric payment code under the Income Tax Act 2025 renumbering, where one has been
    /// notified. Null until the owner enters it; never inferred from <see cref="Code"/>.
    /// </summary>
    public string? StatutePaymentCode { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void SetStatutePaymentCode(string? statutePaymentCode) =>
        StatutePaymentCode = string.IsNullOrWhiteSpace(statutePaymentCode)
            ? null
            : statutePaymentCode.Trim();

    /// <summary>
    /// Retires the section. Deactivation rather than deletion because posted deductions reference
    /// it, and a filed return has to stay reconstructible from the rows that produced it.
    /// </summary>
    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
