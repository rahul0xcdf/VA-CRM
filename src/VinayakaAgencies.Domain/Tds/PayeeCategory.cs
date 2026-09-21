namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// The class of person a rate row applies to (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>structural</b> distinction, not a rate, and that is why it is an enum while every
/// rate and threshold beside it is a data row. Section 194C charges 1% to an individual or HUF and
/// 2% to everyone else; which bucket a payee falls into is a fact about the payee that the statute
/// has drawn the same way for decades, whereas the two percentages change by notification.
/// </para>
/// <para>
/// <see cref="Any"/> exists because most sections do not split at all — 194Q and 206C(1H) charge one
/// rate to everybody. A schedule row carrying <see cref="Any"/> matches every payee, so a section
/// without a split needs one row rather than one row per category.
/// </para>
/// </remarks>
public enum PayeeCategory
{
    /// <summary>Matches every payee. Used by sections that do not split by class of person.</summary>
    Any = 0,

    /// <summary>An individual or a Hindu Undivided Family.</summary>
    IndividualOrHuf = 1,

    /// <summary>Any other person — company, firm, LLP, AOP, BOI, local authority.</summary>
    Other = 2,
}
