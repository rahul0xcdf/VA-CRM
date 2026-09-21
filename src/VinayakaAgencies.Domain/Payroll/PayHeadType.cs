namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// What a pay head does to a pay slip (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// Three, not two, because an employer contribution is neither an earning nor a deduction and
/// treating it as either gets the accounting wrong. The employer's share of PF never reaches the
/// employee — it is not part of gross pay and it is not withheld from it — but it is a real expense
/// of the company and a real liability to the fund. Folding it into
/// <see cref="Deduction"/> would understate net pay by the employer's share; folding it into
/// <see cref="Earning"/> would overstate gross.
/// </para>
/// <para>
/// Stored by name, so adding a fourth later cannot change what an existing row means.
/// </para>
/// </remarks>
public enum PayHeadType
{
    /// <summary>Adds to gross pay — basic, HRA, conveyance, a bonus.</summary>
    Earning = 1,

    /// <summary>Withheld from the employee — PF employee share, ESI employee share, PT, advances.</summary>
    Deduction = 2,

    /// <summary>
    /// Paid by the company on top of gross — the employer's PF and ESI shares. An expense and a
    /// liability; never part of gross pay and never withheld from it.
    /// </summary>
    EmployerContribution = 3,
}
