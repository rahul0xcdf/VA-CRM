using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Interop;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

namespace VinayakaAgencies.Infrastructure.Tests.Interop;

/// <summary>
/// The ClosedXML adapters and the Excel import, against real <c>.xlsx</c> files on disk.
/// </summary>
/// <remarks>
/// The Application suite tests the mapping rules against an in-memory grid, which is the right place
/// for them. What it cannot test is the part this covers: that Excel's own typing — a date stored as
/// a serial number, a currency stored as a double — comes back as text the importer can read. A fake
/// reader returns whatever strings the test wrote into it and would prove nothing about that.
/// </remarks>
public sealed class ExcelWorkbookTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "VinayakaAgencies.Tests",
        $"xlsx-{Guid.NewGuid():N}");

    public ExcelWorkbookTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked handle should not fail an otherwise passing run.
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    /// <summary>Writes a sheet through the real writer, so the reader is reading a real workbook.</summary>
    private async Task<string> WriteSheetAsync(
        SqliteTestHost host,
        string fileName,
        IReadOnlyList<string> headers,
        params IReadOnlyList<string?>[] rows)
    {
        var path = PathFor(fileName);

        await host.InScopeAsync(async sp =>
        {
            await using var stream = File.Create(path);

            await sp.GetRequiredService<IWorkbookWriter>().WriteAsync(
                new WorkbookDocument([new WorkbookSheet("Data", headers, rows)]),
                stream);
        });

        return path;
    }

    [Fact]
    public async Task ReadRows_AfterWritingASheet_ReturnsWhatWasWritten()
    {
        await using var host = new SqliteTestHost();

        var path = await WriteSheetAsync(
            host,
            "round-trip.xlsx",
            ["Item", "Unit", "Rate"],
            ["Openwell Pump", "Nos", "4250.5678"],
            ["Submersible", "Nos", "9100.0000"]);

        var rows = await host.InScopeAsync(async sp =>
        {
            var reader = sp.GetRequiredService<IWorkbookReader>();
            var collected = new List<WorkbookRow>();

            await foreach (var row in reader.ReadRowsAsync(path))
            {
                collected.Add(row);
            }

            return collected;
        });

        rows.Count.ShouldBe(3);
        rows[0].Cells[0].ShouldBe("Item");
        rows[1].Cells[0].ShouldBe("Openwell Pump");

        // Written as text and read back as text: exact to the fourth place, which is what C8.5 asks
        // of every path amounts travel down.
        rows[1].Cells[2].ShouldBe("4250.5678");
    }

    [Fact]
    public async Task ListSheets_NamesEverySheetInTheFile()
    {
        await using var host = new SqliteTestHost();
        var path = PathFor("two-sheets.xlsx");

        await host.InScopeAsync(async sp =>
        {
            await using var stream = File.Create(path);

            await sp.GetRequiredService<IWorkbookWriter>().WriteAsync(
                new WorkbookDocument(
                [
                    new WorkbookSheet("Items", ["Name"], [["Pump"]]),
                    new WorkbookSheet("Ledgers", ["Name"], [["Cash"]]),
                ]),
                stream);
        });

        var sheets = await host.InScopeAsync(sp => sp.GetRequiredService<IWorkbookReader>().ListSheetsAsync(path));

        sheets.ShouldBe(["Items", "Ledgers"]);
    }

    /// <summary>C8.3, end to end: a sheet in the supplier's column order reaches the item master.</summary>
    [Fact]
    public async Task Import_ItemSheetInANonStandardColumnOrder_ReachesTheItemMaster()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeCompanyAsync(host);

        var path = await WriteSheetAsync(
            host,
            "supplier-prices.xlsx",
            ["Warranty", "UOM", "HSN Code", "Part No", "Description of Goods"],
            ["24 months", "NOS", "8413", "OW-1HP", "Openwell Pump 1 HP"],
            ["12 months", "NOS", "8413", "SUB-2HP", "Submersible 2 HP"]);

        var mappingId = await SaveMappingAsync(host, ExcelImportTarget.Items,
        [
            new ExcelColumnMappingLine("Description of Goods", InteropFieldCatalogue.Name),
            new ExcelColumnMappingLine("UOM", InteropFieldCatalogue.BaseUnit),
            new ExcelColumnMappingLine("Part No", InteropFieldCatalogue.Code),
            new ExcelColumnMappingLine("HSN Code", InteropFieldCatalogue.HsnSac),
        ]);

        var result = await host.InScopeAsync(async sp =>
        {
            var source = await sp.GetRequiredService<IInteropSourceFactory>()
                .ForWorkbookAsync(path, mappingId);

            return await sp.GetRequiredService<IInteropImportService>()
                .CommitAsync(source, InteropImportOptions.MastersOnly());
        });

        result.Committed.ShouldBeTrue(
            string.Join(Environment.NewLine, result.Preview.Problems.Select(p => p.ToString())));

        var items = await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IUnitOfWork>().Repository<Item>().ListAsync())
                .Select(item => item.Name)
                .ToList());

        items.ShouldContain("Openwell Pump 1 HP");
        items.ShouldContain("Submersible 2 HP");
    }

    /// <summary>
    /// The HSN codes the sheet carried became master rows.
    /// </summary>
    /// <remarks>
    /// Creating them does not breach "nothing tax-related is hardcoded" (CLAUDE.md non-negotiable 6):
    /// the codes came from the owner's own file, and no rate is invented for them.
    /// </remarks>
    [Fact]
    public async Task Import_ItemSheetWithHsnCodes_RecordsThemAsMasters()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeCompanyAsync(host);

        var path = await WriteSheetAsync(
            host,
            "hsn.xlsx",
            ["Item", "Unit", "HSN"],
            ["Openwell Pump 1 HP", "NOS", "8413"]);

        var mappingId = await SaveMappingAsync(host, ExcelImportTarget.Items,
        [
            new ExcelColumnMappingLine("Item", InteropFieldCatalogue.Name),
            new ExcelColumnMappingLine("Unit", InteropFieldCatalogue.BaseUnit),
            new ExcelColumnMappingLine("HSN", InteropFieldCatalogue.HsnSac),
        ]);

        await host.InScopeAsync(async sp =>
        {
            var source = await sp.GetRequiredService<IInteropSourceFactory>().ForWorkbookAsync(path, mappingId);
            await sp.GetRequiredService<IInteropImportService>().CommitAsync(source, InteropImportOptions.MastersOnly());
        });

        var codes = await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IUnitOfWork>().Repository<Domain.Gst.HsnSac>().ListAsync())
                .Select(code => code.Code)
                .ToList());

        codes.ShouldContain("8413");
    }

    [Fact]
    public async Task Suggest_ColumnsNamedCloseToTheirFields_AreMatchedWithoutBeingAsked()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeCompanyAsync(host);

        var path = await WriteSheetAsync(
            host,
            "suggest.xlsx",
            ["Item Name", "Base Unit", "Warranty"],
            ["Openwell Pump", "NOS", "24 months"]);

        var suggestions = await host.InScopeAsync(sp => sp
            .GetRequiredService<IExcelMappingService>()
            .SuggestAsync(path, ExcelImportTarget.Items));

        suggestions.Count.ShouldBe(3);
        suggestions[0].TargetField.ShouldBe(InteropFieldCatalogue.Name);
        suggestions[1].TargetField.ShouldBe(InteropFieldCatalogue.BaseUnit);

        // Nothing sensible to offer for a column this application has no field for, and saying so is
        // better than attaching it to whatever matched least badly.
        suggestions[2].TargetField.ShouldBeNull();
    }

    [Fact]
    public async Task SaveMapping_NamingAFieldTheTargetDoesNotHave_IsRefused()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeCompanyAsync(host);

        await host.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IExcelMappingService>();

            var error = await Should.ThrowAsync<InvalidOperationException>(() => service.SaveAsync(
                new SaveExcelColumnMappingRequest(
                    Id: null,
                    "Wrong",
                    ExcelImportTarget.Items,
                    [new ExcelColumnMappingLine("Amount", InteropFieldCatalogue.Debit)])));

            error.Message.ShouldContain(InteropFieldCatalogue.Debit);
        });
    }

    [Fact]
    public async Task SaveMapping_ThenReload_KeepsItsColumns()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeCompanyAsync(host);

        var id = await SaveMappingAsync(host, ExcelImportTarget.Items,
        [
            new ExcelColumnMappingLine("Description of Goods", InteropFieldCatalogue.Name),
            new ExcelColumnMappingLine("UOM", InteropFieldCatalogue.BaseUnit),
        ]);

        var reloaded = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IExcelMappingService>().GetAsync(id));

        reloaded.ShouldNotBeNull();
        reloaded!.Columns.Count.ShouldBe(2);
        reloaded.Columns.ShouldContain(column => column.SourceHeader == "UOM");
    }

    /// <summary>An exported workbook maps onto itself, so it can be edited and handed straight back.</summary>
    [Fact]
    public async Task Export_ThenImportTheSameWorkbook_NeedsNoHandBuiltMapping()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        await ArrangeCompanyAsync(host);
        await SaveItemAsync(host, "Openwell Pump 1 HP");

        var path = PathFor("export.xlsx");

        await host.InScopeAsync(async sp =>
        {
            var document = await sp.GetRequiredService<IInteropExportService>()
                .BuildAsync(InteropExportRequest.MastersOnly());

            await using var stream = File.Create(path);
            await sp.GetRequiredService<IInteropWorkbookExporter>().WriteAsync(document, stream);
        });

        var suggestions = await host.InScopeAsync(sp => sp
            .GetRequiredService<IExcelMappingService>()
            .SuggestAsync(path, ExcelImportTarget.Items, "Items"));

        // Every column of the sheet we wrote is a field we know, because the export uses the
        // catalogue's own names as its headers.
        suggestions.ShouldAllBe(suggestion => suggestion.TargetField != null);
    }

    private static async Task<Guid> ArrangeCompanyAsync(SqliteTestHost host)
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

        return companyId;
    }

    private static Task SaveItemAsync(SqliteTestHost host, string name) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var unit = (await unitOfWork.Repository<UnitOfMeasure>().ListAsync()).First(u => u.Code == "NOS");

            await sp.GetRequiredService<Application.Inventory.IInventoryMasterService>()
                .SaveItemAsync(new Application.Inventory.SaveItemRequest(null, name, unit.Id));
        });

    private static Task<Guid> SaveMappingAsync(
        SqliteTestHost host,
        ExcelImportTarget target,
        IReadOnlyList<ExcelColumnMappingLine> columns) =>
        host.InScopeAsync(async sp =>
        {
            var saved = await sp.GetRequiredService<IExcelMappingService>().SaveAsync(
                new SaveExcelColumnMappingRequest(null, "Supplier sheet", target, columns, SheetName: "Data"));

            return saved.Id;
        });
}
