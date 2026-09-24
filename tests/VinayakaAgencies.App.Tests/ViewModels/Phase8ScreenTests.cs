using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Interop;
using Shouldly;

namespace VinayakaAgencies.App.Tests.ViewModels;

/// <summary>
/// The Phase 8 screens' own rules, without a window.
/// </summary>
/// <remarks>
/// ADR-0007's sixth rule: if a behaviour has a rule, the rule goes somewhere a unit test can reach.
/// The rules here are the ones that would otherwise only be discovered by pressing a key and finding
/// nothing happened — a bar entry offered before the screen can act on it, a commit allowed while the
/// file still has problems, a mapping line left pointing at a field its target no longer has.
/// </remarks>
public sealed class Phase8ScreenTests
{
    private static InteropExportViewModel Export(out FakeInteropExportService service)
    {
        service = new FakeInteropExportService();
        return new InteropExportViewModel(service, new FakeInteropWorkbookExporter());
    }

    // ---------------------------------------------------------------- export

    /// <summary>A key is declared only where it works (ADR-0007).</summary>
    [Fact]
    public void ExportActionBar_BeforeAnythingIsPreviewed_OffersWriteAsUnavailable()
    {
        var viewModel = Export(out _);

        var write = viewModel.ActionBar.ShouldHaveSingleItem();
        write.Shortcut.ShouldBe(AppShortcuts.Export);
        write.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task ExportActionBar_AfterAPreview_OffersWrite()
    {
        var viewModel = Export(out _);

        await ((IAsyncRelayCommand)viewModel.PreviewCommand).ExecuteAsync(null);

        viewModel.ActionBar.ShouldHaveSingleItem().IsAvailable.ShouldBeTrue();
        viewModel.CanWrite.ShouldBeTrue();
    }

    [Fact]
    public async Task Export_WithNeitherMastersNorVouchers_SaysSoAndReadsNothing()
    {
        var viewModel = Export(out var service);
        viewModel.IncludeMasters = false;
        viewModel.IncludeVouchers = false;

        await ((IAsyncRelayCommand)viewModel.PreviewCommand).ExecuteAsync(null);

        viewModel.Status.ShouldContain("Nothing is selected");
        service.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task Export_WithTheDatesTheWrongWayRound_IsRefusedBeforeReadingTheBooks()
    {
        var viewModel = Export(out var service);
        viewModel.FromDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Unspecified);
        viewModel.ToDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await ((IAsyncRelayCommand)viewModel.PreviewCommand).ExecuteAsync(null);

        viewModel.Status.ShouldContain("must not be after");
        service.LastRequest.ShouldBeNull();
    }

    /// <summary>"Whole book" means no dates reach the service, not a very wide range.</summary>
    [Fact]
    public async Task Export_WithWholeBook_SendsNoDateWindowAtAll()
    {
        var viewModel = Export(out var service);
        viewModel.WholeBook = true;

        await ((IAsyncRelayCommand)viewModel.PreviewCommand).ExecuteAsync(null);

        service.LastRequest.ShouldNotBeNull();
        service.LastRequest!.FromDate.ShouldBeNull();
        service.LastRequest.ToDate.ShouldBeNull();
        viewModel.IsDateRangeEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Export_Clear_TakesTheWriteKeyBackOffTheBar()
    {
        var viewModel = Export(out _);
        await ((IAsyncRelayCommand)viewModel.PreviewCommand).ExecuteAsync(null);

        viewModel.ClearCommand.Execute(null);

        viewModel.CanWrite.ShouldBeFalse();
        viewModel.ActionBar.ShouldHaveSingleItem().IsAvailable.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- import

    private static InteropImportViewModel Import(out FakeInteropImportService import) =>
        new(import = new FakeInteropImportService(), new FakeInteropSourceFactory(), new FakeExcelMappingService());

    [Fact]
    public void ImportActionBar_WithNothingChecked_OffersImportAsUnavailable()
    {
        var viewModel = Import(out _);

        var bar = viewModel.ActionBar;
        bar.Count.ShouldBe(2);
        bar[0].Shortcut.ShouldBe(AppShortcuts.Find);
        bar[0].IsAvailable.ShouldBeTrue();
        bar[1].Shortcut.ShouldBe(AppShortcuts.Accept);
        bar[1].IsAvailable.ShouldBeFalse();
    }

    /// <summary>One problem is enough to keep the key off the bar (§2, C8.4).</summary>
    [Fact]
    public async Task Import_WithAProblemInTheFile_WillNotOfferTheCommit()
    {
        var viewModel = Import(out var import);
        import.Preview = PreviewWith(
            problems:
            [
                new InteropImportProblem(
                    new InteropSourceLocation(1, "line 42"),
                    "Voucher",
                    "JRN/0001",
                    "debits and credits differ by 10.0000."),
            ],
            tallies: [new InteropRecordTally("Voucher", 1, 0)]);

        viewModel.FilePath = "books.xml";
        await ((IAsyncRelayCommand)viewModel.CheckCommand).ExecuteAsync(null);

        viewModel.CanCommit.ShouldBeFalse();
        viewModel.ActionBar[1].IsAvailable.ShouldBeFalse();
        viewModel.Problems.ShouldHaveSingleItem().Where.ShouldBe("line 42");
        viewModel.Status.ShouldContain("Nothing will be written");
    }

    /// <summary>A warning is not a problem: it is shown, and the commit stays available.</summary>
    [Fact]
    public async Task Import_WithOnlyWarnings_StillOffersTheCommit()
    {
        var viewModel = Import(out var import);
        import.Preview = PreviewWith(
            warnings:
            [
                new InteropImportWarning(
                    new InteropSourceLocation(1, "line 7"),
                    "Voucher",
                    "SAL/0001",
                    "'Traced Pump' is serial-tracked, and the file carries no serial numbers."),
            ],
            tallies: [new InteropRecordTally("Voucher", 1, 0)]);

        viewModel.FilePath = "books.xml";
        await ((IAsyncRelayCommand)viewModel.CheckCommand).ExecuteAsync(null);

        viewModel.CanCommit.ShouldBeTrue();
        viewModel.Warnings.ShouldHaveSingleItem().Reason.ShouldContain("serial-tracked");
    }

    [Fact]
    public async Task Import_WithNoFileChosen_SaysSoRatherThanFailing()
    {
        var viewModel = Import(out _);

        await ((IAsyncRelayCommand)viewModel.CheckCommand).ExecuteAsync(null);

        viewModel.Status.ShouldContain("Choose a file first");
    }

    [Fact]
    public async Task Import_OfASpreadsheetWithNoMappingChosen_RefusesAndSaysWhy()
    {
        var viewModel = Import(out _);
        viewModel.FilePath = "prices.xlsx";

        await ((IAsyncRelayCommand)viewModel.CheckCommand).ExecuteAsync(null);

        viewModel.IsWorkbook.ShouldBeTrue();
        viewModel.Status.ShouldContain("column mapping");
    }

    [Theory]
    [InlineData("books.xml", false)]
    [InlineData("books.json", false)]
    [InlineData("prices.xlsx", true)]
    [InlineData("prices.XLSX", true)]
    public void IsWorkbook_FollowsTheExtension(string path, bool expected)
    {
        var viewModel = Import(out _);
        viewModel.FilePath = path;

        viewModel.IsWorkbook.ShouldBe(expected);
    }

    [Fact]
    public async Task Import_WhenItCommits_ShowsTheReconciliation()
    {
        var viewModel = Import(out var import);
        import.Preview = PreviewWith(tallies: [new InteropRecordTally("Ledger", 2, 0)]);
        import.Result = new InteropImportResult(
            import.Preview,
            Committed: true,
            RefusalReason: null,
            new InteropReconciliation(1_000L, 1_000L, 1_000L, 1_000L, 0L, 0L));

        viewModel.FilePath = "books.xml";
        await ((IAsyncRelayCommand)viewModel.CommitCommand).ExecuteAsync(null);

        viewModel.Reconciliation.ShouldContain("moved by exactly");
        viewModel.Status.ShouldContain("Imported");
    }

    /// <summary>
    /// Validation reports a count; the commit reports a proportion.
    /// </summary>
    /// <remarks>
    /// §4a D2 asks the importer to report progress on a full history. The distinction matters because
    /// the total is not knowable until the file has been read — a determinate bar through the
    /// validation pass would be a bar sitting at nothing for the longest part of the work.
    /// </remarks>
    [Fact]
    public async Task Import_WhileChecking_ReportsACountWithNoTotal()
    {
        using var context = new InlineContext();
        var viewModel = Import(out var import);
        import.Preview = PreviewWith(tallies: [new InteropRecordTally("Ledger", 40, 0)]);

        viewModel.FilePath = "books.xml";
        await ((IAsyncRelayCommand)viewModel.CheckCommand).ExecuteAsync(null);

        viewModel.ProgressText.ShouldBe("40 read");
        viewModel.IsProgressDeterminate.ShouldBeFalse();
    }

    [Fact]
    public async Task Import_WhileCommitting_ReportsAProportion()
    {
        using var context = new InlineContext();
        var viewModel = Import(out var import);
        import.Preview = PreviewWith(tallies: [new InteropRecordTally("Ledger", 40, 0)]);

        viewModel.FilePath = "books.xml";
        await ((IAsyncRelayCommand)viewModel.CommitCommand).ExecuteAsync(null);

        viewModel.ProgressText.ShouldBe("40 of 40 records");
        viewModel.IsProgressDeterminate.ShouldBeTrue();
        viewModel.ProgressFraction.ShouldBe(1d);

        // Nothing is running any more, so the bar does not sweep.
        viewModel.IsProgressIndeterminate.ShouldBeFalse();
    }

    [Fact]
    public async Task Import_Clear_TakesTheProgressOffTheStatusLine()
    {
        using var context = new InlineContext();
        var viewModel = Import(out var import);
        import.Preview = PreviewWith(tallies: [new InteropRecordTally("Ledger", 40, 0)]);

        viewModel.FilePath = "books.xml";
        await ((IAsyncRelayCommand)viewModel.CheckCommand).ExecuteAsync(null);
        viewModel.ClearCommand.Execute(null);

        viewModel.ProgressText.ShouldBeEmpty();
        viewModel.ProgressFraction.ShouldBe(0d);
    }

    [Fact]
    public async Task Import_WhenTheBooksDoNotReconcile_SaysSoOnTheStatusLine()
    {
        var viewModel = Import(out var import);
        import.Preview = PreviewWith(tallies: [new InteropRecordTally("Voucher", 1, 0)]);
        import.Result = new InteropImportResult(
            import.Preview,
            Committed: true,
            RefusalReason: null,

            // The file carried ₹100 of debit; the books moved by ₹90.
            new InteropReconciliation(1_000_000L, 1_000_000L, 900_000L, 1_000_000L, 0L, 0L));

        viewModel.FilePath = "books.xml";
        await ((IAsyncRelayCommand)viewModel.CommitCommand).ExecuteAsync(null);

        viewModel.Status.ShouldContain("do not reconcile");
        viewModel.Reconciliation.ShouldContain("does not reconcile");
    }

    // ---------------------------------------------------------------- column mappings

    [Fact]
    public void Mappings_TheMasterKeySet_IsOnTheActionBar()
    {
        var viewModel = new ExcelMappingsViewModel(new FakeExcelMappingService());

        var gestures = viewModel.ActionBar.Select(button => button.Shortcut).ToList();
        gestures.ShouldContain(AppShortcuts.CreateOnTheFly);
        gestures.ShouldContain(AppShortcuts.Delete);

        // Ctrl+A and Alt+X come from the screen's accept/cancel commands, which the shell adds.
        viewModel.AcceptCommand.ShouldNotBeNull();
        viewModel.CancelCommand.ShouldNotBeNull();
    }

    [Fact]
    public void Mappings_WithNothingSelected_DoesNotOfferDeactivate()
    {
        var viewModel = new ExcelMappingsViewModel(new FakeExcelMappingService());

        viewModel.ActionBar
            .Single(button => button.Shortcut == AppShortcuts.Delete)
            .IsAvailable
            .ShouldBeFalse();
    }

    [Fact]
    public void Mappings_MissingRequired_NamesWhatIsStillUnmapped()
    {
        var viewModel = new ExcelMappingsViewModel(new FakeExcelMappingService())
        {
            Target = ExcelImportTarget.Items,
        };

        viewModel.MissingRequired.ShouldContain(InteropFieldCatalogue.Name);
        viewModel.MissingRequired.ShouldContain(InteropFieldCatalogue.BaseUnit);

        viewModel.Columns.Add(new ExcelMappingsViewModel.MappingColumnEditor(viewModel)
        {
            SourceHeader = "Item",
            TargetField = InteropFieldCatalogue.Name,
        });

        viewModel.MissingRequired.ShouldNotContain($" {InteropFieldCatalogue.Name}");
        viewModel.MissingRequired.ShouldContain(InteropFieldCatalogue.BaseUnit);
    }

    /// <summary>
    /// Changing the target clears lines the new target cannot accept.
    /// </summary>
    /// <remarks>
    /// Left alone, the line would look filled in and be refused at save time — which is the worst
    /// order to discover it in, because by then the owner has finished the grid.
    /// </remarks>
    [Fact]
    public void Mappings_ChangingTheTarget_ClearsFieldsTheNewOneDoesNotHave()
    {
        var viewModel = new ExcelMappingsViewModel(new FakeExcelMappingService())
        {
            Target = ExcelImportTarget.Items,
        };

        viewModel.Columns.Add(new ExcelMappingsViewModel.MappingColumnEditor(viewModel)
        {
            SourceHeader = "UOM",
            TargetField = InteropFieldCatalogue.BaseUnit,
        });

        viewModel.Columns.Add(new ExcelMappingsViewModel.MappingColumnEditor(viewModel)
        {
            SourceHeader = "Item",
            TargetField = InteropFieldCatalogue.Name,
        });

        viewModel.Target = ExcelImportTarget.Ledgers;

        // Ledgers have a Name; they do not have a BaseUnit.
        viewModel.Columns[0].TargetField.ShouldBe(string.Empty);
        viewModel.Columns[1].TargetField.ShouldBe(InteropFieldCatalogue.Name);
        viewModel.AvailableFields.ShouldContain(InteropFieldCatalogue.Parent);
        viewModel.AvailableFields.ShouldNotContain(InteropFieldCatalogue.BaseUnit);
    }

    [Fact]
    public async Task Mappings_SavingWithNoName_IsRefused()
    {
        var service = new FakeExcelMappingService();
        var viewModel = new ExcelMappingsViewModel(service);

        await ((IAsyncRelayCommand)viewModel.SaveCommand).ExecuteAsync(null);

        viewModel.Status.ShouldContain("needs a name");
        service.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mappings_SavingWithNoColumnMapped_IsRefused()
    {
        var service = new FakeExcelMappingService();
        var viewModel = new ExcelMappingsViewModel(service) { Name = "Supplier sheet" };

        viewModel.Columns.Add(new ExcelMappingsViewModel.MappingColumnEditor(viewModel)
        {
            SourceHeader = "Warranty",
            TargetField = string.Empty,
        });

        await ((IAsyncRelayCommand)viewModel.SaveCommand).ExecuteAsync(null);

        viewModel.Status.ShouldContain("No column is mapped");
        service.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mappings_Saving_KeepsOnlyTheLinesThatPointSomewhere()
    {
        var service = new FakeExcelMappingService();
        var viewModel = new ExcelMappingsViewModel(service)
        {
            Name = "Supplier sheet",
            Target = ExcelImportTarget.Items,
        };

        viewModel.Columns.Add(new ExcelMappingsViewModel.MappingColumnEditor(viewModel)
        {
            SourceHeader = "Description of Goods",
            TargetField = InteropFieldCatalogue.Name,
        });

        viewModel.Columns.Add(new ExcelMappingsViewModel.MappingColumnEditor(viewModel)
        {
            SourceHeader = "UOM",
            TargetField = InteropFieldCatalogue.BaseUnit,
        });

        // An unmapped column of the sheet is not a mapping line — it is a column the owner looked at
        // and decided this application has no use for.
        viewModel.Columns.Add(new ExcelMappingsViewModel.MappingColumnEditor(viewModel)
        {
            SourceHeader = "Warranty",
            TargetField = string.Empty,
        });

        await ((IAsyncRelayCommand)viewModel.SaveCommand).ExecuteAsync(null);

        var saved = service.Saved.ShouldHaveSingleItem();
        saved.Columns.Count.ShouldBe(2);
        saved.Columns.ShouldNotContain(column => column.SourceHeader == "Warranty");
    }

    /// <summary>
    /// Runs posted callbacks on the calling thread, for the duration of a <c>using</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Progress{T}"/> delivers through the synchronisation context it captured, which is
    /// what makes it the right sink for a viewmodel — the import service reports from whatever thread
    /// it happens to be on, and the handler still touches bound properties on the UI thread. Under a
    /// test runner there is no such context and delivery is queued to the thread pool, so a test that
    /// asserted straight after the await would be racing it. Supplying one that runs inline removes
    /// the race without changing what the viewmodel does.
    /// </remarks>
    private sealed class InlineContext : SynchronizationContext, IDisposable
    {
        private readonly SynchronizationContext? _previous = Current;

        public InlineContext() => SetSynchronizationContext(this);

        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Dispose() => SetSynchronizationContext(_previous);
    }

    private static InteropImportPreview PreviewWith(
        IReadOnlyList<InteropImportProblem>? problems = null,
        IReadOnlyList<InteropImportWarning>? warnings = null,
        IReadOnlyList<InteropRecordTally>? tallies = null) =>
        new(
            "books.xml",
            "Tally XML",
            problems ?? [],
            warnings ?? [],
            tallies ?? [],
            new InteropTotals(0, 0));
}
