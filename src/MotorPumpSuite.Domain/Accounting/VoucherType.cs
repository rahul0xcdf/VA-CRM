using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// A named voucher type, e.g. "Payment" or "Bank Payment", behaving as one <see cref="VoucherKind"/>.
/// </summary>
/// <remarks>
/// The type is a data row while the kind is an enum, so the owner can run several types over one
/// behaviour — "Cash Payment" and "Bank Payment" both post as <see cref="VoucherKind.Payment"/> but
/// carry separate numbering series. Posting rules key off the kind; everything user-visible keys off
/// the type.
/// </remarks>
public sealed class VoucherType : Entity, IAuditStamped
{
    private VoucherType()
    {
        // EF Core materialisation.
    }

    public VoucherType(Guid companyId, string name, VoucherKind kind, bool isSystem = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        Name = name.Trim();
        Kind = kind;
        IsSystem = isSystem;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public VoucherKind Kind { get; private set; }

    public bool IsSystem { get; private set; }

    public Guid? DefaultSeriesId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void SetDefaultSeries(Guid seriesId) => DefaultSeriesId = seriesId;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Deactivate()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException(
                $"'{Name}' is a system voucher type and cannot be deactivated.");
        }

        IsActive = false;
    }

    public void Activate() => IsActive = true;
}
