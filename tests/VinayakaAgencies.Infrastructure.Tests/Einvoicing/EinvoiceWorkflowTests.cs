using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Einvoicing;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Einvoicing;

/// <summary>
/// The e-invoice workflow end to end against a recorded portal
/// (phase-6-einvoice-eway.md §2; checkpoints C6.4, C6.5, C6.6).
/// </summary>
/// <remarks>
/// These run through the real services, the real unit of work and a real SQLite file, with only the
/// two ports substituted: <see cref="IIrpClient"/>, because there are no NIC sandbox credentials
/// (<c>open-questions.md</c> #10), and <see cref="ICredentialProtector"/>, because DPAPI encrypts for
/// one Windows user. Everything else — the unique index that makes duplicate IRNs impossible, the
/// commit ordering, the persisted call rows — is the production code path.
/// </remarks>
public sealed class EinvoiceWorkflowTests
{
    private const string CompanyGstin = EinvoiceTestWorld.CompanyGstin;
    private const string ClientSecret = EinvoiceTestWorld.ClientSecret;

    private static readonly DateOnly InvoiceDate = EinvoiceTestWorld.InvoiceDate;

    private static string[] AllSecrets => EinvoiceTestWorld.AllSecrets;

    [Fact]
    public async Task GenerateAsync_ForAPostedInvoice_RegistersItAndMatchesOurComputedIrn()
    {
        // Checkpoint C6.4, as far as it can be verified without the sandbox: the IRN we compute is the
        // IRN the portal computes from the same four inputs.
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var result = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        result.Registration.Status.ShouldBe(EinvoiceStatus.Generated);
        result.WasAlreadyRegistered.ShouldBeFalse();
        result.PortalIrnMatchedOurs.ShouldBe(true);

        result.Registration.Irn.ShouldBe(IrnFormula.Compute(
            CompanyGstin, InvoiceDate, EinvoiceDocumentType.TaxInvoice, world.InvoiceNumber));
        result.Registration.SignedQrCodeJws.ShouldNotBeNullOrWhiteSpace();
        result.Registration.AcknowledgementNumber.ShouldNotBeNull();
    }

    [Fact]
    public async Task GenerateAsync_RetriedAfterATimeoutThatActuallyRegistered_ProducesNoDuplicateIrn()
    {
        // Checkpoint C6.5, the case that matters: the portal registered the invoice and the response
        // never arrived. The retry must discover that rather than submit a second time.
        var portal = new RecordedIrpServer { NextGenerateTimesOutAfterRegistering = true };
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var first = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        // Unresolved, not failed: the row keeps its IRN and stays retryable.
        first.Registration.Status.ShouldBe(EinvoiceStatus.Pending);
        var irnAfterTimeout = first.Registration.Irn;

        var second = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        second.Registration.Status.ShouldBe(EinvoiceStatus.Generated);
        second.WasAlreadyRegistered.ShouldBeTrue();
        second.Registration.Irn.ShouldBe(irnAfterTimeout);

        // The retry asked the portal about the IRN instead of submitting again.
        portal.GenerateAttempts.ShouldBe(1);
        portal.Calls.ShouldContain(nameof(RecordedIrpServer.GetByIrnAsync));

        // One registration in the books, and one invoice registered at the portal.
        portal.RegisteredCount.ShouldBe(1);
        (await RegistrationsAsync(host)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task GenerateAsync_RetriedAfterATimeoutThatNeverReachedThePortal_RegistersOnce()
    {
        // The other half of C6.5: the call never landed, so the retry must genuinely submit — and must
        // still end up with exactly one registration under the same IRN.
        var portal = new RecordedIrpServer { NextCallTimesOutBeforeRegistering = true };
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var first = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        first.Registration.Status.ShouldBe(EinvoiceStatus.Pending);

        var second = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        second.Registration.Status.ShouldBe(EinvoiceStatus.Generated);
        second.Registration.Irn.ShouldBe(first.Registration.Irn);

        portal.RegisteredCount.ShouldBe(1);
        (await RegistrationsAsync(host)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task GenerateAsync_CalledTwiceAfterSuccess_MakesNoFurtherPortalCall()
    {
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        var callsAfterFirst = portal.Calls.Count;

        var again = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        again.WasAlreadyRegistered.ShouldBeTrue();

        // A "refresh" that re-submitted would be a duplicate submission wearing a friendly name.
        portal.Calls.Count.ShouldBe(callsAfterFirst);
    }

    [Fact]
    public async Task GenerateAsync_EveryAttempt_IsPersistedAsAnApiCallRow()
    {
        // The spec requires request/response pairs to be persisted; this is that requirement, asserted.
        var portal = new RecordedIrpServer { NextCallTimesOutBeforeRegistering = true };
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        var calls = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IUnitOfWork>().Repository<EinvoiceApiCall>().ListAsync());

        // Three attempts: the timeout, the lookup that settled it, and the resubmission.
        calls.Count.ShouldBe(3);
        calls.ShouldContain(call => call.TimedOut);
        calls.ShouldAllBe(call => call.EinvoiceRegistrationId != Guid.Empty);
    }

    [Fact]
    public async Task GenerateAsync_WhenThePortalRefuses_MarksRejectedAndKeepsTheReason()
    {
        var portal = new RecordedIrpServer { NextCallRejectionMessage = "2172: Invalid GSTIN" };
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var result = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        result.Registration.Status.ShouldBe(EinvoiceStatus.Rejected);
        result.Registration.LastErrorText!.ShouldContain("2172");
    }

    [Fact]
    public async Task GenerateAsync_WithoutCredentials_RefusesBeforeCallingAnything()
    {
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host, withCredentials: false);

        await Should.ThrowAsync<EinvoiceNotSubmittableException>(() => host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId)));

        portal.Calls.ShouldBeEmpty();
        (await RegistrationsAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_ForADraftInvoice_IsRefusedBecauseTheIrnNeedsANumber()
    {
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var draftId = await host.InScopeAsync(async sp =>
        {
            var draft = await sp.GetRequiredService<ITradeDocumentService>().CreateAsync(
                new SaveTradeDocumentRequest(
                    TradeDocumentKind.TaxInvoice,
                    InvoiceDate,
                    world.CustomerLedgerId,
                    [new TradeDocumentLineInput(world.ItemId, world.GodownId, 1m, 500m)]));

            return draft.Id;
        });

        await Should.ThrowAsync<EinvoiceNotSubmittableException>(() => host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().BuildRequestAsync(draftId)));
    }

    [Fact]
    public async Task CancelAsync_WithinTheWindow_CancelsAtThePortal()
    {
        // The local half of checkpoint C6.3. The sandbox half stays open with open-questions.md #10.
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var generated = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        var cancelled = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>()
                .CancelAsync(generated.Registration.Id, "1", "Wrong party"));

        cancelled.Status.ShouldBe(EinvoiceStatus.Cancelled);
        portal.RegisteredCount.ShouldBe(0);
    }

    [Fact]
    public async Task CancelAsync_AfterTheWindowHasClosed_IsRefusedWithoutCallingThePortal()
    {
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var generated = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        // The deadline is 24 hours after the portal's AckDt, not 24 hours after "now": the recorded
        // portal acknowledges at 2026-09-17 17:30 IST, which is 12:00 UTC, so the window shuts at
        // 2026-09-18 12:00 UTC. Moving the clock relative to its own start would prove nothing.
        host.Clock.UtcNow = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);

        var callsBefore = portal.Calls.Count;

        await Should.ThrowAsync<InvalidOperationException>(() => host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().CancelAsync(generated.Registration.Id, "1")));

        portal.Calls.Count.ShouldBe(callsBefore);
    }

    [Fact]
    public async Task StoredCredentials_NeverAppearInPersistedCallRowsOrTheAuditTrail()
    {
        // Checkpoint C6.6. The scan covers the persisted request/response pairs, the settings row's own
        // stored bytes, and the audit trail — the three places a credential could come to rest.
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().GenerateAsync(world.InvoiceId));

        var callRows = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IUnitOfWork>().Repository<EinvoiceApiCall>().ListAsync());

        foreach (var row in callRows)
        {
            SensitiveText.ContainsAny(row.RedactedRequestBody, AllSecrets).ShouldBeFalse();
            SensitiveText.ContainsAny(row.RedactedResponseBody, AllSecrets).ShouldBeFalse();
        }

        // The whole database file, read as text — the bluntest possible version of the assertion, and
        // the one that would catch a credential stored somewhere nobody thought to look.
        var databaseText = await ReadDatabaseAsTextAsync(host.DatabasePath);

        SensitiveText.ContainsAny(databaseText, AllSecrets).ShouldBeFalse(
            "a NIC credential reached the database in plaintext");
    }

    [Fact]
    public async Task StoredCredentials_RoundTripThroughTheProtector_ButAreNotStoredAsPlaintext()
    {
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        await ArrangeAsync(host);

        var settings = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceSettingsService>().GetEntityAsync());

        settings.HasCredentials.ShouldBeTrue();

        // What is stored is ciphertext; what comes back out is the original.
        System.Text.Encoding.UTF8.GetString(settings.ProtectedClientSecret!)
            .ShouldNotBe(ClientSecret);

        var resolved = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceSettingsService>().ResolveCredentialsAsync());

        resolved.ClientSecret.ShouldBe(ClientSecret);
        resolved.Gstin.ShouldBe(CompanyGstin);
        resolved.Environment.ShouldBe(IrpEnvironment.Sandbox);
    }

    [Fact]
    public async Task SetEnvironmentAsync_ToProductionWithoutCredentials_IsRefused()
    {
        // §2 keeps production behind sandbox sign-off; this is the mechanical half of that.
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        await ArrangeAsync(host, withCredentials: false);

        await Should.ThrowAsync<InvalidOperationException>(() => host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceSettingsService>()
                .SetEnvironmentAsync(IrpEnvironment.Production)));
    }

    [Fact]
    public async Task EinvoiceSettings_DefaultToSandboxWithTheSeededThreshold()
    {
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        await ArrangeAsync(host, withCredentials: false);

        var summary = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceSettingsService>().GetAsync());

        summary.Environment.ShouldBe(IrpEnvironment.Sandbox);
        summary.IsEinvoicingApplicable.ShouldBeTrue();
        summary.EwayBillThresholdMinor.ShouldBe(EinvoiceSettings.DefaultEwayBillThresholdMinor);
        summary.HasCredentials.ShouldBeFalse();
    }

    [Fact]
    public async Task BuildRequestAsync_WithAnIncompleteCompanyAddress_SaysWhichFieldIsMissing()
    {
        // The §4b gate: nullable columns, refused at submission rather than at data entry.
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = await unitOfWork.Repository<Company>().GetByIdAsync(world.CompanyId);

            company!.SetAddress(addressLine1: null);
            unitOfWork.Repository<Company>().Update(company);
            await unitOfWork.SaveChangesAsync();
        });

        var exception = await Should.ThrowAsync<EinvoiceNotSubmittableException>(() =>
            host.InScopeAsync(sp =>
                sp.GetRequiredService<IEinvoiceService>().BuildRequestAsync(world.InvoiceId)));

        exception.Message.ShouldContain("pincode");
    }

    [Fact]
    public async Task BuildRequestAsync_CarriesThePostedTaxSnapshot_RatherThanRecomputingIt()
    {
        var portal = new RecordedIrpServer();
        await using var host = NewHost(portal);
        var world = await ArrangeAsync(host);

        var request = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IEinvoiceService>().BuildRequestAsync(world.InvoiceId));

        request.Lines.Count.ShouldBe(1);

        var line = request.Lines[0];

        // ₹1,000 taxable into Maharashtra at 18% → IGST ₹180, no CGST/SGST.
        line.HsnCode.ShouldBe("84137010");
        line.TaxableValueMinor.ShouldBe(10_000_000L);
        line.IgstMinor.ShouldBe(1_800_000L);
        line.CgstMinor.ShouldBe(0L);
        line.SgstMinor.ShouldBe(0L);

        request.Seller.Gstin.ShouldBe(CompanyGstin);
        request.Buyer.Gstin.ShouldBe("27AAPFU0939F1ZV");
        request.PlaceOfSupplyStateCode.ShouldBe("27");
    }

    private static SqliteTestHost NewHost(RecordedIrpServer portal) =>
        EinvoiceTestWorld.NewHost(portal);

    private static Task<EinvoiceWorld> ArrangeAsync(
        SqliteTestHost host,
        bool withCredentials = true) =>
        EinvoiceTestWorld.ArrangeAsync(host, withCredentials);

    private static Task<IReadOnlyList<EinvoiceRegistration>> RegistrationsAsync(SqliteTestHost host) =>
        host.InScopeAsync(sp =>
            sp.GetRequiredService<IUnitOfWork>().Repository<EinvoiceRegistration>().ListAsync());

    /// <summary>
    /// Reads the SQLite file as text so a plaintext secret anywhere in it would be visible. Latin-1 is
    /// used rather than UTF-8 because it never fails on a byte sequence, so no part of the file is
    /// silently skipped.
    /// </summary>
    private static async Task<string> ReadDatabaseAsTextAsync(string databasePath)
    {
        // FileShare.ReadWrite because the host still holds the database open — on Windows a plain
        // ReadAllBytes would fail with a sharing violation rather than read it.
        await using var stream = new FileStream(
            databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        return System.Text.Encoding.Latin1.GetString(buffer.ToArray());
    }
}
