using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Reads and writes the e-invoicing configuration, including the encrypted NIC credentials
/// (phase-6-einvoice-eway.md §4a).
/// </summary>
public interface IEinvoiceSettingsService
{
    /// <summary>The settings as the screen sees them — no credential value, masked or otherwise.</summary>
    Task<EinvoiceSettingsSummary> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The settings row itself, created on first use. Internal-facing: other services need
    /// <see cref="EinvoiceSettings.EwayBillThresholdMinor"/> and the applicability flag, and handing
    /// them a DTO would mean mapping back again to change anything.
    /// </summary>
    Task<EinvoiceSettings> GetEntityAsync(CancellationToken cancellationToken = default);

    Task<EinvoiceSettingsSummary> SetApplicabilityAsync(
        bool isApplicable,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches environment. Production is refused until credentials exist, per §2's requirement that
    /// production is only switched on after sandbox sign-off.
    /// </summary>
    Task<EinvoiceSettingsSummary> SetEnvironmentAsync(
        IrpEnvironment environment,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the e-way bill threshold, which is config rather than a constant (non-negotiable #6).</summary>
    Task<EinvoiceSettingsSummary> SetEwayBillThresholdAsync(
        decimal threshold,
        CancellationToken cancellationToken = default);

    /// <summary>Encrypts and stores the NIC credentials.</summary>
    Task<EinvoiceSettingsSummary> SaveCredentialsAsync(
        SaveIrpCredentialsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets the stored credentials and drops back to sandbox.</summary>
    Task<EinvoiceSettingsSummary> ClearCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts the credentials for one call.
    /// </summary>
    /// <remarks>
    /// Deliberately not cached. The returned record is the only plaintext copy and is expected to go
    /// out of scope with the call that used it; holding it in a field would put a readable secret in
    /// a long-lived object, which is what checkpoint C6.6 is looking for.
    /// </remarks>
    /// <exception cref="EinvoiceNotSubmittableException">No credentials have been supplied yet.</exception>
    Task<IrpCredentials> ResolveCredentialsAsync(CancellationToken cancellationToken = default);
}
