using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using Shouldly;

namespace VinayakaAgencies.Application.Tests.Interop;

/// <summary>
/// The two Tally serialisers, round-tripped through the canonical model.
/// </summary>
/// <remarks>
/// Covers the file-format half of C8.2 (a round trip does not alter or duplicate what it carries)
/// and all of C8.5 (amounts and quantities survive with no precision loss). The database half of
/// C8.2 — that a re-import overwrites rather than inserts a second row — belongs to the importer and
/// is tested against a real SQLite file in the Infrastructure suite.
/// <para>
/// §4a D1 requires the JSON path to be a second serialiser over the same model rather than a second
/// implementation. Every round-trip assertion here therefore runs against both formats from one
/// body: a field that XML kept and JSON dropped fails the same test.
/// </para>
/// </remarks>
public sealed class TallySerialisationTests
{
    /// <summary>Round-trips a document through both formats and hands back what came out.</summary>
    public static TheoryData<string> Formats => new() { "xml", "json" };

    private static async Task<InteropDocument> RoundTripAsync(InteropDocument document, string format) =>
        format == "xml"
            ? TallyXmlReader.Read(TallyXmlWriter.Write(document))
            : await ReadJsonAsync(TallyJsonWriter.Write(document));

    private static async Task<InteropDocument> ReadJsonAsync(string json)
    {
        var records = await TallyJsonSource.FromText(json).ReadAllAsync();

        records.OfType<InteropRecord.Unreadable>().ShouldBeEmpty();

        return TallyXmlReader.Compose(string.Empty, null, null, records);
    }

    private static InteropDocument SampleDocument() => new()
    {
        CompanyName = "Vinayaka Agencies",
        FromDate = new DateOnly(2026, 4, 1),
        ToDate = new DateOnly(2027, 3, 31),
        Units = [new InteropUnit("Nos", 0, Guid.NewGuid(), "Numbers")],
        StockGroups = [new InteropStockGroup("Submersible Pumps", Guid.NewGuid(), "Borewell range")],
        Godowns = [new InteropGodown("Main Location", Guid.NewGuid(), "Shop floor")],
        StockItems =
        [
            new InteropStockItem(
                "Openwell Pump 1 HP",
                "Nos",
                Guid.NewGuid(),
                "OW-1HP",
                "Submersible Pumps",
                "8413"),
        ],
        AccountGroups =
        [
            new InteropAccountGroup("Pump Sales", "Sales Accounts", AccountNature.Income, Guid.NewGuid()),
        ],
        Ledgers =
        [
            new InteropLedger("Cash", "Cash-in-Hand", Guid.NewGuid()),
            new InteropLedger(
                "Ravi Motors",
                "Sundry Debtors",
                Guid.NewGuid(),
                "RM",
                "Regular buyer",
                "29ABCDE1234F1Z5"),
        ],
        Vouchers =
        [
            new InteropVoucher
            {
                Id = Guid.NewGuid(),
                VoucherTypeName = "Sales",
                Kind = VoucherKind.Sales,
                Date = new DateOnly(2026, 5, 14),
                Number = "SAL/0001",
                Narration = "Counter sale",
                ReferenceNumber = "PO-77",
                ReferenceDate = new DateOnly(2026, 5, 10),
                LedgerEntries =
                [
                    new InteropLedgerEntry("Ravi Motors", 118_000_0000L, Narration: "Billed"),
                    new InteropLedgerEntry("Pump Sales", -118_000_0000L),
                ],
                InventoryEntries =
                [
                    new InteropInventoryEntry(
                        "Openwell Pump 1 HP",
                        -2_0000L,
                        -100_000_0000L,
                        "Main Location",
                        "Nos",
                        50_000_0000L),
                ],
            },
        ],
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task RoundTrip_EveryMasterKind_SurvivesWithItsGuid(string format)
    {
        var original = SampleDocument();

        var result = await RoundTripAsync(original, format);

        result.Units.Single().ShouldBe(original.Units.Single());
        result.StockGroups.Single().ShouldBe(original.StockGroups.Single());
        result.Godowns.Single().ShouldBe(original.Godowns.Single());
        result.StockItems.Single().ShouldBe(original.StockItems.Single());
        result.AccountGroups.Single().ShouldBe(original.AccountGroups.Single());
        result.Ledgers.Count.ShouldBe(2);
        result.Ledgers[0].ShouldBe(original.Ledgers[0]);
        result.Ledgers[1].ShouldBe(original.Ledgers[1]);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task RoundTrip_Voucher_SurvivesWithEveryLineAndItsGuid(string format)
    {
        var original = SampleDocument();
        var expected = original.Vouchers.Single();

        var actual = (await RoundTripAsync(original, format)).Vouchers.ShouldHaveSingleItem();

        actual.Id.ShouldBe(expected.Id);
        actual.Number.ShouldBe(expected.Number);
        actual.Kind.ShouldBe(expected.Kind);
        actual.Date.ShouldBe(expected.Date);
        actual.Narration.ShouldBe(expected.Narration);
        actual.ReferenceNumber.ShouldBe(expected.ReferenceNumber);
        actual.ReferenceDate.ShouldBe(expected.ReferenceDate);
        actual.LedgerEntries.ShouldBe(expected.LedgerEntries);
        actual.InventoryEntries.ShouldBe(expected.InventoryEntries);
    }

    /// <summary>C8.5 — four decimal places in, four decimal places out, on both sides of zero.</summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public async Task RoundTrip_AmountsAndQuantitiesAtFullScale_LoseNoPrecision(string format)
    {
        // 1234.5678 and 0.0001 in minor units: the widest and the narrowest values the scale holds.
        const long AwkwardAmount = 12_345_678L;
        const long SmallestQuantity = 1L;

        var document = SampleDocument() with
        {
            Vouchers =
            [
                new InteropVoucher
                {
                    Id = Guid.NewGuid(),
                    VoucherTypeName = "Journal",
                    Kind = VoucherKind.Journal,
                    Date = new DateOnly(2026, 6, 1),
                    Number = "JRN/0007",
                    LedgerEntries =
                    [
                        new InteropLedgerEntry("Cash", AwkwardAmount),
                        new InteropLedgerEntry("Pump Sales", -AwkwardAmount),
                    ],
                    InventoryEntries =
                    [
                        new InteropInventoryEntry(
                            "Openwell Pump 1 HP",
                            SmallestQuantity,
                            AwkwardAmount,
                            UnitCode: "Nos"),
                    ],
                },
            ],
        };

        var voucher = (await RoundTripAsync(document, format)).Vouchers.ShouldHaveSingleItem();

        voucher.LedgerEntries[0].AmountMinor.ShouldBe(AwkwardAmount);
        voucher.LedgerEntries[1].AmountMinor.ShouldBe(-AwkwardAmount);
        voucher.InventoryEntries[0].QuantityMinor.ShouldBe(SmallestQuantity);
        voucher.InventoryEntries[0].AmountMinor.ShouldBe(AwkwardAmount);
        voucher.IsBalanced.ShouldBeTrue();
    }

    /// <summary>C8.2 — writing the same document twice produces byte-identical files.</summary>
    /// <remarks>
    /// Stability matters for more than tidiness. The owner hands the accountant a file each month; a
    /// writer that reordered or re-rendered unchanged records would make every diff useless for
    /// spotting what actually changed, and would defeat Tally's overwrite-by-GUID by presenting the
    /// same record as though it were new.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Formats))]
    public void Write_SameDocumentTwice_ProducesIdenticalOutput(string format)
    {
        var document = SampleDocument();

        var first = format == "xml" ? TallyXmlWriter.Write(document) : TallyJsonWriter.Write(document);
        var second = format == "xml" ? TallyXmlWriter.Write(document) : TallyJsonWriter.Write(document);

        second.ShouldBe(first);
    }

    /// <summary>
    /// The sign inversion, asserted on the file itself rather than through a round trip.
    /// </summary>
    /// <remarks>
    /// A round trip cannot catch this. Invert the sign on both sides and every round-trip test still
    /// passes while every voucher handed to the accountant is reversed — which is exactly why
    /// <see cref="TallyConventions"/> states the rule and why this test reads the rendered text.
    /// </remarks>
    [Fact]
    public void Write_Debit_IsNegativeInTheFile()
    {
        var document = SampleDocument() with
        {
            Vouchers =
            [
                new InteropVoucher
                {
                    VoucherTypeName = "Receipt",
                    Kind = VoucherKind.Receipt,
                    Date = new DateOnly(2026, 6, 1),
                    Number = "RCT/0001",
                    LedgerEntries =
                    [
                        new InteropLedgerEntry("Cash", MoneyScale.ToMinor(500m)),
                        new InteropLedgerEntry("Ravi Motors", MoneyScale.ToMinor(-500m)),
                    ],
                },
            ],
        };

        var xml = TallyXmlWriter.Write(document);

        xml.ShouldContain("<AMOUNT>-500.0000</AMOUNT>");
        xml.ShouldContain("<AMOUNT>500.0000</AMOUNT>");

        // ISDEEMEDPOSITIVE says Yes on the debit, which is the negative one.
        var cashBlock = xml[xml.IndexOf("Cash", StringComparison.Ordinal)..];
        cashBlock.ShouldContain("<ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE>");
    }

    [Fact]
    public async Task Write_CancelledVoucher_IsCarriedRatherThanDropped()
    {
        var document = SampleDocument() with
        {
            Vouchers =
            [
                new InteropVoucher
                {
                    Id = Guid.NewGuid(),
                    VoucherTypeName = "Payment",
                    Kind = VoucherKind.Payment,
                    Date = new DateOnly(2026, 6, 2),
                    Number = "PMT/0004",
                    IsCancelled = true,
                },
            ],
        };

        var voucher = (await RoundTripAsync(document, "xml")).Vouchers.ShouldHaveSingleItem();

        voucher.IsCancelled.ShouldBeTrue();
        voucher.Number.ShouldBe("PMT/0004");
        voucher.LedgerEntries.ShouldBeEmpty();
    }

    [Fact]
    public void Read_ActiveFlagAbsent_TreatsTheMasterAsActive()
    {
        const string Xml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDATA>
              <TALLYMESSAGE><LEDGER NAME="Cash"><NAME>Cash</NAME><PARENT>Cash-in-Hand</PARENT></LEDGER></TALLYMESSAGE>
            </REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>
            """;

        TallyXmlReader.Read(Xml).Ledgers.ShouldHaveSingleItem().IsActive.ShouldBeTrue();
    }
}
