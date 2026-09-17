using Shouldly;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Domain.Tests.Einvoicing;

/// <summary>
/// The registration lifecycle: the locally computed IRN, the retryable <c>Pending</c> state, and the
/// 24-hour cancellation window (phase-6-einvoice-eway.md §2, checkpoints C6.3 and C6.5).
/// </summary>
public sealed class EinvoiceRegistrationTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();
    private const string Gstin = "29AAAAA9999A1Z5";
    private static readonly DateOnly InvoiceDate = new(2026, 9, 17);
    private static readonly DateTimeOffset Noon = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ComputesTheIrnImmediately_WithoutThePortal()
    {
        // The IRN exists before any call is made; that is what makes it usable as the idempotency key
        // and what makes a timed-out attempt recoverable (checkpoint C6.5).
        var registration = Create();

        registration.Irn.ShouldBe(
            IrnFormula.Compute(Gstin, InvoiceDate, EinvoiceDocumentType.TaxInvoice, "SAL/0007"));
        registration.Status.ShouldBe(EinvoiceStatus.Pending);
        registration.IsRetryable.ShouldBeTrue();
        registration.IsRegistered.ShouldBeFalse();
    }

    [Fact]
    public void MarkUnresolved_LeavesTheRowPendingAndRetryable_AndKeepsTheSameIrn()
    {
        var registration = Create();
        var irn = registration.Irn;

        registration.RecordAttempt();
        registration.MarkUnresolved("The request timed out.");

        registration.Status.ShouldBe(EinvoiceStatus.Pending);
        registration.IsRetryable.ShouldBeTrue();
        registration.AttemptCount.ShouldBe(1);
        registration.Irn.ShouldBe(irn);
    }

    [Fact]
    public void MarkRejected_StopsBeingRetryable_BecauseTheDocumentMustChangeFirst()
    {
        var registration = Create();

        registration.MarkRejected("2150: Duplicate IRN");

        registration.Status.ShouldBe(EinvoiceStatus.Rejected);
        registration.IsRetryable.ShouldBeFalse();
        registration.LastErrorText.ShouldBe("2150: Duplicate IRN");
    }

    [Fact]
    public void MarkGenerated_RecordsTheAcknowledgementAndClearsTheLastError()
    {
        var registration = Create();

        registration.MarkUnresolved("Timed out once.");
        registration.MarkGenerated(112_010_001_234L, Noon, "2026-09-17 17:30:00", "signed-invoice", "signed-qr");

        registration.Status.ShouldBe(EinvoiceStatus.Generated);
        registration.IsRegistered.ShouldBeTrue();
        registration.AcknowledgementNumber.ShouldBe(112_010_001_234L);
        registration.PortalAckDateText.ShouldBe("2026-09-17 17:30:00");
        registration.SignedQrCodeJws.ShouldBe("signed-qr");
        registration.LastErrorText.ShouldBeNull();
    }

    [Fact]
    public void PortalAckDateText_IsKeptVerbatim_BecauseTheSignatureCoversIt()
    {
        var registration = Create();

        registration.MarkGenerated(1L, Noon, "2026-09-17 17:30:00", "invoice", "qr");

        // Not re-rendered from AcknowledgedAtUtc: the QR carries the timestamp exactly as NIC wrote
        // it, and re-formatting would risk drift inside a signed payload.
        registration.PortalAckDateText.ShouldBe("2026-09-17 17:30:00");
        registration.AcknowledgedAtUtc.ShouldBe(Noon);
    }

    [Fact]
    public void CancellableUntilUtc_IsTwentyFourHoursAfterAcknowledgement()
    {
        var registration = Registered();

        registration.CancellableUntilUtc.ShouldBe(Noon.AddHours(24));
    }

    [Fact]
    public void CancellableUntilUtc_BeforeAcknowledgement_IsNull()
    {
        Create().CancellableUntilUtc.ShouldBeNull();
    }

    [Fact]
    public void CanCancelAt_WithinTheWindow_IsTrue_AndAfterwardsFalse()
    {
        var registration = Registered();

        registration.CanCancelAt(Noon.AddHours(23)).ShouldBeTrue();
        registration.CanCancelAt(Noon.AddHours(24)).ShouldBeTrue();
        registration.CanCancelAt(Noon.AddHours(24).AddSeconds(1)).ShouldBeFalse();
    }

    [Fact]
    public void MarkCancelled_AfterTheWindow_IsRefusedAndSaysWhy()
    {
        var registration = Registered();

        Should.Throw<InvalidOperationException>(
                () => registration.MarkCancelled(Noon.AddHours(25), "1"))
            .Message.ShouldContain("credit note");
    }

    [Fact]
    public void MarkCancelled_OnAnUnregisteredRow_IsRefused()
    {
        var registration = Create();

        Should.Throw<InvalidOperationException>(
            () => registration.MarkCancelled(Noon, "1"));
    }

    [Fact]
    public void MarkGenerated_AfterCancellation_IsRefused()
    {
        var registration = Registered();

        registration.MarkCancelled(Noon.AddHours(1), "1");

        Should.Throw<InvalidOperationException>(
            () => registration.MarkGenerated(2L, Noon.AddHours(2), "x", "y", "z"));
    }

    [Fact]
    public void Constructor_WithoutADocumentNumber_IsRefused()
    {
        Should.Throw<ArgumentException>(() => new EinvoiceRegistration(
            Company, Document, Gstin, EinvoiceDocumentType.TaxInvoice, "  ", InvoiceDate));
    }

    [Fact]
    public void CancellationWindow_IsTwentyFourHours()
    {
        EinvoiceRegistration.CancellationWindow.ShouldBe(TimeSpan.FromHours(24));
    }

    private static EinvoiceRegistration Create() =>
        new(Company, Document, Gstin, EinvoiceDocumentType.TaxInvoice, "SAL/0007", InvoiceDate);

    private static EinvoiceRegistration Registered()
    {
        var registration = Create();

        registration.MarkGenerated(1L, Noon, "2026-09-17 17:30:00", "invoice", "qr");

        return registration;
    }
}
