using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Auditing;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The edit-log viewer: strictly read-only, with the hash-chain integrity check beside it
/// (spec §2, §11). There is no delete, no purge, and no filter that can hide rows permanently —
/// the interface offers none because the product must not.
/// </summary>
/// <remarks>
/// <c>Ctrl+Enter</c> opens the highlighted row's before and after images in full (ADR-0007). The grid
/// has to truncate them to stay readable, and a truncated before-image is exactly the thing this
/// screen exists to show — non-negotiable #3 is only met if the values can actually be read.
/// </remarks>
public sealed partial class EditLogViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IAuditLogReader _auditLogReader;

    /// <summary>How many newest rows a plain refresh shows.</summary>
    private const int DefaultRowsShown = 200;

    [ObservableProperty]
    private ObservableCollection<AuditLogRow> _rows = [];

    [ObservableProperty]
    private AuditLogRow? _selectedRow;

    [ObservableProperty]
    private string? _detailCaption;

    [ObservableProperty]
    private string _detailBefore = string.Empty;

    [ObservableProperty]
    private string _detailAfter = string.Empty;

    [ObservableProperty]
    private string _entityTypeFilter = string.Empty;

    [ObservableProperty]
    private string _entityIdFilter = string.Empty;

    [ObservableProperty]
    private string _verificationStatus = "Chain not verified yet.";

    [ObservableProperty]
    private string _status = "Press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public EditLogViewModel(IAuditLogReader auditLogReader)
    {
        _auditLogReader = auditLogReader ?? throw new ArgumentNullException(nameof(auditLogReader));
    }

    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.DrillDown, "Full before/after", ShowDetailCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Ctrl+Enter Full before/after for the highlighted row · read-only, always";

    /// <summary>Ctrl+Enter: the highlighted row's images, untruncated.</summary>
    [RelayCommand]
    private void ShowDetail()
    {
        if (SelectedRow is null)
        {
            Status = "Highlight a row first, then press Ctrl+Enter.";
            return;
        }

        DetailCaption = $"{SelectedRow.EntityType} {SelectedRow.EntityId} — {SelectedRow.Action}";
        DetailBefore = SelectedRow.BeforeJson ?? "(nothing — this row created the record)";
        DetailAfter = SelectedRow.AfterJson ?? "(nothing — this row deleted the record)";

        Status = "Showing the full images for the highlighted row.";
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var entityType = string.IsNullOrWhiteSpace(EntityTypeFilter) ? null : EntityTypeFilter.Trim();
            var entityId = string.IsNullOrWhiteSpace(EntityIdFilter) ? null : EntityIdFilter.Trim();

            var rows = entityId is null
                ? await _auditLogReader.ListAsync(null, null, entityType, DefaultRowsShown, cancellationToken)
                    .ConfigureAwait(true)
                : await _auditLogReader.ListForEntityAsync(
                    entityType ?? throw new InvalidOperationException(
                        "An entity id filter needs its entity type, e.g. Ledger or VoucherRevision."),
                    entityId,
                    cancellationToken)
                    .ConfigureAwait(true);

            Rows = new ObservableCollection<AuditLogRow>(rows);

            Status = entityId is null
                ? $"Showing the newest {Rows.Count} row(s); every row ever written is retained."
                : $"{Rows.Count} row(s) for that entity.";
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
    private async Task VerifyChainAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var verification = await _auditLogReader.VerifyChainAsync(cancellationToken)
                .ConfigureAwait(true);

            VerificationStatus = verification.IsIntact
                ? $"Chain intact over {verification.RowsChecked} row(s)."
                : $"CHAIN BROKEN at sequence {verification.FirstBrokenSequence}: {verification.Detail}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            VerificationStatus = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
