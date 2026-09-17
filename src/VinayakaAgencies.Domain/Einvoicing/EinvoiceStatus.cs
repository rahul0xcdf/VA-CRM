namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Where one invoice stands with the IRP (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <see cref="Pending"/> is the state that makes the phase's resilience requirement expressible: a
/// call that timed out leaves the row here — claimed, IRN computed, outcome unknown — rather than
/// nowhere. The retry path reads it, asks the portal what it knows about that IRN, and settles it.
/// A network outage therefore cannot leave an invoice in an ambiguous state, because "ambiguous" is
/// itself a recorded state.
/// </remarks>
public enum EinvoiceStatus
{
    /// <summary>Claimed locally, not yet confirmed by the portal. Retryable.</summary>
    Pending = 1,

    /// <summary>Registered: the portal returned an acknowledgement, a signed invoice and a signed QR.</summary>
    Generated = 2,

    /// <summary>Cancelled at the portal within the 24-hour window.</summary>
    Cancelled = 3,

    /// <summary>The portal rejected the document. Not retryable without changing the document.</summary>
    Rejected = 4,
}
