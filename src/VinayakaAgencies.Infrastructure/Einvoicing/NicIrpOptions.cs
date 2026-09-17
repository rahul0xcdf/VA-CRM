namespace VinayakaAgencies.Infrastructure.Einvoicing;

/// <summary>
/// Endpoints and the NIC public certificate, bound from configuration
/// (phase-6-einvoice-eway.md §2, §4a).
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is configuration rather than a constant, for two different reasons. The
/// certificate is environment data NIC publishes and rotates, and it is not ours to ship. The route
/// paths carry a version segment that NIC bumps — <c>v1.03</c> today — so a version change must be a
/// settings edit, not a rebuild, in the same spirit as non-negotiable #6.
/// </para>
/// <para>
/// The defaults below are the documented sandbox routes. They are <b>unverified against a live
/// portal</b> because <c>open-questions.md</c> #10 is still open, and confirming them is part of
/// sandbox sign-off (C6.1–C6.3). They are defaults so the wiring is complete and testable, not a
/// claim that they have been exercised.
/// </para>
/// </remarks>
public sealed class NicIrpOptions
{
    public const string SectionName = "Einvoicing:Nic";

    /// <summary>Base address of the sandbox portal.</summary>
    public string SandboxBaseAddress { get; set; } = "https://einv-apisandbox.nic.in";

    /// <summary>
    /// Base address of the production portal. Unset by default: production is switched on only after
    /// the owner signs off on sandbox (§2), and an absent address makes that a configuration step
    /// rather than a toggle somebody can flip by accident.
    /// </summary>
    public string? ProductionBaseAddress { get; set; }

    /// <summary>NIC's public certificate, base64 DER. Supplied with the credentials.</summary>
    public string? PublicCertificateBase64 { get; set; }

    public string AuthPath { get; set; } = "/eivital/v1.04/auth";

    public string GenerateIrnPath { get; set; } = "/eicore/v1.03/Invoice";

    /// <summary>Lookup route; <c>{irn}</c> is substituted.</summary>
    public string GetByIrnPath { get; set; } = "/eicore/v1.03/Invoice/irn/{irn}";

    public string CancelIrnPath { get; set; } = "/eicore/v1.03/Invoice/Cancel";

    public string GenerateEwayBillByIrnPath { get; set; } = "/eiewb/v1.03/ewaybill";

    public string UpdateEwayBillPartBPath { get; set; } = "/eiewb/v1.03/ewayapi/vehewb";

    public string CancelEwayBillPath { get; set; } = "/eiewb/v1.03/ewayapi/canewb";

    public string ExtendEwayBillPath { get; set; } = "/eiewb/v1.03/ewayapi/extendvalidity";

    /// <summary>
    /// How long to wait on a call before treating it as unanswered. Deliberately finite and
    /// deliberately short enough to be survivable: the retry path exists precisely so that giving up
    /// on a slow call is safe (checkpoint C6.5).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Safety margin on the auth token's stated expiry, so a token is refreshed slightly early rather
    /// than expiring mid-call.
    /// </summary>
    public int TokenExpirySafetyMarginSeconds { get; set; } = 120;
}
