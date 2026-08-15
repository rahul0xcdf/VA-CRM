namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// When a numbering series restarts its counter.
/// </summary>
/// <remarks>
/// <see cref="FinancialYear"/> is the owner's decision (D2) and the Indian convention: invoice
/// numbering conventionally restarts on 1 April, and a number that carries no year context is
/// ambiguous once the books span several years.
/// </remarks>
public enum NumberResetPolicy
{
    Never = 1,
    FinancialYear = 2,
}
