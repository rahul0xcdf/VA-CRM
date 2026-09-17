using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// Per-company e-invoicing and e-way bill configuration, including the encrypted NIC credentials
/// (phase-6-einvoice-eway.md §2, §4a).
/// </summary>
/// <remarks>
/// <para>
/// Three things live here that could each have been a constant somewhere, and each is data for a
/// reason. <see cref="IsEinvoicingApplicable"/> decides whether a posted invoice demands an IRN —
/// that depends on the business's turnover, which is a fact about the business and not something
/// this application should try to infer. <see cref="EwayBillThresholdMinor"/> is a statutory
/// threshold, and non-negotiable #6 puts thresholds in config rows. <see cref="Environment"/> keeps
/// production behind a switch the owner throws after sandbox sign-off, per §2.
/// </para>
/// <para>
/// The four credentials are held only as ciphertext. They are encrypted before they arrive here and
/// this entity has no way to decrypt them — it cannot, because the domain knows nothing about DPAPI
/// or any other platform service. That is what keeps checkpoint C6.6 achievable: there is no
/// property on this type whose value is a readable secret, so no logger, exporter or audit writer
/// can stumble onto one. The username is treated as a credential alongside the password because the
/// spec lists it as one; the GSTIN is not, since it is printed on every invoice anyway.
/// </para>
/// </remarks>
public sealed class EinvoiceSettings : Entity, IAuditStamped
{
    /// <summary>The e-way bill threshold as seeded: ₹50,000 in minor units of 1/10,000.</summary>
    public const long DefaultEwayBillThresholdMinor = 50_000L * 10_000L;

    private EinvoiceSettings()
    {
        // EF Core materialisation.
    }

    public EinvoiceSettings(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        IsEinvoicingApplicable = true;
        Environment = IrpEnvironment.Sandbox;
        EwayBillThresholdMinor = DefaultEwayBillThresholdMinor;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Whether a posted tax invoice is expected to carry an IRN. Turnover-dependent, so it is the
    /// owner's declaration rather than something derived.
    /// </summary>
    public bool IsEinvoicingApplicable { get; private set; }

    /// <summary>Sandbox until the owner signs off; never defaults to production.</summary>
    public IrpEnvironment Environment { get; private set; }

    /// <summary>The value a consignment must exceed before an e-way bill is required.</summary>
    public long EwayBillThresholdMinor { get; private set; }

    /// <summary>DPAPI-protected NIC client id.</summary>
    public byte[]? ProtectedClientId { get; private set; }

    /// <summary>DPAPI-protected NIC client secret.</summary>
    public byte[]? ProtectedClientSecret { get; private set; }

    /// <summary>DPAPI-protected NIC API username.</summary>
    public byte[]? ProtectedUsername { get; private set; }

    /// <summary>DPAPI-protected NIC API password.</summary>
    public byte[]? ProtectedPassword { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>True when all four credentials have been supplied.</summary>
    public bool HasCredentials =>
        ProtectedClientId is { Length: > 0 }
        && ProtectedClientSecret is { Length: > 0 }
        && ProtectedUsername is { Length: > 0 }
        && ProtectedPassword is { Length: > 0 };

    public void SetApplicability(bool isApplicable) => IsEinvoicingApplicable = isApplicable;

    public void SetEnvironment(IrpEnvironment environment)
    {
        if (environment is IrpEnvironment.Production && !HasCredentials)
        {
            throw new InvalidOperationException(
                "Production cannot be selected before NIC credentials have been supplied.");
        }

        Environment = environment;
    }

    public void SetEwayBillThresholdMinor(long thresholdMinor)
    {
        if (thresholdMinor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdMinor), thresholdMinor, "A threshold cannot be negative.");
        }

        EwayBillThresholdMinor = thresholdMinor;
    }

    /// <summary>
    /// Replaces the stored credentials. Every argument is already ciphertext; passing plaintext here
    /// would defeat the entire arrangement, which is why the parameters are <c>byte[]</c> and not
    /// <c>string</c>.
    /// </summary>
    public void SetProtectedCredentials(
        byte[] protectedClientId,
        byte[] protectedClientSecret,
        byte[] protectedUsername,
        byte[] protectedPassword)
    {
        ArgumentNullException.ThrowIfNull(protectedClientId);
        ArgumentNullException.ThrowIfNull(protectedClientSecret);
        ArgumentNullException.ThrowIfNull(protectedUsername);
        ArgumentNullException.ThrowIfNull(protectedPassword);

        if (protectedClientId.Length is 0
            || protectedClientSecret.Length is 0
            || protectedUsername.Length is 0
            || protectedPassword.Length is 0)
        {
            throw new ArgumentException("A protected credential must not be empty.");
        }

        ProtectedClientId = protectedClientId;
        ProtectedClientSecret = protectedClientSecret;
        ProtectedUsername = protectedUsername;
        ProtectedPassword = protectedPassword;
    }

    /// <summary>Forgets the stored credentials and drops back to sandbox.</summary>
    public void ClearCredentials()
    {
        ProtectedClientId = null;
        ProtectedClientSecret = null;
        ProtectedUsername = null;
        ProtectedPassword = null;
        Environment = IrpEnvironment.Sandbox;
    }
}
