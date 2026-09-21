namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// Whether a section governs tax <b>deducted</b> at source or tax <b>collected</b> at source
/// (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// One master covers both, discriminated by this, exactly as <c>HsnSac</c> covers HSN and SAC codes
/// together. The two are structurally identical — a code, a payee, an effective-dated rate and a
/// threshold — and the only thing that differs is the direction of the money: TDS is withheld from
/// what we pay, TCS is added to what we charge. Splitting them into two masters would duplicate the
/// whole rate-schedule and cumulative-threshold machinery to express that one fact.
/// </remarks>
public enum TaxSectionKind
{
    /// <summary>Tax deducted at source: withheld from a payment we make.</summary>
    Tds = 1,

    /// <summary>Tax collected at source: added to what we charge a buyer.</summary>
    Tcs = 2,
}
