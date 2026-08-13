using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Entities;

/// <summary>
/// A company whose books this install maintains.
/// </summary>
/// <remarks>
/// The schema is multi-company-ready from the start (project.md §4.1) even though only one company
/// exists at launch: every master and voucher added from Phase 1 onward carries a
/// <c>CompanyId</c>. Retrofitting that later would touch every table.
/// </remarks>
public sealed class Company : Entity, IAuditStamped
{
    private Company()
    {
        // EF Core materialisation.
    }

    public Company(
        string name,
        string legalName,
        string stateCode,
        string? gstin = null,
        int financialYearStartMonth = 4,
        string baseCurrencyCode = "INR")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrencyCode);

        if (financialYearStartMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(financialYearStartMonth),
                financialYearStartMonth,
                "Financial year start month must be between 1 and 12.");
        }

        Name = name.Trim();
        LegalName = legalName.Trim();
        StateCode = stateCode.Trim();
        Gstin = string.IsNullOrWhiteSpace(gstin) ? null : gstin.Trim().ToUpperInvariant();
        FinancialYearStartMonth = financialYearStartMonth;
        BaseCurrencyCode = baseCurrencyCode.Trim().ToUpperInvariant();
    }

    /// <summary>Trading name, shown in the UI.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Registered legal name, printed on the tax invoice (GST Rule 46).</summary>
    public string LegalName { get; private set; } = string.Empty;

    /// <summary>
    /// GSTIN of this registration. Nullable because a company may be created before registration
    /// details are entered; it becomes mandatory before any GST document is raised (Phase 5).
    /// </summary>
    public string? Gstin { get; private set; }

    /// <summary>GST state code — drives the CGST/SGST vs IGST split in Phase 5.</summary>
    public string StateCode { get; private set; } = string.Empty;

    /// <summary>Month the financial year begins. 4 (April) in India.</summary>
    public int FinancialYearStartMonth { get; private set; } = 4;

    public string BaseCurrencyCode { get; private set; } = "INR";

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void Rename(string name, string legalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);

        Name = name.Trim();
        LegalName = legalName.Trim();
    }

    public void SetGstin(string? gstin) =>
        Gstin = string.IsNullOrWhiteSpace(gstin) ? null : gstin.Trim().ToUpperInvariant();

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
