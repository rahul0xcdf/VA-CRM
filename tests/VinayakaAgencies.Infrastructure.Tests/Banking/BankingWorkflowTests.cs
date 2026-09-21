using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Banking;
using VinayakaAgencies.Domain.Printing;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Banking;

/// <summary>
/// The Phase 7a banking slice against a real SQLite file: payment settlement with its instrument,
/// the e-payment reference fields, and the cheque layout an owner can reposition without a rebuild
/// (phase-7-tds-payroll-banking.md §2, checkpoint C7.8).
/// </summary>
public sealed class BankingWorkflowTests
{
    private static readonly DateOnly PaymentDate = new(2026, 5, 12);

    [Fact]
    public async Task PostAsync_ForACheque_PostsTheVoucherAndRecordsTheInstrumentTogether()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        var instrument = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                paymentTypeId,
                PaymentDate,
                world.ContractorLedgerId,
                world.BankLedgerId,
                Amount: 25_000m,
                PaymentMode.Cheque,
                InstrumentDate: new DateOnly(2026, 5, 20),
                InstrumentNumber: "004512",
                Narration: "Freight settlement")));

        instrument.Mode.ShouldBe(PaymentMode.Cheque);
        instrument.InstrumentNumber.ShouldBe("004512");
        instrument.Amount.ShouldBe(25_000m);

        // The payee defaults from the party ledger but stays editable — the name on a cheque is
        // sometimes not the ledger name.
        instrument.PayeeName.ShouldBe("Ravi Transport");

        // A post-dated cheque is the ordinary case: the instrument date is its own, not the
        // voucher's, and it is what reconciliation works from.
        instrument.InstrumentDate.ShouldBe(new DateOnly(2026, 5, 20));
        instrument.VoucherDate.ShouldBe(PaymentDate);

        // A payment debits the party and credits the bank; the operator never states the signs.
        var lines = await ReadJournalAsync(host, instrument.VoucherId);

        lines.Sum(line => line.AmountMinor).ShouldBe(0L);
        lines.Single(line => line.LedgerId == world.ContractorLedgerId).AmountMinor
            .ShouldBe(250_000_000L);
        lines.Single(line => line.LedgerId == world.BankLedgerId).AmountMinor
            .ShouldBe(-250_000_000L);
    }

    [Fact]
    public async Task PostAsync_ForAReceipt_MirrorsTheEntry()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var receiptTypeId = await VoucherTypeIdAsync(host, VoucherKind.Receipt);

        var instrument = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                receiptTypeId,
                PaymentDate,
                world.CustomerLedgerId,
                world.BankLedgerId,
                Amount: 18_000m,
                PaymentMode.Upi,
                BankReference: "426512345678",
                IsReceipt: true)));

        var lines = await ReadJournalAsync(host, instrument.VoucherId);

        lines.Single(line => line.LedgerId == world.BankLedgerId).AmountMinor
            .ShouldBe(180_000_000L, "money came in, so the bank is debited");
        lines.Single(line => line.LedgerId == world.CustomerLedgerId).AmountMinor
            .ShouldBe(-180_000_000L);

        // The register shows the face value regardless of direction.
        instrument.Amount.ShouldBe(18_000m);
    }

    /// <summary>
    /// The e-payment reference fields the spec names: NEFT, RTGS, IMPS and UPI each carry the
    /// bank's own reference, and none of them carries an instrument number.
    /// </summary>
    [Theory]
    [InlineData(PaymentMode.Neft)]
    [InlineData(PaymentMode.Rtgs)]
    [InlineData(PaymentMode.Imps)]
    [InlineData(PaymentMode.Upi)]
    public async Task PostAsync_ForAnElectronicMode_KeepsTheBankReferenceAndNoInstrumentNumber(
        PaymentMode mode)
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        var instrument = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                paymentTypeId,
                PaymentDate,
                world.ContractorLedgerId,
                world.BankLedgerId,
                Amount: 5_000m,
                mode,
                BankReference: "UTR2026051200001",

                // Offered and dropped: an electronic transfer has no instrument to number, and a
                // stale one left behind would reconcile against a cheque that was never written.
                InstrumentNumber: "004512")));

        instrument.BankReference.ShouldBe("UTR2026051200001");
        instrument.InstrumentNumber.ShouldBeNull();
        instrument.IsPrintable.ShouldBeFalse();
    }

    [Fact]
    public async Task PostAsync_ForAnElectronicModeWithNoReference_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        await Should.ThrowAsync<ArgumentException>(() =>
            host.InScopeAsync(sp =>
                sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                    paymentTypeId,
                    PaymentDate,
                    world.ContractorLedgerId,
                    world.BankLedgerId,
                    Amount: 5_000m,
                    PaymentMode.Neft))));
    }

    // ------------------------------------------------- C7.8 — repositioning without a rebuild

    [Fact]
    public async Task AChequeLayoutIsSeededAsAPrintTemplateAndResolvesAsTheDefault()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await TdsTestWorld.ArrangeAsync(host);

        var template = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPrintTemplateService>()
                .ResolveDefaultAsync(PrintDocumentKind.Cheque));

        template.ShouldNotBeNull();
        template.Name.ShouldBe(ChequeLayoutDefaults.DefaultTemplateName);

        var detail = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPrintTemplateService>().GetAsync(template.Id));

        // Every field the renderer understands has a measured home, in millimetres.
        detail!.Layout.PlacedFields.Count.ShouldBe(ChequeLayoutDefaults.FieldNames.Count);
        detail.Layout.PlacedFields
            .Select(field => field.Field)
            .ShouldBe(ChequeLayoutDefaults.FieldNames, ignoreOrder: true);
    }

    [Fact]
    public async Task RepositioningAChequeLayout_AppendsAVersionAndTakesEffectImmediately()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await TdsTestWorld.ArrangeAsync(host);

        var templateId = (await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPrintTemplateService>()
                .ResolveDefaultAsync(PrintDocumentKind.Cheque)))!.Id;

        var before = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPrintTemplateService>().GetAsync(templateId));

        // The owner prints onto a real cheque, finds the payee line 4 mm too high and the A/C
        // payee crossing wanted after all, and edits the numbers. No code, no rebuild.
        var adjusted = before!.Layout with
        {
            Fields = before.Layout.PlacedFields
                .Select(field => field.Field switch
                {
                    ChequeLayoutDefaults.PayeeField => field with { TopMm = field.TopMm + 4d },
                    ChequeLayoutDefaults.AccountPayeeField => field with { IsVisible = true },
                    _ => field,
                })
                .ToList(),
        };

        var saved = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IPrintTemplateService>().SaveLayoutAsync(templateId, adjusted));

        saved.CurrentVersion.ShouldBe(2, "a layout edit appends a version, it does not overwrite");

        var payee = saved.Layout.PlacedFields
            .Single(field => field.Field == ChequeLayoutDefaults.PayeeField);
        payee.TopMm.ShouldBe(29d);

        saved.Layout.PlacedFields
            .Single(field => field.Field == ChequeLayoutDefaults.AccountPayeeField)
            .IsVisible.ShouldBeTrue();

        // Version 1 is still readable — which is the point of versioning a layout at all.
        saved.Revisions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetChequeDataAsync_WordsTheAmountOnceForFiguresAndWordsAlike()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        var instrument = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                paymentTypeId,
                PaymentDate,
                world.ContractorLedgerId,
                world.BankLedgerId,
                Amount: 1_25_000m,
                PaymentMode.Cheque,
                InstrumentNumber: "004513",
                PayeeName: "Ravi Transport Services")));

        var cheque = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().GetChequeDataAsync(instrument.Id));

        cheque.ShouldNotBeNull();
        cheque.PayeeName.ShouldBe("Ravi Transport Services");
        cheque.AmountInWords.ShouldBe("Rupees One Lakh Twenty Five Thousand Only");
        cheque.AmountInFigures.ShouldBe("1,25,000.00");
        cheque.ChequeNumber.ShouldBe("004513");
    }

    [Fact]
    public async Task GetChequeDataAsync_ForAnElectronicPayment_IsNullBecauseThereIsNothingToPrint()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        var instrument = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                paymentTypeId,
                PaymentDate,
                world.ContractorLedgerId,
                world.BankLedgerId,
                Amount: 5_000m,
                PaymentMode.Neft,
                BankReference: "UTR001")));

        (await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().GetChequeDataAsync(instrument.Id)))
            .ShouldBeNull();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            host.InScopeAsync(sp =>
                sp.GetRequiredService<IBankingService>().MarkPrintedAsync(instrument.Id)));
    }

    [Fact]
    public async Task MarkPrintedAsync_RecordsThePrintSoAReprintIsDeliberate()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        var instrument = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                paymentTypeId,
                PaymentDate,
                world.ContractorLedgerId,
                world.BankLedgerId,
                Amount: 5_000m,
                PaymentMode.Cheque,
                InstrumentNumber: "004514")));

        instrument.IsPrinted.ShouldBeFalse();

        var printed = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().MarkPrintedAsync(instrument.Id));

        printed.IsPrinted.ShouldBeTrue();
        printed.PrintedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateInstrumentAsync_CorrectsAMistypedChequeNumberWithoutTouchingTheJournal()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        var instrument = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                paymentTypeId,
                PaymentDate,
                world.ContractorLedgerId,
                world.BankLedgerId,
                Amount: 5_000m,
                PaymentMode.Cheque,
                InstrumentNumber: "004515")));

        var before = await ReadJournalAsync(host, instrument.VoucherId);

        var corrected = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().UpdateInstrumentAsync(
                instrument.Id,
                PaymentMode.Cheque,
                instrument.InstrumentDate,
                instrumentNumber: "004516",
                bankReference: null,
                payeeName: instrument.PayeeName,
                remarks: "leaf 004515 spoiled"));

        corrected.InstrumentNumber.ShouldBe("004516");
        corrected.Remarks.ShouldBe("leaf 004515 spoiled");

        // The accounting did not move: correcting the paper is not an alteration of the entry.
        var after = await ReadJournalAsync(host, instrument.VoucherId);
        after.Count.ShouldBe(before.Count);
        after.Sum(line => line.AmountMinor).ShouldBe(0L);
    }

    [Fact]
    public async Task ListAsync_IsTheChequeRegisterForABankAccountOverAPeriod()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await TdsTestWorld.ArrangeAsync(host);

        var paymentTypeId = await VoucherTypeIdAsync(host, VoucherKind.Payment);

        foreach (var (number, day) in new[] { ("004520", 5), ("004521", 12), ("004522", 25) })
        {
            await host.InScopeAsync(sp =>
                sp.GetRequiredService<IBankingService>().PostAsync(new PostBankVoucherRequest(
                    paymentTypeId,
                    new DateOnly(2026, 5, day),
                    world.ContractorLedgerId,
                    world.BankLedgerId,
                    Amount: 1_000m,
                    PaymentMode.Cheque,
                    InstrumentNumber: number)));
        }

        var register = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IBankingService>().ListAsync(
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 20),
                world.BankLedgerId,
                PaymentMode.Cheque));

        register.Count.ShouldBe(2, "the 25th falls outside the period");
        register.Select(row => row.InstrumentNumber).ShouldBe(["004521", "004520"]);
        register.ShouldAllBe(row => row.PartyLedgerName == "Ravi Transport");
    }

    private static Task<Guid> VoucherTypeIdAsync(SqliteTestHost host, VoucherKind kind) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            return (await unitOfWork.Repository<VoucherType>().ListAsync())
                .First(type => type.Kind == kind)
                .Id;
        });

    private static Task<List<JournalLine>> ReadJournalAsync(SqliteTestHost host, Guid voucherId) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            return (await unitOfWork.Repository<JournalLine>().ListAsync())
                .Where(line => line.VoucherId == voucherId)
                .ToList();
        });
}
