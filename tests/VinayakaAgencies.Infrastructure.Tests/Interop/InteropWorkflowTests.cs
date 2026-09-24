using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

namespace VinayakaAgencies.Infrastructure.Tests.Interop;

/// <summary>
/// Phase 8 end to end against real SQLite files: export the books, read the file back, and check
/// what the books did.
/// </summary>
/// <remarks>
/// <para>
/// The migration checkpoints are here rather than in the Application suite because they are claims
/// about the database, not about the model. C8.2 is "a re-import does not duplicate", which only
/// means something against rows; C8.4 is "commits nothing", which only means something if there was
/// a transaction to roll back; C8.6 compares two readings of the trial balance.
/// </para>
/// <para>
/// Where a test needs a second company it uses a second host — a genuinely separate database file.
/// Importing back into the same company proves idempotence; importing into a database that has never
/// seen these records is the actual migration, and the two fail in different ways.
/// </para>
/// </remarks>
public sealed class InteropWorkflowTests
{
    private static readonly DateOnly InFy2026 = new(2026, 4, 15);

    // ---------------------------------------------------------------- C8.2 — the round trip

    [Theory]
    [InlineData(InteropFormat.TallyXml)]
    [InlineData(InteropFormat.TallyJson)]
    public async Task Import_OfThisCompanysOwnExport_ChangesNothing(InteropFormat format)
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        await PostJournalAsync(host, world, 1_500m);

        var before = await CensusAsync(host);
        var file = await ExportAsync(host, format);

        var result = await ImportAsync(host, file, format);

        result.Committed.ShouldBeTrue(Because(result));
        result.Preview.Problems.ShouldBeEmpty();

        var after = await CensusAsync(host);
        after.ShouldBe(before);
    }

    /// <summary>
    /// The second import reports everything as an update, nothing as a creation.
    /// </summary>
    /// <remarks>
    /// The census test above proves no rows appeared. This one proves the importer <i>knew</i> they
    /// would not: a file whose records it believes are all new, that happens to leave the row count
    /// alone because every write collided with a unique index, would pass the census and be one
    /// schema change away from duplicating the accountant's books.
    /// </remarks>
    [Fact]
    public async Task Import_OfThisCompanysOwnExport_CountsEverythingAsAnUpdate()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        await PostJournalAsync(host, world, 900m);

        var file = await ExportAsync(host, InteropFormat.TallyXml);
        var preview = await ValidateAsync(host, file, InteropFormat.TallyXml);

        preview.RecordCount.ShouldBeGreaterThan(0);
        preview.CreatedCount.ShouldBe(0);
        preview.UpdatedCount.ShouldBe(preview.RecordCount);
    }

    /// <summary>A re-import of an unchanged voucher does not write a revision.</summary>
    /// <remarks>
    /// Altering is how a changed voucher is brought up to date, and it appends a revision (ADR-0002).
    /// Doing that unconditionally would mean every re-run of a migration file added a revision to
    /// every voucher in the books and filled the edit log with changes that changed nothing.
    /// </remarks>
    [Fact]
    public async Task Import_OfAnUnchangedVoucher_AddsNoRevision()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        var posted = await PostJournalAsync(host, world, 640m);

        var file = await ExportAsync(host, InteropFormat.TallyXml);
        await ImportAsync(host, file, InteropFormat.TallyXml);

        var current = await host.InScopeAsync(sp => sp
            .GetRequiredService<IVoucherPostingService>()
            .GetCurrentAsync(posted.VoucherId));

        current!.RevisionNumber.ShouldBe(posted.RevisionNumber);
    }

    // ------------------------------------------------- C8.1 / C8.6 — the migration into a new book

    [Fact]
    public async Task Import_IntoACompanyThatHasNeverSeenTheData_ReproducesTheBooks()
    {
        await using var origin = new SqliteTestHost();
        await origin.MigrateAsync();
        var world = await ArrangeAsync(origin);
        var item = await CreateItemAsync(origin, world, "Openwell Pump 1 HP");
        await PostReceiptAsync(origin, world, item, quantity: 12m, cost: 4_250m);
        await PostJournalAsync(origin, world, 7_800m);

        var file = await ExportAsync(origin, InteropFormat.TallyXml);
        var expected = await TrialBalanceTotalsAsync(origin);
        var expectedStock = await StockValueAsync(origin);

        await using var target = new SqliteTestHost();
        await target.MigrateAsync();
        await ArrangeAsync(target);

        var result = await ImportAsync(target, file, InteropFormat.TallyXml);

        result.Committed.ShouldBeTrue(Because(result));

        // C8.6: the books this import produced read the same as the books it came from.
        var actual = await TrialBalanceTotalsAsync(target);
        actual.ShouldBe(expected);
        (await StockValueAsync(target)).ShouldBe(expectedStock);

        // And the reconciliation the import itself performed agrees.
        result.Reconciliation.ShouldNotBeNull();
        result.Reconciliation!.IsClean.ShouldBeTrue(result.Reconciliation.Describe());
        result.Reconciliation.HasDeclaration.ShouldBeFalse();
    }

    [Fact]
    public async Task Import_IntoANewCompany_CreatesTheMastersItNames()
    {
        await using var origin = new SqliteTestHost();
        await origin.MigrateAsync();
        var world = await ArrangeAsync(origin);
        await CreateLedgerAsync(origin, world, "Ravi Motors", "Sundry Debtors");
        await CreateItemAsync(origin, world, "Submersible 2 HP");

        var file = await ExportAsync(origin, InteropFormat.TallyXml);

        await using var target = new SqliteTestHost();
        await target.MigrateAsync();
        await ArrangeAsync(target);

        var result = await ImportAsync(target, file, InteropFormat.TallyXml);
        result.Committed.ShouldBeTrue(Because(result));

        var ledgers = await LedgerNamesAsync(target);
        ledgers.ShouldContain("Ravi Motors");

        var items = await ItemNamesAsync(target);
        items.ShouldContain("Submersible 2 HP");
    }

    // ---------------------------------------------------------------- C8.5 — precision

    [Fact]
    public async Task Import_AmountsAtFullScale_ReachTheBooksExactly()
    {
        await using var origin = new SqliteTestHost();
        await origin.MigrateAsync();
        var world = await ArrangeAsync(origin);

        // 1234.5678 — four decimal places, the widest the scale holds.
        await PostJournalAsync(origin, world, 1_234.5678m);

        var file = await ExportAsync(origin, InteropFormat.TallyXml);

        await using var target = new SqliteTestHost();
        await target.MigrateAsync();
        await ArrangeAsync(target);
        await ImportAsync(target, file, InteropFormat.TallyXml);

        var totals = await TrialBalanceTotalsAsync(target);
        totals.DebitMinor.ShouldBe(MoneyScale.ToMinor(1_234.5678m));
        totals.CreditMinor.ShouldBe(MoneyScale.ToMinor(1_234.5678m));
    }

    // ---------------------------------------------------------------- C8.4 — one bad row, no writes

    [Fact]
    public async Task Import_WithOneUnbalancedVoucher_CommitsNothingAndNamesTheRow()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var before = await CensusAsync(host);

        var file = Wrap(
            Message("<LEDGER><NAME>Freight Inward</NAME><PARENT>Direct Expenses</PARENT></LEDGER>")
            + Message("""
                <VOUCHER VCHTYPE="Journal">
                  <VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>
                  <DATE>20260415</DATE>
                  <VOUCHERNUMBER>JRN/9001</VOUCHERNUMBER>
                  <ALLLEDGERENTRIES.LIST><LEDGERNAME>Cash</LEDGERNAME><AMOUNT>-100.0000</AMOUNT></ALLLEDGERENTRIES.LIST>
                  <ALLLEDGERENTRIES.LIST><LEDGERNAME>Sales</LEDGERNAME><AMOUNT>90.0000</AMOUNT></ALLLEDGERENTRIES.LIST>
                </VOUCHER>
                """));

        var result = await ImportAsync(host, file, InteropFormat.TallyXml);

        result.Committed.ShouldBeFalse();
        result.RefusalReason.ShouldNotBeNull();

        var problem = result.Preview.Problems.ShouldHaveSingleItem();
        problem.RecordLabel.ShouldBe("JRN/9001");
        problem.Reason.ShouldContain("differ by");
        problem.Location.Description.ShouldStartWith("line ");

        // The valid ledger sitting above the bad voucher did not go in either. One problem stops the
        // whole file — a half-imported migration is the state there is no way back from.
        (await CensusAsync(host)).ShouldBe(before);
        (await LedgerNamesAsync(host)).ShouldNotContain("Freight Inward");
    }

    [Fact]
    public async Task Import_NamingALedgerThatDoesNotExist_ReportsItRatherThanCreatingOne()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var file = Wrap(Message("""
            <VOUCHER VCHTYPE="Journal">
              <VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>
              <DATE>20260415</DATE>
              <VOUCHERNUMBER>JRN/9002</VOUCHERNUMBER>
              <ALLLEDGERENTRIES.LIST><LEDGERNAME>Nobody</LEDGERNAME><AMOUNT>-100.0000</AMOUNT></ALLLEDGERENTRIES.LIST>
              <ALLLEDGERENTRIES.LIST><LEDGERNAME>Sales</LEDGERNAME><AMOUNT>100.0000</AMOUNT></ALLLEDGERENTRIES.LIST>
            </VOUCHER>
            """));

        var result = await ImportAsync(host, file, InteropFormat.TallyXml);

        result.Committed.ShouldBeFalse();
        result.Preview.Problems.ShouldHaveSingleItem().Reason.ShouldContain("'Nobody'");
    }

    [Fact]
    public async Task Import_ReportsEveryProblemInOnePass()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var file = Wrap(
            Message("<STOCKITEM><NAME>Ghost Pump</NAME><BASEUNITS>Furlongs</BASEUNITS></STOCKITEM>")
            + Message("<LEDGER><NAME>Odd One</NAME><PARENT>No Such Group</PARENT></LEDGER>"));

        var result = await ImportAsync(host, file, InteropFormat.TallyXml);

        // Both, not just the first. A migration is fixed by working through a list (§4a D2); handing
        // the owner one problem per run turns that into twenty runs.
        result.Preview.Problems.Count.ShouldBe(2);
        result.Preview.Problems[0].Reason.ShouldContain("Furlongs");
        result.Preview.Problems[1].Reason.ShouldContain("No Such Group");
    }

    // ---------------------------------------------------------------- masters created mid-file

    [Fact]
    public async Task Import_MasterCreatedEarlierInTheSameFile_SatisfiesALaterReference()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeAsync(host);

        var file = Wrap(
            Message("<UNIT><NAME>Ltr</NAME><DECIMALPLACES>2</DECIMALPLACES></UNIT>")
            + Message("<STOCKITEM><NAME>Lubricant</NAME><BASEUNITS>Ltr</BASEUNITS></STOCKITEM>"));

        var result = await ImportAsync(host, file, InteropFormat.TallyXml);

        result.Committed.ShouldBeTrue(Because(result));
        (await ItemNamesAsync(host)).ShouldContain("Lubricant");
    }

    [Fact]
    public async Task Import_SerialTrackedItem_WarnsWithoutBlocking()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);
        var item = await CreateItemAsync(host, world, "Traced Pump", isSerialTracked: true);

        var file = Wrap(Message($"""
            <VOUCHER VCHTYPE="Journal">
              <VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>
              <DATE>20260415</DATE>
              <VOUCHERNUMBER>JRN/9003</VOUCHERNUMBER>
              <ALLLEDGERENTRIES.LIST><LEDGERNAME>Cash</LEDGERNAME><AMOUNT>-100.0000</AMOUNT></ALLLEDGERENTRIES.LIST>
              <ALLLEDGERENTRIES.LIST><LEDGERNAME>Sales</LEDGERNAME><AMOUNT>100.0000</AMOUNT></ALLLEDGERENTRIES.LIST>
              <ALLINVENTORYENTRIES.LIST>
                <STOCKITEMNAME>{item.Name}</STOCKITEMNAME>
                <ACTUALQTY>1.0000 NOS</ACTUALQTY>
                <RATE>100.0000</RATE>
                <AMOUNT>-100.0000</AMOUNT>
              </ALLINVENTORYENTRIES.LIST>
            </VOUCHER>
            """));

        var result = await ImportAsync(host, file, InteropFormat.TallyXml);

        result.Committed.ShouldBeTrue(Because(result));
        result.Preview.Warnings.ShouldHaveSingleItem().Note.ShouldContain("serial-tracked");
    }

    // ---------------------------------------------------------------- helpers

    private sealed record InteropWorld(Guid CompanyId, Guid UnitId, Guid GodownId, Guid CashLedgerId, Guid SalesLedgerId);

    private sealed record ItemProxy(Guid Id, string Name);

    private sealed record Census(int Ledgers, int Groups, int Items, int Units, int Godowns, int Vouchers, int Revisions, int JournalLines);

    private sealed record Totals(long DebitMinor, long CreditMinor);

    private const string Prologue = "<ENVELOPE><BODY><IMPORTDATA><REQUESTDATA>";
    private const string Epilogue = "</REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>";

    /// <summary>The refusal plus every problem behind it, so a failing test says why rather than that.</summary>
    private static string Because(InteropImportResult result) =>
        string.Join(Environment.NewLine, [result.RefusalReason ?? "committed", .. result.Preview.Problems.Select(p => p.ToString())]);

    private static string Wrap(string messages) => $"{Prologue}\n{messages}\n{Epilogue}";

    private static string Message(string content) => $"<TALLYMESSAGE>{content}</TALLYMESSAGE>\n";

    private static async Task<InteropWorld> ArrangeAsync(SqliteTestHost host)
    {
        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();
            return company.Id;
        });

        await host.InScopeAsync(sp => sp.GetRequiredService<ICompanyProvisioningService>().ProvisionAsync(companyId));
        await host.InScopeAsync(sp => sp.GetRequiredService<ICompanyProvisioningService>().EnsureInventorySeedAsync(companyId));

        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var unit = (await unitOfWork.Repository<UnitOfMeasure>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Code == "NOS");
            var godown = (await unitOfWork.Repository<Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId);
            var ledgers = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Where(candidate => candidate.CompanyId == companyId)
                .ToList();

            return new InteropWorld(
                companyId,
                unit.Id,
                godown.Id,
                ledgers.Single(l => l.Name == "Cash").Id,
                ledgers.Single(l => l.Name == "Sales").Id);
        });
    }

    private static Task<PostedVoucherSummary> PostJournalAsync(SqliteTestHost host, InteropWorld world, decimal amount) =>
        host.InScopeAsync(async sp =>
        {
            var posting = sp.GetRequiredService<IVoucherPostingService>();
            var types = await posting.ListVoucherTypesAsync();
            var journal = types.Single(type => type.Name == "Journal");

            return await posting.PostAsync(new PostVoucherRequest(
                journal.Id,
                InFy2026,
                [
                    new PostVoucherLine(world.CashLedgerId, amount, 0m),
                    new PostVoucherLine(world.SalesLedgerId, 0m, amount),
                ],
                Narration: "counter sale"));
        });

    private static Task<ItemProxy> CreateItemAsync(
        SqliteTestHost host,
        InteropWorld world,
        string name,
        bool isSerialTracked = false) =>
        host.InScopeAsync(async sp =>
        {
            var saved = await sp.GetRequiredService<IInventoryMasterService>()
                .SaveItemAsync(new SaveItemRequest(
                    Id: null,
                    name,
                    world.UnitId,
                    IsSerialTracked: isSerialTracked));

            return new ItemProxy(saved.Id, saved.Name);
        });

    private static Task CreateLedgerAsync(SqliteTestHost host, InteropWorld world, string name, string groupName) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var group = (await unitOfWork.Repository<AccountGroup>().ListAsync())
                .Single(candidate => candidate.CompanyId == world.CompanyId && candidate.Name == groupName);

            await unitOfWork.Repository<Ledger>().AddAsync(new Ledger(world.CompanyId, name, group.Id));
            await unitOfWork.SaveChangesAsync();
        });

    private static Task<PostedStockVoucherSummary> PostReceiptAsync(
        SqliteTestHost host,
        InteropWorld world,
        ItemProxy item,
        decimal quantity,
        decimal cost) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        item.Id,
                        world.GodownId,
                        quantity,
                        cost),
                ],
                Narration: "goods in",
                CounterLedgerId: world.CashLedgerId)));

    private static Task<string> ExportAsync(SqliteTestHost host, InteropFormat format) =>
        host.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IInteropExportService>();
            var document = await service.BuildAsync(new InteropExportRequest());

            using var stream = new MemoryStream();
            await service.WriteAsync(document, format, stream);

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        });

    private static IInteropSource SourceFor(string text, InteropFormat format) =>
        format == InteropFormat.TallyJson
            ? TallyJsonSource.FromText(text, "export.json")
            : TallyXmlSource.FromText(text, "export.xml");

    private static Task<InteropImportResult> ImportAsync(SqliteTestHost host, string text, InteropFormat format) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IInteropImportService>()
            .CommitAsync(SourceFor(text, format), InteropImportOptions.Everything()));

    private static Task<InteropImportPreview> ValidateAsync(SqliteTestHost host, string text, InteropFormat format) =>
        host.InScopeAsync(sp => sp
            .GetRequiredService<IInteropImportService>()
            .ValidateAsync(SourceFor(text, format), InteropImportOptions.Everything()));

    private static Task<Census> CensusAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            return new Census(
                await unitOfWork.Repository<Ledger>().CountAsync(),
                await unitOfWork.Repository<AccountGroup>().CountAsync(),
                await unitOfWork.Repository<Item>().CountAsync(),
                await unitOfWork.Repository<UnitOfMeasure>().CountAsync(),
                await unitOfWork.Repository<Godown>().CountAsync(),
                await unitOfWork.Repository<Voucher>().CountAsync(),
                await unitOfWork.Repository<VoucherRevision>().CountAsync(),
                await unitOfWork.Repository<JournalLine>().CountAsync());
        });

    private static Task<Totals> TrialBalanceTotalsAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
        {
            // Inside the data's own financial year: a trial balance is scoped to the year containing
            // the date it is asked for, so a far-future date would report an empty year.
            var trialBalance = await sp.GetRequiredService<IAccountingReportService>()
                .GetTrialBalanceAsync(InFy2026);

            return new Totals(
                MoneyScale.ToMinor(trialBalance.TotalDebit),
                MoneyScale.ToMinor(trialBalance.TotalCredit));
        });

    private static Task<long> StockValueAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IStockReportService>()
                .ReconcileValuationAsync(new DateOnly(9998, 12, 31))).TotalStockValueMinor);

    private static Task<IReadOnlyList<string>> LedgerNamesAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
            (IReadOnlyList<string>)(await sp.GetRequiredService<IUnitOfWork>()
                .Repository<Ledger>().ListAsync()).Select(l => l.Name).ToList());

    private static Task<IReadOnlyList<string>> ItemNamesAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
            (IReadOnlyList<string>)(await sp.GetRequiredService<IUnitOfWork>()
                .Repository<Item>().ListAsync()).Select(i => i.Name).ToList());
}
