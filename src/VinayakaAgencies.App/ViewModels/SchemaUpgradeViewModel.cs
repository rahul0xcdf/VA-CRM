using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Infrastructure.Persistence;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Backs the startup window that asks one operator to apply a schema upgrade.
/// </summary>
/// <remarks>
/// `phase-9-multiuser.md` §2: auto-migrate-on-startup was acceptable while the product was
/// single-user and "becomes an explicit, gated, single-operator step". This is the gate. With two
/// or three clients, several of them racing to migrate one database as they launch is a corruption
/// risk rather than a convenience, and the one thing that reliably prevents it is a person deciding
/// when it happens.
/// <para>
/// The pending migrations are listed rather than counted. "3 changes pending" tells the operator
/// nothing they can act on or report; the names are what they can read back over the phone.
/// </para>
/// </remarks>
public sealed partial class SchemaUpgradeViewModel : ObservableObject
{
    private readonly IDatabaseInitialiser _initialiser;

    public SchemaUpgradeViewModel(IDatabaseInitialiser initialiser, IEnumerable<string> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        _initialiser = initialiser ?? throw new ArgumentNullException(nameof(initialiser));

        PendingMigrations = new ObservableCollection<string>(pending);
        Status = PendingMigrations.Count == 1
            ? "One schema change is waiting to be applied."
            : $"{PendingMigrations.Count} schema changes are waiting to be applied.";
    }

    /// <summary>Raised when the upgrade has finished, or the operator chose to quit.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The pending migrations, oldest first, by name.</summary>
    public ObservableCollection<string> PendingMigrations { get; }

    /// <summary>
    /// Whether the schema is now current, and so whether the shell may open.
    /// </summary>
    /// <remarks>
    /// The host reads this instead of assuming. Opening the books against a schema the build does
    /// not match is how a missing column becomes a crash halfway through billing.
    /// </remarks>
    [ObservableProperty]
    private bool _isUpToDate;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>True while the upgrade is running.</summary>
    /// <remarks>
    /// Drives both commands rather than a converter on each button: a half-applied migration is the
    /// one state worth going out of the way to prevent, and "the button was still clickable" is how
    /// it happens.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(QuitCommand))]
    private bool _isBusy;

    private bool CanInteract => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        Status = "Applying schema changes. Do not close this window.";

        try
        {
            var applied = await _initialiser.ApplyPendingMigrationsAsync().ConfigureAwait(true);

            IsUpToDate = true;
            Status = applied.Count == 0
                ? "The schema was already current."
                : $"Applied {applied.Count} change(s). Opening the books.";

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Deliberately not rethrown and deliberately not closing: a failed upgrade must leave
            // the operator looking at what went wrong, with the books unopened. Carrying on would
            // run the product against a half-migrated schema, which is worse than not starting.
            IsUpToDate = false;
            Status = $"The upgrade did not finish: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Quit()
    {
        IsUpToDate = false;
        Status = "Upgrade postponed.";
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
