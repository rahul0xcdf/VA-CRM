using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.Domain.Banking;

namespace VinayakaAgencies.App.Tests.ViewModels;

/// <summary>
/// What the Phase 7 screens put on the shell's bars, and the one piece of screen logic that is not
/// in a service (phase-7-tds-payroll-banking.md, ADR-0007).
/// </summary>
/// <remarks>
/// The bars are the shell's key routing table — <c>ShellWindow</c> walks them to dispatch a
/// keystroke — so a bar that advertises a key with no command behind it is a dead key, not a
/// cosmetic slip. ADR-0006's rule is that a key is declared only where it works, and these tests
/// are what makes that checkable for the screens this phase adds.
/// </remarks>
public sealed class Phase7ScreenShortcutTests
{
    [Fact]
    public void EveryPhase7Screen_IsRegisteredInAModule()
    {
        var headers = ScreenModuleCatalog.Discover()
            .SelectMany(module => module.Screens)
            .Select(screen => screen.Header)
            .ToList();

        headers.ShouldContain("TDS/TCS sections");
        headers.ShouldContain("TDS/TCS setup");
        headers.ShouldContain("TDS/TCS register");
        headers.ShouldContain("Payments & receipts");
        headers.ShouldContain("Employees");
        headers.ShouldContain("Payroll setup");
        headers.ShouldContain("Payroll run");
    }

    [Fact]
    public void TheTwoPhase7Groups_SitAfterGstAndBeforeAnythingLater()
    {
        var modules = ScreenModuleCatalog.Discover().ToList();

        var tds = modules.Single(module => module.Name == "TDS & Banking");
        var payroll = modules.Single(module => module.Name == "Payroll");
        var gst = modules.Single(module => module.Name == "GST");

        tds.Order.ShouldBeGreaterThan(gst.Order);
        payroll.Order.ShouldBeGreaterThan(tds.Order);
    }

    /// <summary>
    /// The master key set ADR-0007 fixed for every master screen: Alt+C new, Alt+D
    /// delete-or-deactivate. Ctrl+A and Alt+X reach the screen through AcceptCommand and
    /// CancelCommand rather than the bar, which is why they are asserted separately.
    /// </summary>
    [Fact]
    public void TheSectionMaster_DeclaresTheSharedMasterKeySet()
    {
        var viewModel = new TaxSectionsViewModel(new StubTaxSectionService());

        var bar = ((IScreenShortcuts)viewModel).ActionBar;

        bar.Select(button => button.Gesture).ShouldBe(["Alt+C", "Alt+D"]);
        bar.ShouldAllBe(button => button.Command != null);

        ((IScreenShortcuts)viewModel).AcceptCommand.ShouldNotBeNull();
        ((IScreenShortcuts)viewModel).CancelCommand.ShouldNotBeNull();
    }

    [Fact]
    public void TheEmployeeMaster_DeclaresTheSameMasterKeySet()
    {
        var viewModel = new EmployeesViewModel(new StubPayrollMasterService());

        ((IScreenShortcuts)viewModel).ActionBar
            .Select(button => button.Gesture)
            .ShouldBe(["Alt+C", "Alt+D"]);
    }

    /// <summary>
    /// A report screen drills on Ctrl+Enter and on nothing else (ADR-0007). The register's drill
    /// target is the challan roll-up.
    /// </summary>
    [Fact]
    public void TheDeductionRegister_DrillsOnCtrlEnterOnly()
    {
        var viewModel = new TaxDeductionRegisterViewModel(
            new StubTaxDeductionService(),
            new StubTaxSectionService());

        var bar = ((IScreenShortcuts)viewModel).ActionBar;

        bar.Select(button => button.Gesture).ShouldBe(["Ctrl+Enter"]);
        viewModel.IsChallanVisible.ShouldBeFalse("the drill pane stays shut until it is asked for");
    }

    [Fact]
    public void ThePayrollRun_DrillsOnCtrlEnterOnly() =>
        ((IScreenShortcuts)new PayrollRunViewModel(
                new StubPayrollRunService(),
                new StubVoucherPostingService()))
            .ActionBar
            .Select(button => button.Gesture)
            .ShouldBe(["Ctrl+Enter"]);

    /// <summary>
    /// F5 and F6 keep TallyPrime's published meanings, which ADR-0006 centralised precisely so a
    /// screen could not quietly redefine one.
    /// </summary>
    [Fact]
    public void ThePaymentScreen_UsesTallysOwnPaymentAndReceiptKeys()
    {
        var viewModel = NewPaymentsViewModel();

        ((IScreenShortcuts)viewModel).ActionBar
            .Select(button => button.Gesture)
            .ShouldBe(["F5", "F6", "Ctrl+P"]);
    }

    // ------------------------------------------------- the mode-dependent fields on the payment

    /// <summary>
    /// Which identifier a mode needs is a rule, and the rule itself lives in
    /// <c>PaymentInstrument</c>. What the screen owns is only which fields to show, and showing a
    /// cheque-number box for a UPI payment is how an operator ends up typing one.
    /// </summary>
    [Theory]
    [InlineData(PaymentMode.Cheque, true, false)]
    [InlineData(PaymentMode.DemandDraft, true, false)]
    [InlineData(PaymentMode.Neft, false, true)]
    [InlineData(PaymentMode.Rtgs, false, true)]
    [InlineData(PaymentMode.Imps, false, true)]
    [InlineData(PaymentMode.Upi, false, true)]
    [InlineData(PaymentMode.Cash, false, false)]
    public void ThePaymentScreen_ShowsOnlyTheIdentifierFieldsTheModeActuallyHas(
        PaymentMode mode,
        bool expectsInstrumentNumber,
        bool expectsBankReference)
    {
        var viewModel = NewPaymentsViewModel();

        viewModel.Mode = mode;

        viewModel.NeedsInstrumentNumber.ShouldBe(expectsInstrumentNumber);
        viewModel.NeedsBankReference.ShouldBe(expectsBankReference);
    }

    /// <summary>
    /// The words are computed as the amount is typed, from the same domain converter the printed
    /// cheque uses — so what the operator reads back before posting is what will print.
    /// </summary>
    [Fact]
    public void ThePaymentScreen_WordsTheAmountAsItIsTyped()
    {
        var viewModel = NewPaymentsViewModel();

        viewModel.Amount = "125000";

        viewModel.AmountInWords.ShouldBe("Rupees One Lakh Twenty Five Thousand Only");

        viewModel.Amount = "not a number";

        viewModel.AmountInWords.ShouldBeEmpty();
    }

    private static PaymentsViewModel NewPaymentsViewModel() =>
        new(new StubBankingService(),
            new StubVoucherPostingService(),
            new StubChartOfAccountsService(),
            new StubPrintTemplateService());
}
