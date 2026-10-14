using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Maintenance;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Backup and restore from the screen (phase-10-hardening.md §2, checkpoints C10.1 and C10.6).
/// </summary>
/// <remarks>
/// <para>
/// Restore is deliberately two keystrokes, not one. It replaces the live books, and a single
/// mis-selected row would otherwise cost the operator everything posted since the archive was
/// taken. <see cref="RestoreCommand"/> only arms the confirmation; <see cref="ConfirmRestoreCommand"/>
/// performs it, and <see cref="CancelCommand"/> disarms. The rule lives here rather than in a
/// dialog so a unit test can reach it without a window (ADR-0007).
/// </para>
/// <para>
/// The screen also states the things §4a D2 and D5 say an operator must not have to be told
/// verbally: whether a second, off-machine copy is configured and how old it is, and that the
/// database file itself is not encrypted.
/// </para>
/// </remarks>
public sealed partial class BackupViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IBackupService _backups;
    private readonly IDiagnosticsBundleService _diagnostics;
    private readonly BackupOptions _options;
    private readonly IAppDataPaths _paths;

    [ObservableProperty]
    private ObservableCollection<BackupSummary> _archives = [];

    [ObservableProperty]
    private BackupSummary? _selectedArchive;

    [ObservableProperty]
    private string _status = "Press Refresh to list the backups on this machine.";

    [ObservableProperty]
    private string _verificationDetail = string.Empty;

    /// <summary>True once Restore has been pressed and before it has been confirmed.</summary>
    [ObservableProperty]
    private bool _isRestoreArmed;

    [ObservableProperty]
    private bool _isBusy;

    public BackupViewModel(
        IBackupService backups,
        IDiagnosticsBundleService diagnostics,
        BackupOptions options,
        IAppDataPaths paths)
    {
        _backups = backups ?? throw new ArgumentNullException(nameof(backups));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>Where the backups are, shown so an operator can find them in Explorer.</summary>
    public string BackupDirectory => _paths.BackupDirectory;

    /// <summary>Where the books are (§4a D7).</summary>
    public string DataDirectory => _paths.DataRoot;

    /// <summary>
    /// The off-site warning of §4a D2, phrased for whichever state this install is actually in.
    /// </summary>
    public string SecondDestinationStatus => string.IsNullOrWhiteSpace(_options.SecondDestination)
        ? "No second destination is set, so every copy of your books is on this computer. "
          + "Fire, theft or a failed disk would take them with it. Set Backup:SecondDestination to "
          + "a network share or an external drive."
        : string.Create(
            CultureInfo.InvariantCulture,
            $"A second copy of every backup is written to {_options.SecondDestination}.");

    /// <summary>The data-at-rest position of §4a D5, said on the screen rather than only in a spec.</summary>
    public static string DataAtRestNotice =>
        "The database is not encrypted. Anyone who obtains the file or a backup of it can read "
        + "every customer, price and voucher. Credentials and passwords inside it are protected. "
        + "If a backup drive leaves the premises, encrypt it with BitLocker.";

    public string RetentionNotice => string.Create(
        CultureInfo.InvariantCulture,
        $"The newest {Math.Max(1, _options.RetainedBackupCount)} backups are kept; older ones are removed automatically. The most recent is never removed.");

    // ----------------------------------------------------------------------- ADR-0006 chrome

    ICommand? IScreenShortcuts.AcceptCommand => BackupNowCommand;

    ICommand? IScreenShortcuts.CancelCommand => CancelCommand;

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.Accept, "Back up now", BackupNowCommand),
        new ShortcutButton(AppShortcuts.Find, "Verify", VerifyCommand),

        // Alt+D for a destructive act, matching the master screens' delete key (ADR-0007). It arms
        // rather than performs; the bar relabels once armed so the key is never a surprise.
        new ShortcutButton(
            AppShortcuts.Delete,
            IsRestoreArmed ? "Confirm restore" : "Restore",
            IsRestoreArmed ? ConfirmRestoreCommand : RestoreCommand),
    ];

    string? IScreenShortcuts.StatusBarHints => IsRestoreArmed
        ? "Restore is armed — press Alt+D again to replace the books, or Alt+X to cancel."
        : null;

    // ----------------------------------------------------------------------------- commands

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var archives = await _backups.ListAsync(cancellationToken).ConfigureAwait(true);
            Archives = new ObservableCollection<BackupSummary>(archives);

            if (SelectedArchive is null && Archives.Count > 0)
            {
                SelectedArchive = Archives[0];
            }

            Status = archives.Count == 0
                ? "This install has never completed a backup. Press Back up now."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{archives.Count} backup(s). Newest: {archives[0].FileName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        IsRestoreArmed = false;

        try
        {
            var summary = await _backups
                .CreateAsync("operator requested", cancellationToken)
                .ConfigureAwait(true);

            // Reported as unverified rather than as a success, if that is what happened. §2 asks
            // for verified backups, and an archive nobody could check is not one.
            Status = summary.IsVerified
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Backed up and verified: {summary.FileName} ({summary.SizeBytes / 1024d / 1024d:F2} MB).")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Wrote {summary.FileName}, but it did not pass verification. Do not rely on it.");

            var failed = summary.Destinations.Where(destination => !destination.Succeeded).ToList();

            VerificationDetail = failed.Count == 0
                ? string.Join(
                    Environment.NewLine,
                    summary.Destinations.Select(destination => "Written to " + destination.Path))
                : string.Join(
                    Environment.NewLine,
                    failed.Select(destination => "Could not write to " + destination.Path + ": " + destination.Problem));

            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "The backup failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task VerifyAsync(CancellationToken cancellationToken)
    {
        if (SelectedArchive is null)
        {
            Status = "Choose a backup to verify.";
            return;
        }

        IsBusy = true;

        try
        {
            var verification = await _backups
                .VerifyAsync(SelectedArchive.FilePath, cancellationToken)
                .ConfigureAwait(true);

            Status = verification.IsValid
                ? "This backup is intact: every file matches, and the database inside it passes its integrity check."
                : "This backup has problems and should not be restored.";

            VerificationDetail = verification.IsValid
                ? string.Empty
                : string.Join(Environment.NewLine, verification.Problems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Arms the restore. Deliberately does not perform it — see the class remarks.
    /// </summary>
    [RelayCommand]
    private void Restore()
    {
        if (SelectedArchive is null)
        {
            Status = "Choose a backup to restore.";
            return;
        }

        IsRestoreArmed = true;

        Status = string.Create(
            CultureInfo.InvariantCulture,
            $"Restoring {SelectedArchive.FileName} will replace the current books. Confirm to go ahead.");

        VerificationDetail =
            "What you have now is copied aside first, so this can be undone — the copy's path is "
            + "reported when the restore finishes. The application must be restarted afterwards.";

        OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));
    }

    [RelayCommand]
    private async Task ConfirmRestoreAsync(CancellationToken cancellationToken)
    {
        if (!IsRestoreArmed || SelectedArchive is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var outcome = await _backups
                .RestoreAsync(SelectedArchive.FilePath, cancellationToken)
                .ConfigureAwait(true);

            Status = outcome.FingerprintMatches
                ? "Restored. The trial balance and stock summary match the backup exactly."
                : "Restored, but the books do not match the backup. " + outcome.Explanation;

            VerificationDetail =
                "The books as they were before this restore were copied to:" + Environment.NewLine
                + outcome.SafetyCopyPath + Environment.NewLine + Environment.NewLine
                + "Close and reopen the application now. Anything already on screen is from the "
                + "previous books.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "The restore did not happen: " + ex.Message;
            VerificationDetail = "The current books are untouched.";
        }
        finally
        {
            IsRestoreArmed = false;
            IsBusy = false;
            OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsRestoreArmed)
        {
            IsRestoreArmed = false;
            Status = "Restore cancelled. Nothing was changed.";
            VerificationDetail = string.Empty;
            OnPropertyChanged(nameof(IScreenShortcuts.ActionBar));
        }
    }

    [RelayCommand]
    private async Task CollectDiagnosticsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var bundle = await _diagnostics
                .CreateAsync("Collected from the Backup & Restore screen.", cancellationToken)
                .ConfigureAwait(true);

            Status = string.Create(
                CultureInfo.InvariantCulture,
                $"Diagnostics written: {bundle.FileName} ({bundle.SizeBytes / 1024d:F0} KB).");

            VerificationDetail =
                bundle.FilePath + Environment.NewLine + Environment.NewLine
                + "Logs and settings in this file have had credentials removed. Send it to whoever "
                + "is helping you.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "Could not collect diagnostics: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
