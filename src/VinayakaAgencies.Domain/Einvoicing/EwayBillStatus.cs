namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Where one e-way bill stands (phase-6-einvoice-eway.md §2).
/// </summary>
public enum EwayBillStatus
{
    /// <summary>Part-A captured locally; the portal has not issued a number yet.</summary>
    Draft = 1,

    /// <summary>Issued by the portal and currently valid.</summary>
    Active = 2,

    /// <summary>Cancelled at the portal within its 24-hour window.</summary>
    Cancelled = 3,

    /// <summary>The portal rejected the request.</summary>
    Rejected = 4,
}
