using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Accounting;
using Shouldly;

namespace VinayakaAgencies.Application.Tests.Interop;

/// <summary>
/// What the sources do with input they cannot use.
/// </summary>
/// <remarks>
/// The rule these all test is one rule: a source never throws on bad data, it yields
/// <see cref="InteropRecord.Unreadable"/>. §2 requires the whole file to be validated before
/// anything is written and C8.4 requires the offending row and the reason — neither is possible if
/// the first bad element aborts the read, because the owner then fixes one problem per run through a
/// file that may have twenty.
/// </remarks>
public sealed class InteropSourceTests
{
    private const string Prologue = "<ENVELOPE><BODY><IMPORTDATA><REQUESTDATA>";
    private const string Epilogue = "</REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>";

    private static string Wrap(string messages) => Prologue + messages + Epilogue;

    private static string Message(string content) => $"<TALLYMESSAGE>{content}</TALLYMESSAGE>";

    [Fact]
    public async Task ReadAsync_MalformedXml_ReportsItRatherThanThrowing()
    {
        var source = TallyXmlSource.FromText(Prologue + "<TALLYMESSAGE><LEDGER>");

        var records = await source.ReadAllAsync();

        var unreadable = records.OfType<InteropRecord.Unreadable>().ShouldHaveSingleItem();
        unreadable.Label.ShouldContain("not valid XML");
    }

    [Fact]
    public async Task ReadAsync_MalformedJson_ReportsItRatherThanThrowing()
    {
        var source = TallyJsonSource.FromText("{ \"ENVELOPE\": ");

        var records = await source.ReadAllAsync();

        var unreadable = records.OfType<InteropRecord.Unreadable>().ShouldHaveSingleItem();
        unreadable.Label.ShouldContain("not valid JSON");
    }

    [Fact]
    public async Task ReadAsync_JsonWithoutAnEnvelope_SaysSo()
    {
        var records = await TallyJsonSource.FromText("{ \"something\": 1 }").ReadAllAsync();

        records.OfType<InteropRecord.Unreadable>().ShouldHaveSingleItem()
            .Label.ShouldContain("not a Tally export");
    }

    /// <summary>
    /// A bad record does not stop the ones after it.
    /// </summary>
    /// <remarks>
    /// This is the property C8.4's "reports the offending row" depends on. A reader that gave up at
    /// the first problem would satisfy the letter of the criterion and be useless for the migration
    /// §4a D2 describes, where the file is fixed and re-run several times.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_OneBadRecord_StillYieldsTheGoodOnesAroundIt()
    {
        var xml = Wrap(
            Message("<LEDGER><NAME>Cash</NAME><PARENT>Cash-in-Hand</PARENT></LEDGER>")
            + Message("<LEDGER><NAME>Orphan</NAME></LEDGER>")
            + Message("<LEDGER><NAME>Bank</NAME><PARENT>Bank Accounts</PARENT></LEDGER>"));

        var records = await TallyXmlSource.FromText(xml).ReadAllAsync();

        records.Count.ShouldBe(3);
        records.OfType<InteropRecord.Ledger>().Select(r => r.Value.Name).ShouldBe(["Cash", "Bank"]);
        records[1].ShouldBeOfType<InteropRecord.Unreadable>().Label.ShouldContain("names no group");
    }

    [Fact]
    public async Task ReadAsync_EveryRecord_CarriesALineNumber()
    {
        var xml = $"{Prologue}\n{Message("<LEDGER><NAME>Cash</NAME><PARENT>Cash-in-Hand</PARENT></LEDGER>")}\n{Epilogue}";

        var records = await TallyXmlSource.FromText(xml).ReadAllAsync();

        var record = records.ShouldHaveSingleItem();
        record.Location.Ordinal.ShouldBe(1);
        record.Location.Description.ShouldStartWith("line ");
    }

    [Fact]
    public async Task ReadAsync_UnknownElement_IsReportedNotSkipped()
    {
        var records = await TallyXmlSource.FromText(Wrap(Message("<COSTCENTRE><NAME>Depot</NAME></COSTCENTRE>"))).ReadAllAsync();

        records.ShouldHaveSingleItem()
            .ShouldBeOfType<InteropRecord.Unreadable>()
            .Label.ShouldContain("not a master or voucher");
    }

    /// <summary>
    /// A voucher type the application does not post is reported, never guessed at.
    /// </summary>
    /// <remarks>
    /// <see cref="TallyConventions.ParseVoucherKind"/> returns null rather than defaulting because a
    /// Tally company routinely carries user-defined voucher types. Guessing "Bank Payment" is a
    /// payment happens to be right; guessing wrong posts real money through the wrong path, and
    /// nothing in the file would show it had happened.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_UnrecognisedVoucherType_IsReported()
    {
        var xml = Wrap(Message(
            "<VOUCHER VCHTYPE=\"Stock Journal\"><VOUCHERTYPENAME>Stock Journal</VOUCHERTYPENAME>"
            + "<DATE>20260601</DATE><VOUCHERNUMBER>SJ/1</VOUCHERNUMBER></VOUCHER>"));

        var records = await TallyXmlSource.FromText(xml).ReadAllAsync();

        records.ShouldHaveSingleItem()
            .ShouldBeOfType<InteropRecord.Unreadable>()
            .Label.ShouldContain("Stock Journal");
    }

    [Fact]
    public async Task ReadAsync_GroupWithNeitherParentNorNature_IsReported()
    {
        var records = await TallyXmlSource.FromText(Wrap(Message("<GROUP><NAME>Odds</NAME></GROUP>"))).ReadAllAsync();

        records.ShouldHaveSingleItem()
            .ShouldBeOfType<InteropRecord.Unreadable>()
            .Label.ShouldContain("cannot be placed");
    }

    [Fact]
    public async Task ReadAsync_CalledTwice_ReadsTheWholeSourceEachTime()
    {
        var source = TallyXmlSource.FromText(Wrap(Message("<UNIT><NAME>Nos</NAME></UNIT>")));

        var first = await source.ReadAllAsync();
        var second = await source.ReadAllAsync();

        // The importer validates on one pass and commits on another. A source that could only be
        // read once would silently commit nothing (§4a D2).
        first.Count.ShouldBe(1);
        second.Count.ShouldBe(1);
    }

    [Fact]
    public void ParentsFirst_Children_ComeAfterTheirParents()
    {
        InteropAccountGroup Group(string name, string? parent) =>
            new(name, parent, AccountNature.Expense);

        IReadOnlyCollection<InteropAccountGroup> unsorted =
        [
            Group("Freight Inward", "Direct Expenses"),
            Group("Direct Expenses", "Expenses"),
            Group("Expenses", null),
        ];

        var ordered = InteropOrdering.ParentsFirst(unsorted).Select(g => g.Name).ToList();

        ordered.ShouldBe(["Expenses", "Direct Expenses", "Freight Inward"]);
    }

    [Fact]
    public void ParentsFirst_ParentNotInTheFile_TreatsTheChildAsARoot()
    {
        IReadOnlyCollection<InteropAccountGroup> groups =
        [
            new("Pump Sales", "Sales Accounts", AccountNature.Income),
        ];

        // "Sales Accounts" is one of Tally's own primary groups and is deliberately not exported,
        // so a child naming it has no parent row here and must still be emitted.
        InteropOrdering.ParentsFirst(groups).ShouldHaveSingleItem().Name.ShouldBe("Pump Sales");
    }

    [Fact]
    public void ParentsFirst_ACycle_KeepsEveryRowRatherThanDroppingIt()
    {
        IReadOnlyCollection<InteropAccountGroup> groups =
        [
            new("A", "B", AccountNature.Asset),
            new("B", "A", AccountNature.Asset),
        ];

        InteropOrdering.ParentsFirst(groups).Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["A", "B"]);
    }
}
