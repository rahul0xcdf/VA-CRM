using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Gst;

/// <summary>
/// Per-company GST configuration row: where the per-invoice round-off difference posts
/// (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// GST convention rounds each invoice total to the nearest rupee, and the difference must go
/// somewhere visible — never silently dropped. Which ledger absorbs it is the owner's choice, so
/// it lives here as data (setting key <c>Gst.RoundOffLedger</c>), seeded with the chart of
/// accounts' own "Round Off" row. Tax rates and HSN codes deliberately do not live here: they are
/// master data with effective dates, not settings.
/// </remarks>
public sealed class GstSettings : Entity, IAuditStamped
{
    /// <summary>The seeded default round-off ledger name (CompanySeedData L7).</summary>
    public const string DefaultRoundOffLedgerName = "Round Off";

    private GstSettings()
    {
        // EF Core materialisation.
    }

    public GstSettings(Guid companyId, string? roundOffLedgerName = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        SetRoundOffLedgerName(roundOffLedgerName);
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The ledger the per-invoice rounding difference is posted to.</summary>
    public string RoundOffLedgerName { get; private set; } = DefaultRoundOffLedgerName;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void SetRoundOffLedgerName(string? roundOffLedgerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roundOffLedgerName);

        RoundOffLedgerName = roundOffLedgerName.Trim();
    }
}
