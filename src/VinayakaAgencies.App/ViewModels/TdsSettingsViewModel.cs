using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Tds;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// TDS/TCS configuration: the liability ledgers, the two turnover-linked flags, and the payee
/// profiles those flags act on (phase-7-tds-payroll-banking.md §4a D2, checkpoint C7.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>194Q and 206C(1H) are declared here, never inferred.</b> Both apply above a turnover limit,
/// and turnover as the books compute it is not necessarily turnover as the statute defines it — so
/// the owner declares whether the business is liable, following the Phase 6 precedent for
/// e-invoicing applicability. With both off the two sections never resolve.
/// </para>
/// <para>
/// The payee panel carries the other half of checkpoint C7.6: a buyer's written declaration that
/// they deduct on their purchases is what stops us collecting on the same transaction. It is
/// recorded, not inferred — the seller cannot see the buyer's books.
/// </para>
/// </remarks>
public sealed partial class TdsSettingsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Save · Alt+C Save payee profile · Alt+X Reload";

    private readonly ITdsSettingsService _settings;
    private readonly ITaxSectionService _sections;
    private readonly IChartOfAccountsService _chart;

    [ObservableProperty]
    private ObservableCollection<TaxSectionChoice> _tdsSectionChoices = [];

    [ObservableProperty]
    private ObservableCollection<TaxSectionChoice> _tcsSectionChoices = [];

    [ObservableProperty]
    private ObservableCollection<TaxPayeeProfileSummary> _profiles = [];

    [ObservableProperty]
    private ObservableCollection<LedgerChoice> _ledgerChoices = [];

    [ObservableProperty]
    private string _tdsPayableLedgerName = string.Empty;

    [ObservableProperty]
    private string _tcsPayableLedgerName = string.Empty;

    [ObservableProperty]
    private bool _isPurchaseOfGoodsDeductionEnabled;

    [ObservableProperty]
    private TaxSectionChoice? _purchaseOfGoodsSection;

    [ObservableProperty]
    private bool _isSaleOfGoodsCollectionEnabled;

    [ObservableProperty]
    private TaxSectionChoice? _saleOfGoodsSection;

    [ObservableProperty]
    private LedgerChoice? _profileLedger;

    [ObservableProperty]
    private PayeeCategory _profileCategory = PayeeCategory.Other;

    [ObservableProperty]
    private string _profilePan = string.Empty;

    [ObservableProperty]
    private bool _profileIsDeductionApplicable = true;

    [ObservableProperty]
    private bool _profileDeclaresDeductingOnPurchases;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public TdsSettingsViewModel(
        ITdsSettingsService settings,
        ITaxSectionService sections,
        IChartOfAccountsService chart)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sections = sections ?? throw new ArgumentNullException(nameof(sections));
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => SaveCommand;

    public ICommand? CancelCommand => RefreshCommand;

    public IReadOnlyList<PayeeCategory> CategoryChoices { get; } =
    [
        PayeeCategory.Any,
        PayeeCategory.IndividualOrHuf,
        PayeeCategory.Other,
    ];

    /// <inheritdoc />
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "Save payee", SavePayeeCommand),
    ];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var sections = await _sections
                .ListSectionsAsync(kind: null, includeInactive: false, cancellationToken)
                .ConfigureAwait(true);

            TdsSectionChoices = Choices(sections, TaxSectionKind.Tds);
            TcsSectionChoices = Choices(sections, TaxSectionKind.Tcs);

            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            TdsPayableLedgerName = settings.TdsPayableLedgerName;
            TcsPayableLedgerName = settings.TcsPayableLedgerName;
            IsPurchaseOfGoodsDeductionEnabled = settings.IsPurchaseOfGoodsDeductionEnabled;
            IsSaleOfGoodsCollectionEnabled = settings.IsSaleOfGoodsCollectionEnabled;

            PurchaseOfGoodsSection = TdsSectionChoices
                .FirstOrDefault(choice => choice.Id == settings.PurchaseOfGoodsSectionId);
            SaleOfGoodsSection = TcsSectionChoices
                .FirstOrDefault(choice => choice.Id == settings.SaleOfGoodsSectionId);

            Profiles = new ObservableCollection<TaxPayeeProfileSummary>(
                await _sections.ListPayeeProfilesAsync(cancellationToken).ConfigureAwait(true));

            if (LedgerChoices.Count == 0)
            {
                var ledgers = await _chart.ListLedgersAsync(includeInactive: false, cancellationToken)
                    .ConfigureAwait(true);

                LedgerChoices = new ObservableCollection<LedgerChoice>(
                    ledgers.Select(ledger => new LedgerChoice(ledger.Id, ledger.Name)));
            }

            Status = settings.IsPurchaseOfGoodsDeductionEnabled
                || settings.IsSaleOfGoodsCollectionEnabled
                ? "Loaded."
                : "Neither turnover-linked section is in force — both cost a flag and no behaviour.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            await _settings.SaveAsync(
                    new SaveTdsSettingsRequest(
                        TdsPayableLedgerName,
                        TcsPayableLedgerName,
                        IsPurchaseOfGoodsDeductionEnabled,
                        PurchaseOfGoodsSection?.Id,
                        IsSaleOfGoodsCollectionEnabled,
                        SaleOfGoodsSection?.Id),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = "Saved.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SavePayeeAsync(CancellationToken cancellationToken)
    {
        if (ProfileLedger is null)
        {
            Status = "Pick the party this profile describes.";
            return;
        }

        IsBusy = true;

        try
        {
            await _sections.SavePayeeProfileAsync(
                    new SaveTaxPayeeProfileRequest(
                        ProfileLedger.Id,
                        ProfileCategory,
                        string.IsNullOrWhiteSpace(ProfilePan) ? null : ProfilePan.Trim(),
                        ProfileIsDeductionApplicable,
                        ProfileDeclaresDeductingOnPurchases),
                    cancellationToken)
                .ConfigureAwait(true);

            var name = ProfileLedger.Name;
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = ProfileDeclaresDeductingOnPurchases
                ? $"Saved {name}. They deduct on their purchases, so we will not also collect (C7.6)."
                : $"Saved {name}.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ObservableCollection<TaxSectionChoice> Choices(
        IReadOnlyList<TaxSectionSummary> sections,
        TaxSectionKind kind) =>
        new(sections
            .Where(section => section.Kind == kind)
            .Select(section => new TaxSectionChoice(section.Id, $"{section.Code} — {section.Name}")));
}
