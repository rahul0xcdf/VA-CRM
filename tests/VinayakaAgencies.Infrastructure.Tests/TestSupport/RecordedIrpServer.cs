using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// A stand-in for NIC's portal that speaks the documented contract
/// (phase-6-einvoice-eway.md §4a).
/// </summary>
/// <remarks>
/// <para>
/// This exists because <c>open-questions.md</c> #10 is open: there are no sandbox credentials, so the
/// only way to verify the behaviour above the <see cref="IIrpClient"/> port is to drive it with a
/// double. It is deliberately a <em>recording</em> double rather than a mock — the tests that matter
/// ask questions like "how many registrations exist after a retry?", which is about state, not about
/// which methods were called.
/// </para>
/// <para>
/// It keeps a registry of IRNs it has issued, so it can behave like the real portal in the case
/// checkpoint C6.5 is about: a call that times out on our side may still have registered the invoice,
/// and a later lookup must then find it.
/// </para>
/// <para>
/// This is not a substitute for sandbox sign-off. C6.1–C6.3 need the real portal and stay open.
/// </para>
/// </remarks>
internal sealed class RecordedIrpServer : IIrpClient
{
    private readonly Dictionary<string, IssuedInvoice> _issued = new(StringComparer.OrdinalIgnoreCase);
    private long _nextAcknowledgement = 112_010_000_001L;
    private long _nextEwayBillNumber = 391_000_000_001L;

    /// <summary>Every call made, in order — for asserting that a retry did not re-submit.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// When true, <see cref="GenerateIrnAsync"/> registers the invoice and <em>then</em> reports no
    /// response — the exact shape of the failure C6.5 describes, where the portal did the work and we
    /// never heard about it.
    /// </summary>
    public bool NextGenerateTimesOutAfterRegistering { get; set; }

    /// <summary>When true, the next call reports no response without doing anything at all.</summary>
    public bool NextCallTimesOutBeforeRegistering { get; set; }

    /// <summary>When set, the next call is refused with this error.</summary>
    public string? NextCallRejectionMessage { get; set; }

    /// <summary>How many invoices this portal believes it has registered.</summary>
    public int RegisteredCount => _issued.Count;

    /// <summary>The number of times a generate request actually reached the portal.</summary>
    public int GenerateAttempts => Calls.Count(call => call is nameof(GenerateIrnAsync));

    public Task<IrpIrnResponse> GenerateIrnAsync(
        IrpCredentials credentials,
        EinvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GenerateIrnAsync));

        var irn = IrnFormula.Compute(
            credentials.Gstin, request.DocumentDate, request.DocumentType, request.DocumentNumber);

        if (NextCallTimesOutBeforeRegistering)
        {
            NextCallTimesOutBeforeRegistering = false;

            return Task.FromResult(new IrpIrnResponse(NoResponse("GenerateIrn")));
        }

        if (NextCallRejectionMessage is { } rejection)
        {
            NextCallRejectionMessage = null;

            return Task.FromResult(new IrpIrnResponse(Rejected("GenerateIrn", rejection)));
        }

        if (_issued.ContainsKey(irn))
        {
            // The real portal's duplicate-IRN refusal. If our idempotency is working, no test should
            // ever provoke this — which makes it a useful tripwire rather than dead code.
            return Task.FromResult(new IrpIrnResponse(
                Rejected("GenerateIrn", "2150: Duplicate IRN")));
        }

        var issued = new IssuedInvoice(
            irn,
            _nextAcknowledgement++,
            "2026-09-17 17:30:00",
            $"signed-invoice-for-{request.DocumentNumber}",
            $"signed-qr-for-{request.DocumentNumber}");

        _issued[irn] = issued;

        if (NextGenerateTimesOutAfterRegistering)
        {
            NextGenerateTimesOutAfterRegistering = false;

            // Registered above, but the caller hears nothing. This is the interesting failure.
            return Task.FromResult(new IrpIrnResponse(NoResponse("GenerateIrn")));
        }

        return Task.FromResult(Success("GenerateIrn", issued));
    }

    public Task<IrpIrnResponse> GetByIrnAsync(
        IrpCredentials credentials,
        string irn,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetByIrnAsync));

        if (NextCallTimesOutBeforeRegistering)
        {
            NextCallTimesOutBeforeRegistering = false;

            return Task.FromResult(new IrpIrnResponse(NoResponse("GetByIrn")));
        }

        return Task.FromResult(_issued.TryGetValue(irn, out var issued)
            ? Success("GetByIrn", issued)
            : new IrpIrnResponse(Rejected("GetByIrn", "3028: IRN not found")));
    }

    public Task<IrpCancelResponse> CancelIrnAsync(
        IrpCredentials credentials,
        string irn,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(CancelIrnAsync));

        if (NextCallRejectionMessage is { } rejection)
        {
            NextCallRejectionMessage = null;

            return Task.FromResult(new IrpCancelResponse(Rejected("CancelIrn", rejection)));
        }

        if (!_issued.Remove(irn))
        {
            return Task.FromResult(new IrpCancelResponse(Rejected("CancelIrn", "3028: IRN not found")));
        }

        return Task.FromResult(new IrpCancelResponse(
            Ok("CancelIrn"), "2026-09-17 18:00:00"));
    }

    public Task<IrpEwayBillResponse> GenerateEwayBillByIrnAsync(
        IrpCredentials credentials,
        string irn,
        EwayBillPartB partB,
        int distanceKilometres,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GenerateEwayBillByIrnAsync));

        if (NextCallRejectionMessage is { } rejection)
        {
            NextCallRejectionMessage = null;

            return Task.FromResult(new IrpEwayBillResponse(Rejected("GenerateEwayBill", rejection)));
        }

        if (!_issued.ContainsKey(irn))
        {
            return Task.FromResult(new IrpEwayBillResponse(
                Rejected("GenerateEwayBill", "3028: IRN not found")));
        }

        // Mirrors the portal: it states the expiry, computed from the distance slabs.
        var days = EwayBillValidity.DaysFor(distanceKilometres, partB.VehicleType);

        return Task.FromResult(new IrpEwayBillResponse(
            Ok("GenerateEwayBill"),
            _nextEwayBillNumber++,
            "2026-09-17 17:45:00",
            new DateTime(2026, 9, 17, 17, 45, 0).AddDays(days)
                .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public Task<IrpEwayBillResponse> UpdateEwayBillPartBAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        EwayBillPartB partB,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(UpdateEwayBillPartBAsync));

        return Task.FromResult(new IrpEwayBillResponse(Ok("UpdatePartB"), ewayBillNumber));
    }

    public Task<IrpCancelResponse> CancelEwayBillAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(CancelEwayBillAsync));

        return Task.FromResult(new IrpCancelResponse(Ok("CancelEwayBill"), "2026-09-17 19:00:00"));
    }

    public Task<IrpEwayBillResponse> ExtendEwayBillValidityAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        int remainingDistanceKilometres,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ExtendEwayBillValidityAsync));

        return Task.FromResult(new IrpEwayBillResponse(
            Ok("ExtendValidity"),
            ewayBillNumber,
            "2026-09-17 17:45:00",
            "2026-09-25 17:45:00"));
    }

    private static IrpIrnResponse Success(string endpoint, IssuedInvoice issued) =>
        new(Ok(endpoint),
            issued.AcknowledgementNumber,
            issued.AcknowledgementDateText,
            issued.Irn,
            issued.SignedInvoice,
            issued.SignedQrCode);

    private static IrpCallRecord Ok(string endpoint) =>
        new(IrpOutcome.Success, endpoint, "{}", """{"Status":"1"}""", 200);

    private static IrpCallRecord Rejected(string endpoint, string message) =>
        new(IrpOutcome.Rejected, endpoint, "{}", """{"Status":"0"}""", 200, "0", message);

    private static IrpCallRecord NoResponse(string endpoint) =>
        new(IrpOutcome.NoResponse, endpoint, "{}", string.Empty, ErrorMessage: "The request timed out.");

    private sealed record IssuedInvoice(
        string Irn,
        long AcknowledgementNumber,
        string AcknowledgementDateText,
        string SignedInvoice,
        string SignedQrCode);
}
