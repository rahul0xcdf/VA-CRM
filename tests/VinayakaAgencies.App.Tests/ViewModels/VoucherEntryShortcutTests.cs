using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Auditing;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.Tests.ViewModels;

/// <summary>
/// What voucher entry puts on the shell's bars, and what the keys there do (ADR-0007).
/// </summary>
/// <remarks>
/// The bars are not decoration: <c>ShellWindow</c> routes keystrokes by walking them, so a bar that is
/// wrong or stale is a key firing the wrong command rather than a button looking odd. These tests are
/// the reason that can be relied on.
/// </remarks>
public sealed class VoucherEntryShortcutTests
{
    [Fact]
    public async Task ButtonBar_ShowsOneKeyPerVoucherTypeTheCompanyActuallyHas()
    {
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);
        posting.Seed("Sales", VoucherKind.Sales);

        var viewModel = await LoadedAsync(posting);

        var bar = ((IScreenShortcuts)viewModel).ButtonBar;

        bar.Select(button => button.Gesture).ShouldBe(["F5", "F8"]);
        bar.Select(button => button.Label).ShouldBe(["Payment", "Sales"]);
    }

    [Fact]
    public async Task ButtonBar_OmitsAKeyWhoseVoucherTypeIsDeactivated()
    {
        // ADR-0006: a bar never advertises a key that does nothing. A deactivated voucher type is as
        // unpostable as one no phase has built.
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);
        posting.Seed("Contra", VoucherKind.Contra, isActive: false);

        var viewModel = await LoadedAsync(posting);

        ((IScreenShortcuts)viewModel).ButtonBar
            .Select(button => button.Gesture)
            .ShouldBe(["F5"]);
    }

    [Fact]
    public async Task ButtonBar_MarksTheVoucherTypeNowBeingEntered()
    {
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);
        posting.Seed("Receipt", VoucherKind.Receipt);

        var viewModel = await LoadedAsync(posting);

        viewModel.SelectVoucherKindCommand.Execute(VoucherKind.Receipt);

        var bar = ((IScreenShortcuts)viewModel).ButtonBar;

        bar.Single(button => button.Gesture == "F6").IsCurrent.ShouldBeTrue();
        bar.Single(button => button.Gesture == "F5").IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task SelectVoucherKind_SwitchesTheTypeAndKeepsTheLinesAlreadyTyped()
    {
        // An operator who reaches for F6 having started on F5 mistyped the voucher type, not the
        // figures. Throwing the lines away would teach them to avoid the keyboard.
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);
        posting.Seed("Receipt", VoucherKind.Receipt);

        var viewModel = await LoadedAsync(posting);
        viewModel.Lines[0].Debit = 500m;

        viewModel.SelectVoucherKindCommand.Execute(VoucherKind.Receipt);

        viewModel.SelectedVoucherType!.Name.ShouldBe("Receipt");
        viewModel.Lines[0].Debit.ShouldBe(500m);
    }

    [Fact]
    public async Task SelectVoucherKind_ForATypeTheCompanyDoesNotHave_SaysSoAndChangesNothing()
    {
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);

        var viewModel = await LoadedAsync(posting);

        viewModel.SelectVoucherKindCommand.Execute(VoucherKind.Contra);

        viewModel.SelectedVoucherType!.Name.ShouldBe("Payment");
        viewModel.Status.ShouldContain("No active Contra voucher type");
    }

    [Fact]
    public async Task ActionBar_AdvertisesCreateOnTheFlyRemoveLineAndCancelVoucher()
    {
        var viewModel = await LoadedAsync(new FakeVoucherPostingService());

        ((IScreenShortcuts)viewModel).ActionBar
            .Select(button => button.Gesture)
            .ShouldBe(["Alt+C", "Ctrl+D", "Alt+F", "Alt+D"]);
    }

    [Fact]
    public void Cancel_IsClearTheForm_NotCancelThePostedVoucher()
    {
        // The pre-ADR-0007 screen had Alt+X cancelling a posted voucher, which moves figures out of
        // every statement. That is Alt+D's job; Alt+X abandons what is being typed.
        var viewModel = NewViewModel(new FakeVoucherPostingService());
        var shortcuts = (IScreenShortcuts)viewModel;

        shortcuts.CancelCommand.ShouldBe(viewModel.ClearEntryCommand);

        shortcuts.ActionBar
            .Single(button => button.Gesture == "Alt+D")
            .Command.ShouldBe(viewModel.CancelVoucherCommand);
    }

    [Fact]
    public async Task WhileCreatingALedger_AcceptSavesTheLedgerRatherThanPostingTheVoucher()
    {
        // Tally's sub-screens take Accept for as long as they are open. An operator finishing a
        // ledger must not post a half-typed voucher instead.
        var viewModel = await LoadedAsync(new FakeVoucherPostingService());
        var shortcuts = (IScreenShortcuts)viewModel;

        shortcuts.AcceptCommand.ShouldBe(viewModel.PostCommand);

        viewModel.BeginCreateLedgerCommand.Execute("Freight Outward");

        viewModel.IsCreatingLedger.ShouldBeTrue();
        viewModel.NewLedgerName.ShouldBe("Freight Outward");
        shortcuts.AcceptCommand.ShouldBe(viewModel.ConfirmCreateLedgerCommand);
        shortcuts.CancelCommand.ShouldBe(viewModel.CancelCreateLedgerCommand);
    }

    [Fact]
    public async Task CreatingALedger_PutsItStraightOntoTheLineTheOperatorWasFilling()
    {
        // This is the point of Alt+C: the cost of a missing master is the name, and nothing else.
        var chart = new FakeChartOfAccountsService();
        var viewModel = await LoadedAsync(new FakeVoucherPostingService(), chart);

        var line = viewModel.Lines[0];
        viewModel.CurrentLine = line;

        viewModel.BeginCreateLedgerCommand.Execute("Freight Outward");
        viewModel.NewLedgerGroup = viewModel.AccountGroups[0];

        await ((IAsyncRelayCommand)viewModel.ConfirmCreateLedgerCommand).ExecuteAsync(null);

        viewModel.IsCreatingLedger.ShouldBeFalse();
        line.Ledger.ShouldNotBeNull();
        line.Ledger!.Name.ShouldBe("Freight Outward");
        viewModel.Ledgers.ShouldContain(ledger => ledger.Name == "Freight Outward");
    }

    [Fact]
    public async Task RemoveLine_WithNoArgument_TakesTheLineTheCaretIsIn()
    {
        // The shell invokes action-bar commands without a parameter, so Ctrl+D pressed anywhere on
        // the screen has to find its own target.
        var viewModel = await LoadedAsync(new FakeVoucherPostingService());

        viewModel.AddLineCommand.Execute(null);
        viewModel.Lines.Count.ShouldBe(3);

        viewModel.CurrentLine = viewModel.Lines[1];
        var doomed = viewModel.Lines[1];

        viewModel.RemoveLineCommand.Execute(null);

        viewModel.Lines.ShouldNotContain(doomed);
        viewModel.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RemoveLine_NeverLeavesTheGridWithNowhereToType()
    {
        var viewModel = await LoadedAsync(new FakeVoucherPostingService());

        while (viewModel.Lines.Count > 0)
        {
            viewModel.CurrentLine = viewModel.Lines[0];
            viewModel.RemoveLineCommand.Execute(null);

            viewModel.Lines.ShouldNotBeEmpty();

            if (viewModel.Lines.Count == 1)
            {
                break;
            }
        }

        viewModel.Lines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CanPost_IgnoresTheTrailingBlankLineTheGridAlwaysShows()
    {
        // A voucher grid keeps one empty line past the last filled one so the operator can keep
        // typing. Counting it as invalid would keep Post disabled on every well-formed voucher.
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);

        var viewModel = await LoadedAsync(posting, new FakeChartOfAccountsService());

        viewModel.AddLineCommand.Execute(null);

        viewModel.Lines[0].Ledger = viewModel.Ledgers[0];
        viewModel.Lines[0].Debit = 100m;
        viewModel.Lines[1].Ledger = viewModel.Ledgers[1];
        viewModel.Lines[1].Credit = 100m;

        viewModel.Lines.Count.ShouldBe(3);
        viewModel.Lines[2].IsBlank.ShouldBeTrue();
        viewModel.CanPost.ShouldBeTrue();
    }

    [Fact]
    public async Task EveryGestureOnEitherBar_IsOneTheApplicationKeyMapDeclares()
    {
        // A screen inventing a gesture would escape the duplicate-gesture guard entirely.
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);

        var shortcuts = (IScreenShortcuts)await LoadedAsync(posting);

        foreach (var button in shortcuts.ButtonBar.Concat(shortcuts.ActionBar))
        {
            AppShortcuts.All.ShouldContain(button.Shortcut);
        }
    }

    [Fact]
    public async Task NoGestureAppearsTwiceAcrossTheScreensOwnBars()
    {
        var posting = new FakeVoucherPostingService();
        posting.Seed("Payment", VoucherKind.Payment);
        posting.Seed("Sales", VoucherKind.Sales);

        var shortcuts = (IScreenShortcuts)await LoadedAsync(posting);

        var gestures = shortcuts.ButtonBar.Concat(shortcuts.ActionBar)
            .Select(button => button.Gesture)
            .ToList();

        gestures.Distinct(StringComparer.Ordinal).Count().ShouldBe(gestures.Count);
    }

    private static VoucherEntryViewModel NewViewModel(
        FakeVoucherPostingService posting,
        FakeChartOfAccountsService? chart = null) =>
        new(posting, chart ?? new FakeChartOfAccountsService(), new FakeAuditLogReader());

    private static async Task<VoucherEntryViewModel> LoadedAsync(
        FakeVoucherPostingService posting,
        FakeChartOfAccountsService? chart = null)
    {
        var viewModel = NewViewModel(posting, chart);

        await ((IAsyncRelayCommand)viewModel.LoadListsCommand).ExecuteAsync(null);

        return viewModel;
    }

    private sealed class FakeVoucherPostingService : IVoucherPostingService
    {
        private readonly List<VoucherTypeSummary> _types = [];

        public void Seed(string name, VoucherKind kind, bool isActive = true) =>
            _types.Add(new VoucherTypeSummary(Guid.NewGuid(), name, kind, null, isActive));

        public Task<IReadOnlyList<VoucherTypeSummary>> ListVoucherTypesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoucherTypeSummary>>(_types);

        public Task<PostedVoucherSummary> PostAsync(
            PostVoucherRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These tests never post.");

        public Task<PostedVoucherSummary> AlterAsync(
            AlterVoucherRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These tests never alter.");

        public Task<PostedVoucherSummary> CancelAsync(
            Guid voucherId,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These tests never cancel.");

        public Task<PostedVoucherSummary?> GetCurrentAsync(
            Guid voucherId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PostedVoucherSummary?>(null);
    }

    private sealed class FakeChartOfAccountsService : IChartOfAccountsService
    {
        private readonly Guid _groupId = Guid.NewGuid();
        private readonly List<LedgerSummary> _ledgers;

        public FakeChartOfAccountsService()
        {
            _ledgers =
            [
                Ledger("Cash"),
                Ledger("Bank"),
            ];
        }

        public Task<IReadOnlyList<AccountGroupSummary>> ListGroupsAsync(
            bool includeInactive = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountGroupSummary>>(
            [
                new AccountGroupSummary(
                    _groupId, "Indirect Expenses", null, "/", AccountNature.Expense,
                    false, true, false, true, 0, 0),
            ]);

        public Task<IReadOnlyList<LedgerSummary>> ListLedgersAsync(
            bool includeInactive = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LedgerSummary>>(_ledgers);

        public Task<IReadOnlyList<LedgerSummary>> ListBankLedgersAsync(
            bool includeInactive = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LedgerSummary>>([]);

        public Task<LedgerSummary?> GetLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_ledgers.FirstOrDefault(ledger => ledger.Id == id));

        public Task<LedgerSummary> CreateLedgerAsync(
            CreateLedgerRequest request,
            CancellationToken cancellationToken = default)
        {
            var created = Ledger(request.Name);
            _ledgers.Add(created);

            return Task.FromResult(created);
        }

        public Task<LedgerSummary> UpdateLedgerAsync(
            UpdateLedgerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These tests never update a ledger.");

        public Task DeactivateLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ActivateLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteLedgerAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AccountGroupSummary> CreateGroupAsync(
            CreateAccountGroupRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These tests never create a group.");

        public Task<AccountGroupSummary> RenameGroupAsync(
            Guid id,
            string newName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These tests never rename a group.");

        private LedgerSummary Ledger(string name) =>
            new(Guid.NewGuid(), name, null, _groupId, "Indirect Expenses",
                AccountNature.Expense, false, true, true, null);
    }

    private sealed class FakeAuditLogReader : IAuditLogReader
    {
        public Task<IReadOnlyList<AuditLogRow>> ListAsync(
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            string? entityType = null,
            int maxRows = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditLogRow>>([]);

        public Task<IReadOnlyList<AuditLogRow>> ListForEntityAsync(
            string entityType,
            string entityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditLogRow>>([]);

        public Task<AuditChainVerification> VerifyChainAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These tests never verify the chain.");
    }
}
