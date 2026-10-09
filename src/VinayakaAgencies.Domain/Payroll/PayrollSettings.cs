using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// Per-company payroll configuration: which statutes are in force
/// (phase-7-tds-payroll-banking.md §4a D3, checkpoint C7.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three flags, all off by default, and that is the whole design.</b> PF is mandatory above
/// twenty employees and ESI above ten; both numbers are statutory thresholds that change, and the
/// owner did not have a headcount when the phase was approved. §4a D3 resolves that by refusing to
/// depend on it: applicability is a flag the owner sets and the thresholds are data rows, so the
/// decision survives whatever the headcount turns out to be. Switching PF on when the business
/// crosses twenty is a settings change, not a migration against tables that by then hold real
/// payroll history.
/// </para>
/// <para>
/// <b>Off means inert, not zero.</b> Checkpoint C7.7 is that with every flag off no PF/ESI/PT row
/// is produced <em>and no statutory code path runs</em> — the computation does not read a rate
/// table and post a zero, it does not consult the statute at all. A zero row on a pay slip claims
/// the scheme applies and happened to cost nothing, which is a different and false statement.
/// </para>
/// <para>
/// The rates, ceilings and slabs themselves are deliberately not here. They are effective-dated
/// master data (<see cref="StatutoryRateSchedule"/>, <see cref="ProfessionalTaxSlab"/>), because a
/// settings row has no history and a notified figure needs one.
/// </para>
/// </remarks>
public sealed class PayrollSettings : Entity, IAuditStamped
{
    private PayrollSettings()
    {
        // EF Core materialisation.
    }

    public PayrollSettings(Guid companyId, string salaryPayableLedgerName)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        SetSalaryPayableLedgerName(salaryPayableLedgerName);
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The liability ledger net pay is credited to until it is paid out.</summary>
    public string SalaryPayableLedgerName { get; private set; } = string.Empty;

    /// <summary>True once the owner declares the business covered by provident fund.</summary>
    public bool IsProvidentFundEnabled { get; private set; }

    /// <summary>True once the owner declares the business covered by state insurance.</summary>
    public bool IsEmployeeStateInsuranceEnabled { get; private set; }

    /// <summary>True once the owner declares the business liable to deduct professional tax.</summary>
    public bool IsProfessionalTaxEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>
    /// Whether a statute is in force, asked the way the computation asks it.
    /// </summary>
    /// <remarks>
    /// One method rather than three call sites reading three properties: the payroll engine walks a
    /// list of statutory heads and asks this of each, so a fourth statute added later needs one arm
    /// here and no change at all in the engine.
    /// </remarks>
    public bool IsEnabled(StatuteKind statute) => statute switch
    {
        StatuteKind.ProvidentFund => IsProvidentFundEnabled,
        StatuteKind.EmployeeStateInsurance => IsEmployeeStateInsuranceEnabled,
        StatuteKind.ProfessionalTax => IsProfessionalTaxEnabled,
        _ => false,
    };

    /// <summary>True when no statute at all is in force — the C7.7 state.</summary>
    public bool HasNoStatutesInForce =>
        !IsProvidentFundEnabled && !IsEmployeeStateInsuranceEnabled && !IsProfessionalTaxEnabled;

    public void SetSalaryPayableLedgerName(string ledgerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerName);

        SalaryPayableLedgerName = ledgerName.Trim();
    }

    /// <summary>Switches one statute on or off.</summary>
    public void SetStatuteEnabled(StatuteKind statute, bool isEnabled)
    {
        switch (statute)
        {
            case StatuteKind.ProvidentFund:
                IsProvidentFundEnabled = isEnabled;
                break;

            case StatuteKind.EmployeeStateInsurance:
                IsEmployeeStateInsuranceEnabled = isEnabled;
                break;

            case StatuteKind.ProfessionalTax:
                IsProfessionalTaxEnabled = isEnabled;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(statute), statute, "Unknown statute.");
        }
    }
}
