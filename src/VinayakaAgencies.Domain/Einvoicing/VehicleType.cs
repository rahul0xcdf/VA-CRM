namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Regular carriage or over-dimensional cargo (phase-6-einvoice-eway.md §4a).
/// </summary>
/// <remarks>
/// This changes how long an e-way bill lives, so it is not a label — see
/// <see cref="EwayBillValidity"/>.
/// </remarks>
public enum VehicleType
{
    Regular = 1,

    /// <summary>Over-dimensional cargo, which earns far less validity per day.</summary>
    OverDimensionalCargo = 2,
}
