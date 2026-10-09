using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Security;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Backs the first-run window: the one screen a brand-new install shows before there is any
/// company to open.
/// </summary>
/// <remarks>
/// A company is never defaulted into existence — its legal name and state code end up on filed
/// returns, so provisioning is a deliberate act with real inputs (spec §10, and the very message
/// the startup error showed). This view model collects those inputs and drives creation plus the
/// full seed: chart of accounts, inventory setup, pipeline setup.
/// </remarks>
public sealed partial class FirstRunCompanyViewModel : ObservableObject
{
    private static readonly Regex GstinPattern =
        new("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[0-9A-Z]{1}[Z]{1}[0-9A-Z]{1}$", RegexOptions.Compiled);

    private readonly ICompanyService _companies;
    private readonly ICompanyProvisioningService _provisioning;
    private readonly ISignInService _signIn;

    public FirstRunCompanyViewModel(
        ICompanyService companies,
        ICompanyProvisioningService provisioning,
        ISignInService signIn)
    {
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
    }

    /// <summary>Raised when the company exists and is fully provisioned; the window closes on it.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The id of the company this dialog created; null until then.</summary>
    [ObservableProperty]
    private Guid? _createdCompanyId;

    [ObservableProperty]
    private string _tradingName = string.Empty;

    [ObservableProperty]
    private string _legalName = string.Empty;

    [ObservableProperty]
    private StateCode? _selectedState;

    [ObservableProperty]
    private string _gstin = string.Empty;

    /// <summary>The owner account created alongside the company.</summary>
    /// <remarks>
    /// Collected here, not left to the sign-in window, so that a brand-new install is never in the
    /// unsecured state at all: until an account exists every permission is granted to everybody
    /// (see <see cref="Domain.Abstractions.IPermissionAuthority"/>), and an install that has books
    /// but no account is an install where that is true of real data.
    /// </remarks>
    [ObservableProperty]
    private string _ownerUserName = string.Empty;

    [ObservableProperty]
    private string _ownerDisplayName = string.Empty;

    [ObservableProperty]
    private string _ownerPassword = string.Empty;

    [ObservableProperty]
    private string _ownerPasswordAgain = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Tell the books who they belong to.";

    public IReadOnlyList<StateCode> States { get; } = IndianStateCodes.All;

    [RelayCommand]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var problem = Validate();

        if (problem is not null)
        {
            Status = problem;
            return;
        }

        IsBusy = true;

        try
        {
            var gstin = string.IsNullOrWhiteSpace(Gstin) ? null : Gstin.Trim().ToUpperInvariant();

            var company = await _companies.CreateAsync(
                    new CreateCompanyRequest(
                        TradingName.Trim(),
                        LegalName.Trim(),
                        SelectedState!.Code,
                        gstin),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = $"Creating {company.Name}'s chart of accounts…";
            await _provisioning.ProvisionAsync(company.Id, cancellationToken).ConfigureAwait(true);

            Status = "Preparing inventory setup…";
            await _provisioning.EnsureInventorySeedAsync(company.Id, cancellationToken).ConfigureAwait(true);

            Status = "Preparing sales & purchase setup…";
            await _provisioning.EnsurePipelineSeedAsync(company.Id, cancellationToken).ConfigureAwait(true);

            Status = "Preparing customer setup…";
            await _provisioning.EnsureCustomerSeedAsync(company.Id, cancellationToken).ConfigureAwait(true);

            Status = "Preparing GST setup…";
            await _provisioning.EnsureGstSeedAsync(company.Id, cancellationToken).ConfigureAwait(true);

            Status = "Preparing TDS setup…";
            await _provisioning.EnsureTdsSeedAsync(company.Id, cancellationToken).ConfigureAwait(true);

            Status = "Preparing payroll setup…";
            await _provisioning.EnsurePayrollSeedAsync(company.Id, cancellationToken).ConfigureAwait(true);

            Status = "Preparing user roles…";
            await _provisioning.EnsureSecuritySeedAsync(company.Id, cancellationToken).ConfigureAwait(true);

            // Same step as the company, deliberately. An install that has books and no account is
            // an install where every permission is granted to everybody, and leaving this to the
            // sign-in window would make that a state a fresh install can sit in.
            Status = "Creating the owner account…";

            var signedIn = await _signIn
                .CreateFirstAccountAsync(
                    company.Id,
                    OwnerUserName.Trim(),
                    string.IsNullOrWhiteSpace(OwnerDisplayName)
                        ? OwnerUserName.Trim()
                        : OwnerDisplayName.Trim(),
                    OwnerPassword,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!signedIn.Succeeded)
            {
                // The company and its seeds are already committed, so this is not a rollback — the
                // operator is sent to the sign-in window, which will ask for the account again.
                Status = signedIn.Message;
                return;
            }

            OwnerPassword = string.Empty;
            OwnerPasswordAgain = string.Empty;

            CreatedCompanyId = company.Id;
            Status = $"{company.Name} is ready. Opening…";

            CloseRequested?.Invoke(this, EventArgs.Empty);
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

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(TradingName))
        {
            return "Enter the trading name shown around the shop.";
        }

        if (string.IsNullOrWhiteSpace(LegalName))
        {
            return "Enter the registered legal name — it prints on tax invoices.";
        }

        if (SelectedState is null)
        {
            return "Pick the state the business is registered in.";
        }

        if (string.IsNullOrWhiteSpace(OwnerUserName))
        {
            return "Choose the user name you will sign in with.";
        }

        var passwordProblems = PasswordPolicy.Validate(OwnerPassword);

        if (passwordProblems.Count > 0)
        {
            return string.Join(" ", passwordProblems);
        }

        if (!string.Equals(OwnerPassword, OwnerPasswordAgain, StringComparison.Ordinal))
        {
            return "The two passwords do not match.";
        }

        if (!string.IsNullOrWhiteSpace(Gstin))
        {
            var candidate = Gstin.Trim().ToUpperInvariant();

            if (!GstinPattern.IsMatch(candidate))
            {
                return "That GSTIN does not look right — 15 characters like 29ABCDE1234F1Z5. Leave it blank to add later.";
            }
        }

        return null;
    }
}
