namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// How a pay head's amount is arrived at (phase-7-tds-payroll-banking.md §4a D3 — "pay heads as
/// configuration, each with a computation basis, the same way a price-list line carries its basis
/// in Phase 4").
/// </summary>
/// <remarks>
/// The deliberate parallel is Phase 4's pricing bases. A price list line says "fixed price" or
/// "margin on cost" and the engine applies it; a pay head says "flat amount" or "percent of basic"
/// and the same idea applies. Modelling the basis rather than hardcoding "HRA is 40% of basic"
/// means a company that pays HRA differently changes a row, not a build.
/// </remarks>
public enum PayComputationBasis
{
    /// <summary>A stated rupee amount for this employee — basic pay, a fixed conveyance allowance.</summary>
    FlatAmount = 1,

    /// <summary>
    /// A percentage of the employee's basic pay, which is how HRA and DA are almost always stated.
    /// </summary>
    PercentOfBasic = 2,

    /// <summary>A percentage of gross earnings — used by some deductions and by ESI's wage base.</summary>
    PercentOfGross = 3,

    /// <summary>
    /// Produced by the statutory engine rather than from the structure: PF, ESI and Professional
    /// Tax, whose rates, ceilings and slabs are effective-dated config rows of their own.
    /// </summary>
    /// <remarks>
    /// A head on this basis carries no amount and no percentage on the salary structure, because
    /// the figure is not the company's to choose. It is also the basis that goes silent when the
    /// matching statutory flag is off, which is what checkpoint C7.7 turns on.
    /// </remarks>
    Statutory = 4,
}
