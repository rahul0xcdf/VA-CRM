namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Which NIC environment the client talks to (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// The spec is explicit that production is switched on only after the owner signs off on sandbox, so
/// this is a deliberate two-state setting rather than a base-URL string somebody can paste anything
/// into. Defaulting to <see cref="Sandbox"/> means a fresh install cannot file a real invoice by
/// accident.
/// </remarks>
public enum IrpEnvironment
{
    Sandbox = 1,
    Production = 2,
}
