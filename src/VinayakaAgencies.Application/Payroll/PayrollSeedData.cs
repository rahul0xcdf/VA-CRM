namespace VinayakaAgencies.Application.Payroll;

/// <summary>
/// The payroll seed rows provisioning writes for one company
/// (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// <b>No rate, no ceiling, no slab, and no pay head.</b> §2 extends non-negotiable #6 to PF and ESI
/// contribution rates, their wage ceilings and Professional Tax slabs by name, so none of them is
/// seeded. Pay heads are not seeded either: what a company calls its allowances and how it computes
/// them is the owner's structure, and a seeded "HRA at 40% of basic" would be a figure nobody chose.
/// </para>
/// <para>
/// What is seeded is the ledgers a salary voucher has to post through, and they are ordinary chart
/// of accounts rows rather than statutory figures. Every statutory flag starts off, so on a company
/// that never turns one on this module is inert (checkpoint C7.7).
/// </para>
/// </remarks>
internal static class PayrollSeedData
{
    /// <summary>The expense head gross salary is debited to.</summary>
    public const string SalaryExpenseLedgerName = "Salaries";

    /// <summary>The liability net pay is credited to until it is paid out.</summary>
    public const string SalaryPayableLedgerName = "Salary Payable";

    /// <summary>The liability the provident fund contributions accumulate in.</summary>
    public const string ProvidentFundPayableLedgerName = "PF Payable";

    /// <summary>The liability the state insurance contributions accumulate in.</summary>
    public const string EmployeeStateInsurancePayableLedgerName = "ESI Payable";

    /// <summary>The liability professional tax withheld accumulates in.</summary>
    public const string ProfessionalTaxPayableLedgerName = "Professional Tax Payable";

    /// <summary>The expense head the employer's own contributions are charged to.</summary>
    public const string EmployerContributionExpenseLedgerName = "Employer Statutory Contributions";

    /// <summary>Where the salary and employer-contribution expense heads hang.</summary>
    public const string IndirectExpensesGroupName = "Indirect Expenses";

    /// <summary>Where the four payable ledgers hang.</summary>
    public const string CurrentLiabilitiesGroupName = "Current Liabilities";

    /// <summary>The expense ledgers, with the group each belongs under.</summary>
    public static IReadOnlyList<(string Name, string Group)> Ledgers { get; } =
    [
        (SalaryExpenseLedgerName, IndirectExpensesGroupName),
        (EmployerContributionExpenseLedgerName, IndirectExpensesGroupName),
        (SalaryPayableLedgerName, CurrentLiabilitiesGroupName),
        (ProvidentFundPayableLedgerName, CurrentLiabilitiesGroupName),
        (EmployeeStateInsurancePayableLedgerName, CurrentLiabilitiesGroupName),
        (ProfessionalTaxPayableLedgerName, CurrentLiabilitiesGroupName),
    ];
}
