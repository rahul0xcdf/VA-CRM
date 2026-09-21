namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// Which statutory deduction a config row or a pay head belongs to
/// (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// An enum, and the only part of the payroll statutes that is one. The three <em>schemes</em> exist
/// and are named in primary legislation; their rates, wage ceilings and slabs are notified figures
/// that change, and §2's extension of non-negotiable #6 puts every one of those in a data row. So
/// "there is a thing called Provident Fund" is an enum member, and "the employee contributes 12% up
/// to a wage of ₹15,000" is three columns of a dated row.
/// </para>
/// <para>
/// Each member also names the flag that switches it on. With the flag off, the scheme's rows are
/// never read and its pay heads never compute — the property checkpoint C7.7 pins.
/// </para>
/// </remarks>
public enum StatuteKind
{
    /// <summary>Employees' Provident Fund — a rate on a capped wage, employee and employer shares.</summary>
    ProvidentFund = 1,

    /// <summary>Employees' State Insurance — a rate on a capped wage, employee and employer shares.</summary>
    EmployeeStateInsurance = 2,

    /// <summary>
    /// Professional Tax — a state levy charged in slabs rather than at a rate, which is why it is
    /// configured through slab rows instead of a contribution rate.
    /// </summary>
    ProfessionalTax = 3,
}
