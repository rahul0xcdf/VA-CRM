using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// A numbering series for a voucher type: prefix, width, reset policy and override policy.
/// </summary>
/// <remarks>
/// The owner's decision (D2): the counter resets each financial year, gaps are blocked — a
/// cancelled voucher keeps its number rather than freeing it for reuse — and a manual override is
/// permitted and edit-logged. Reuse is refused deliberately: two documents sharing an invoice
/// number is a GST problem, whereas a visible gap is merely a question to answer.
/// </remarks>
public sealed class VoucherNumberSeries : Entity, IAuditStamped
{
    public const int MaxWidth = 12;

    private VoucherNumberSeries()
    {
        // EF Core materialisation.
    }

    public VoucherNumberSeries(
        Guid companyId,
        Guid voucherTypeId,
        string name,
        string prefix,
        int width = 4,
        string? suffix = null,
        long startNumber = 1,
        NumberResetPolicy resetPolicy = NumberResetPolicy.FinancialYear,
        bool allowManualOverride = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (voucherTypeId == Guid.Empty)
        {
            throw new ArgumentException("A series belongs to a voucher type.", nameof(voucherTypeId));
        }

        if (width is < 1 or > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, $"Width must be 1–{MaxWidth}.");
        }

        if (startNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startNumber), startNumber, "Must be at least 1.");
        }

        CompanyId = companyId;
        VoucherTypeId = voucherTypeId;
        SetName(name);
        Prefix = prefix?.Trim() ?? string.Empty;
        Suffix = string.IsNullOrWhiteSpace(suffix) ? null : suffix.Trim();
        Width = width;
        StartNumber = startNumber;
        ResetPolicy = resetPolicy;
        AllowManualOverride = allowManualOverride;
    }

    public Guid CompanyId { get; private set; }

    public Guid VoucherTypeId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="Name"/>, which the unique name index is built over so that two
    /// series cannot differ only by letter case on one provider and collide on the other. See
    /// <see cref="NameNormaliser"/>.
    /// </summary>
    /// <remarks>
    /// There is no rename path on a series, so this is written once, in the constructor.
    /// </remarks>
    public string NameNormalised { get; private set; } = string.Empty;

    public string Prefix { get; private set; } = string.Empty;

    public string? Suffix { get; private set; }

    /// <summary>Zero-padded width of the numeric part.</summary>
    public int Width { get; private set; }

    public long StartNumber { get; private set; }

    public NumberResetPolicy ResetPolicy { get; private set; }

    public bool AllowManualOverride { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Renders a sequence number as the document number, e.g. <c>PMT/0007</c>.</summary>
    public string Format(long sequenceNumber)
    {
        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber), sequenceNumber, "Must be at least 1.");
        }

        var digits = sequenceNumber.ToString(
            System.Globalization.CultureInfo.InvariantCulture).PadLeft(Width, '0');

        return $"{Prefix}{digits}{Suffix}";
    }

    public void Reconfigure(string prefix, int width, string? suffix, bool allowManualOverride)
    {
        if (width is < 1 or > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, $"Width must be 1–{MaxWidth}.");
        }

        Prefix = prefix?.Trim() ?? string.Empty;
        Width = width;
        Suffix = string.IsNullOrWhiteSpace(suffix) ? null : suffix.Trim();
        AllowManualOverride = allowManualOverride;
    }

    /// <summary>Keeps <see cref="NameNormalised"/> in step with <see cref="Name"/>; the two are only
    /// ever written together.</summary>
    private void SetName(string name)
    {
        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }
}
