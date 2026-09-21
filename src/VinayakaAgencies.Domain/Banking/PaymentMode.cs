namespace VinayakaAgencies.Domain.Banking;

/// <summary>
/// How money physically moved on a payment or receipt voucher
/// (phase-7-tds-payroll-banking.md §2, §4a D4).
/// </summary>
/// <remarks>
/// <para>
/// An enum and not a data row, unlike almost everything else this phase adds, because these are not
/// rates or thresholds that a notification can change — they are the settlement rails that exist,
/// and each one means something structurally different to the software. <see cref="Cheque"/> is the
/// only mode that prints; <see cref="Neft"/>, <see cref="Rtgs"/>, <see cref="Imps"/> and
/// <see cref="Upi"/> carry a reference the bank issued and we record rather than generate;
/// <see cref="Cash"/> carries neither. Non-negotiable #6 governs tax rates and statutory figures —
/// nothing here is one.
/// </para>
/// <para>
/// Stored by name, so a mode added later (a rail that does not exist today) cannot silently change
/// what an existing row means.
/// </para>
/// </remarks>
public enum PaymentMode
{
    /// <summary>Notes and coins. No instrument number, no bank reference.</summary>
    Cash = 1,

    /// <summary>A cheque we wrote or received — the one mode that has a printed layout.</summary>
    Cheque = 2,

    /// <summary>A demand draft or pay order.</summary>
    DemandDraft = 3,

    /// <summary>National Electronic Funds Transfer; settles in batches, carries a UTR.</summary>
    Neft = 4,

    /// <summary>Real Time Gross Settlement; same-day and high value, carries a UTR.</summary>
    Rtgs = 5,

    /// <summary>Immediate Payment Service; round-the-clock, carries a reference number.</summary>
    Imps = 6,

    /// <summary>Unified Payments Interface; carries a transaction id rather than a UTR.</summary>
    Upi = 7,

    /// <summary>A card settlement.</summary>
    Card = 8,

    /// <summary>A transfer between two of our own accounts, or any rail not listed above.</summary>
    BankTransfer = 9,
}
