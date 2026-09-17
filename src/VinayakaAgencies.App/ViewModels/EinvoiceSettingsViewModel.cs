using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// E-invoice settings and NIC credentials (phase-6-einvoice-eway.md §4a).
/// </summary>
/// <remarks>
/// <para>
/// The credential boxes are write-only. Nothing on this screen can display a stored secret, because
/// the settings DTO does not carry one — not even masked. The screen shows only whether credentials are
/// present, and re-entering all four is how they are changed. That is deliberately less convenient
/// than an editable field, and it is what makes checkpoint C6.6 hold on the UI side rather than only
/// in the service.
/// </para>
/// <para>
/// Switching to production is refused until credentials exist, matching §2's requirement that
/// production is reached only after the owner signs off on sandbox.
/// </para>
/// </remarks>
public sealed partial class EinvoiceSettingsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Save · Alt+X Reload";

    private readonly IEinvoiceSettingsService _settings;

    [ObservableProperty]
    private bool _isEinvoicingApplicable = true;

    [ObservableProperty]
    private bool _useProduction;

    [ObservableProperty]
    private string _ewayBillThreshold = string.Empty;

    [ObservableProperty]
    private bool _hasCredentials;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public EinvoiceSettingsViewModel(IEinvoiceSettingsService settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => SaveCommand;

    public ICommand? CancelCommand => RefreshCommand;

    /// <summary>Text the screen shows instead of a credential value.</summary>
    public string CredentialState => HasCredentials
        ? "Stored and encrypted for this Windows user."
        : "Not set. The portal cannot be reached until all four are entered.";

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Apply(await _settings.GetAsync(cancellationToken).ConfigureAwait(true));
            ClearCredentialEntry();
            Status = "Loaded.";
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
        if (!decimal.TryParse(
                EwayBillThreshold,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var threshold)
            || threshold < 0m)
        {
            Status = "The e-way bill threshold must be a non-negative amount.";
            return;
        }

        IsBusy = true;

        try
        {
            await _settings.SetApplicabilityAsync(IsEinvoicingApplicable, cancellationToken)
                .ConfigureAwait(true);

            var summary = await _settings.SetEwayBillThresholdAsync(threshold, cancellationToken)
                .ConfigureAwait(true);

            // Environment last, because switching to production is refused unless credentials exist —
            // and if the user is entering credentials in this same save, they must land first.
            summary = await _settings
                .SetEnvironmentAsync(
                    UseProduction ? IrpEnvironment.Production : IrpEnvironment.Sandbox,
                    cancellationToken)
                .ConfigureAwait(true);

            Apply(summary);
            Status = "Saved.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;

            // The refused change must not linger on screen as if it had taken.
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveCredentialsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ClientId)
            || string.IsNullOrWhiteSpace(ClientSecret)
            || string.IsNullOrWhiteSpace(Username)
            || string.IsNullOrWhiteSpace(Password))
        {
            Status = "All four credentials are needed — the portal accepts nothing partial.";
            return;
        }

        IsBusy = true;

        try
        {
            var summary = await _settings
                .SaveCredentialsAsync(
                    new SaveIrpCredentialsRequest(
                        ClientId.Trim(), ClientSecret, Username.Trim(), Password),
                    cancellationToken)
                .ConfigureAwait(true);

            Apply(summary);

            // Wiped from the view model the instant they are stored, so a screen left open does not
            // keep four plaintext secrets in memory.
            ClearCredentialEntry();

            Status = "Credentials encrypted and stored.";
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
    private async Task ClearCredentialsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Apply(await _settings.ClearCredentialsAsync(cancellationToken).ConfigureAwait(true));
            ClearCredentialEntry();
            Status = "Credentials cleared; back to sandbox.";
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

    private void Apply(EinvoiceSettingsSummary summary)
    {
        IsEinvoicingApplicable = summary.IsEinvoicingApplicable;
        UseProduction = summary.Environment is IrpEnvironment.Production;
        EwayBillThreshold = MoneyScale.FromMinor(summary.EwayBillThresholdMinor)
            .ToString("0.##", CultureInfo.CurrentCulture);
        HasCredentials = summary.HasCredentials;

        OnPropertyChanged(nameof(CredentialState));
    }

    private void ClearCredentialEntry()
    {
        ClientId = string.Empty;
        ClientSecret = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
    }
}
