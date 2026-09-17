namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// How the consignment moves, which decides what Part-B must carry
/// (phase-6-einvoice-eway.md §4a).
/// </summary>
/// <remarks>
/// The mode is not cosmetic: by road the vehicle number is required and the transport document
/// number is not, and for rail, air and ship it is the other way round. Enforcing that per mode is
/// why this is an enum rather than free text.
/// </remarks>
public enum TransportMode
{
    Road = 1,
    Rail = 2,
    Air = 3,
    Ship = 4,
}
