using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Gst;

/// <summary>
/// One HSN/SAC master row: the classification code a goods-or-service item bills under
/// (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// One master covers both HSN (goods) and SAC (services) codes — nothing downstream needs to know
/// which is which, and Table 12 of GSTR-1 reports them identically. Uniqueness is deliberately
/// soft: the <c>(CompanyId, Code)</c> index is unique among <em>active</em> rows only, so a code
/// retired by notification can be deactivated and re-created later without history fights.
/// Nothing about a code is hardcoded anywhere — rates hang off this master as data rows
/// (<see cref="GstRateSchedule"/>).
/// </remarks>
public sealed class HsnSac : Entity, IAuditStamped
{
    private HsnSac()
    {
        // EF Core materialisation.
    }

    public HsnSac(Guid companyId, string code, string? description = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var trimmed = code.Trim();

        if (trimmed.Length is < 3 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "An HSN/SAC code is between 3 and 8 characters.");
        }

        CompanyId = companyId;
        Code = trimmed.ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The code exactly as filed — normalised once here, never re-derived.</summary>
    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void SetDescription(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
