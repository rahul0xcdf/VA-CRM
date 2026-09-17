using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// One persisted request/response pair against the IRP or the e-way bill API
/// (phase-6-einvoice-eway.md §2, "resilience is part of the deliverable").
/// </summary>
/// <remarks>
/// <para>
/// The spec requires request/response pairs to be persisted, and this is where they land —
/// append-only, one row per attempt, never updated. When the portal and our books disagree months
/// later, this is the only record of what was actually sent, and a row that could be edited would
/// be worth nothing in that argument.
/// </para>
/// <para>
/// Bodies arrive here already redacted. The redaction happens at the client boundary, before the
/// text ever reaches this entity, because the alternative — storing the raw body and filtering on
/// the way out — leaves the secret on disk. Checkpoint C6.6 scans these rows along with the logs.
/// </para>
/// <para>
/// Rows group under their <see cref="EinvoiceRegistration"/> for edit-log purposes, so the
/// registration id is carried denormalised exactly as <c>JournalLine.VoucherId</c> is.
/// </para>
/// </remarks>
public sealed class EinvoiceApiCall : Entity, ICreationStamped, IBelongsToAggregate
{
    private EinvoiceApiCall()
    {
        // EF Core materialisation.
    }

    public EinvoiceApiCall(
        Guid companyId,
        Guid einvoiceRegistrationId,
        string operation,
        string endpoint,
        string redactedRequestBody,
        DateTimeOffset startedAtUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (einvoiceRegistrationId == Guid.Empty)
        {
            throw new ArgumentException(
                "An API call must name the registration it belongs to.",
                nameof(einvoiceRegistrationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(redactedRequestBody);

        CompanyId = companyId;
        EinvoiceRegistrationId = einvoiceRegistrationId;
        Operation = operation.Trim();
        Endpoint = endpoint.Trim();
        RedactedRequestBody = redactedRequestBody;
        StartedAtUtc = startedAtUtc;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The registration this call was made for — denormalised for edit-log grouping.</summary>
    public Guid EinvoiceRegistrationId { get; private set; }

    /// <summary>Which API operation this was, e.g. <c>GenerateIrn</c> or <c>CancelIrn</c>.</summary>
    public string Operation { get; private set; } = string.Empty;

    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>The request body with every credential already removed.</summary>
    public string RedactedRequestBody { get; private set; } = string.Empty;

    /// <summary>The response body with every credential already removed, once one arrived.</summary>
    public string? RedactedResponseBody { get; private set; }

    public int? HttpStatusCode { get; private set; }

    /// <summary>The portal's own error code, when it returned one.</summary>
    public string? PortalErrorCode { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>True when this call produced no response at all — a timeout or a dropped connection.</summary>
    public bool TimedOut { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(EinvoiceRegistration);

    public string AggregateId => EinvoiceRegistrationId.ToString();

    /// <summary>Records the response. Called once; a retry is a new row, never an overwrite.</summary>
    public void Complete(
        int httpStatusCode,
        string redactedResponseBody,
        DateTimeOffset completedAtUtc,
        string? portalErrorCode = null)
    {
        ArgumentNullException.ThrowIfNull(redactedResponseBody);

        HttpStatusCode = httpStatusCode;
        RedactedResponseBody = redactedResponseBody;
        CompletedAtUtc = completedAtUtc;
        PortalErrorCode = string.IsNullOrWhiteSpace(portalErrorCode) ? null : portalErrorCode.Trim();
        TimedOut = false;
    }

    /// <summary>Records that no response arrived. This is the row a retry is reasoning from.</summary>
    public void MarkTimedOut(DateTimeOffset abandonedAtUtc)
    {
        CompletedAtUtc = abandonedAtUtc;
        TimedOut = true;
    }
}
