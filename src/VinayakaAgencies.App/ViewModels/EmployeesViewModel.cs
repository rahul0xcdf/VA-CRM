using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Payroll;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The employee master and each person's salary structure
/// (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// <para>
/// Master key set per ADR-0007: <c>Alt+C</c> new, <c>Ctrl+A</c> save, <c>Alt+D</c>
/// deactivate, <c>Alt+X</c> reload the selected record.
/// </para>
/// <para>
/// The structure grid is history, not a form: a raise appends a line from its own date and closes
/// the one before it, so September's pay slip stays reproducible after an October raise. That is
/// why there is no "edit line" here and why every row shows its own window.
/// </para>
/// </remarks>
public sealed partial class EmployeesViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints =
        "Alt+C New · Ctrl+A Save · Alt+D Deactivate · Alt+X Reload";

    private readonly IPayrollMasterService _payroll;
    private List<EmployeeSummary> _allEmployees = [];

    [ObservableProperty]
    private ObservableCollection<EmployeeSummary> _employees = [];

    [ObservableProperty]
    private ObservableCollection<SalaryStructureLineSummary> _structure = [];

    [ObservableProperty]
    private ObservableCollection<PayHeadSummary> _payHeadChoices = [];

    [ObservableProperty]
    private EmployeeSummary? _selectedEmployee;

    [ObservableProperty]
    private PayHeadSummary? _structurePayHead;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDesignation = string.Empty;

    [ObservableProperty]
    private string _editPan = string.Empty;

    [ObservableProperty]
    private string _editUan = string.Empty;

    [ObservableProperty]
    private string _editEsiNumber = string.Empty;

    [ObservableProperty]
    private DateTime _editJoinedOn = DateTime.Today;

    [ObservableProperty]
    private DateTime? _editLeftOn;

    [ObservableProperty]
    private string _structureAmount = string.Empty;

    [ObservableProperty]
    private string _structurePercent = string.Empty;

    [ObservableProperty]
    private DateTime _structureEffectiveFrom = DateTime.Today;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public EmployeesViewModel(IPayrollMasterService payroll)
    {
        _payroll = payroll ?? throw new ArgumentNullException(nameof(payroll));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => SaveCommand;

    public ICommand? CancelCommand => ReloadSelectedCommand;

    /// <inheritdoc />
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New employee", ClearEditorCommand),
        new ShortcutButton(AppShortcuts.Delete, "Deactivate", DeactivateSelectedCommand),
    ];

    partial void OnSelectedEmployeeChanged(EmployeeSummary? value)
    {
        if (value is null)
        {
            return;
        }

        EditId = value.Id;
        EditCode = value.Code;
        EditName = value.Name;
        EditDesignation = value.Designation ?? string.Empty;
        EditPan = value.Pan ?? string.Empty;
        EditUan = value.Uan ?? string.Empty;
        EditEsiNumber = value.EsiNumber ?? string.Empty;
        EditJoinedOn = value.JoinedOn.ToDateTime(TimeOnly.MinValue);
        EditLeftOn = value.LeftOn?.ToDateTime(TimeOnly.MinValue);

        _ = LoadStructureAsync(value.Id, CancellationToken.None);
    }

    partial void OnIncludeInactiveChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            _allEmployees = [.. await _payroll
                .ListEmployeesAsync(includeInactive: true, cancellationToken)
                .ConfigureAwait(true)];

            PayHeadChoices = new ObservableCollection<PayHeadSummary>(
                await _payroll.ListPayHeadsAsync(includeInactive: false, cancellationToken)
                    .ConfigureAwait(true));

            ApplyFilter();

            Status = _allEmployees.Count == 0
                ? "No employees yet."
                : $"{_allEmployees.Count} employee(s) on file.";
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
        if (string.IsNullOrWhiteSpace(EditCode) || string.IsNullOrWhiteSpace(EditName))
        {
            Status = "An employee needs a code and a name.";
            return;
        }

        IsBusy = true;

        try
        {
            var request = new SaveEmployeeRequest(
                EditCode.Trim(),
                EditName.Trim(),
                DateOnly.FromDateTime(EditJoinedOn),
                NullIfEmpty(EditDesignation),
                NullIfEmpty(EditPan),
                NullIfEmpty(EditUan),
                NullIfEmpty(EditEsiNumber),
                EditLeftOn.HasValue ? DateOnly.FromDateTime(EditLeftOn.Value) : null);

            var saved = EditId is { } id
                ? await _payroll.UpdateEmployeeAsync(id, request, cancellationToken).ConfigureAwait(true)
                : await _payroll.CreateEmployeeAsync(request, cancellationToken).ConfigureAwait(true);

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            EditId = saved.Id;
            Status = $"Saved {saved.Code} {saved.Name}.";
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
    private async Task AddStructureLineAsync(CancellationToken cancellationToken)
    {
        if (EditId is not { } employeeId)
        {
            Status = "Save the employee first.";
            return;
        }

        if (StructurePayHead is null)
        {
            Status = "Pick the pay head this line is for.";
            return;
        }

        IsBusy = true;

        try
        {
            decimal? amount = decimal.TryParse(
                    StructureAmount?.Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var parsedAmount)
                ? parsedAmount
                : null;

            long? percentMinor = decimal.TryParse(
                    StructurePercent?.Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var parsedPercent)
                ? Domain.Common.MoneyScale.ToMinor(parsedPercent)
                : null;

            await _payroll.AddStructureLineAsync(
                    new SaveSalaryStructureLineRequest(
                        employeeId,
                        StructurePayHead.Id,
                        DateOnly.FromDateTime(StructureEffectiveFrom),
                        amount,
                        percentMinor),
                    cancellationToken)
                .ConfigureAwait(true);

            await LoadStructureAsync(employeeId, cancellationToken).ConfigureAwait(true);

            StructureAmount = string.Empty;
            StructurePercent = string.Empty;

            Status = $"Added {StructurePayHead.Name} from "
                + $"{StructureEffectiveFrom:dd-MMM-yyyy}. Earlier months are untouched.";
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
    private async Task DeactivateSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedEmployee is null)
        {
            Status = "Pick an employee first.";
            return;
        }

        IsBusy = true;

        try
        {
            await _payroll.SetEmployeeActiveAsync(SelectedEmployee.Id, false, cancellationToken)
                .ConfigureAwait(true);

            var name = SelectedEmployee.Name;
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Deactivated {name}. To record a leaver instead, set their last day.";
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
    private void ReloadSelected()
    {
        if (SelectedEmployee is null)
        {
            ClearEditor();
            return;
        }

        var selected = SelectedEmployee;
        SelectedEmployee = null;
        SelectedEmployee = selected;
        Status = "Reloaded from the books — unsaved edits discarded.";
    }

    [RelayCommand]
    private void ClearEditor()
    {
        SelectedEmployee = null;
        EditId = null;
        EditCode = string.Empty;
        EditName = string.Empty;
        EditDesignation = string.Empty;
        EditPan = string.Empty;
        EditUan = string.Empty;
        EditEsiNumber = string.Empty;
        EditJoinedOn = DateTime.Today;
        EditLeftOn = null;
        Structure = [];
        Status = "New employee.";
    }

    private async Task LoadStructureAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        try
        {
            Structure = new ObservableCollection<SalaryStructureLineSummary>(
                await _payroll.ListStructureAsync(employeeId, cancellationToken).ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
    }

    private void ApplyFilter() =>
        Employees = new ObservableCollection<EmployeeSummary>(
            IncludeInactive ? _allEmployees : _allEmployees.Where(row => row.IsActive));

    private static string? NullIfEmpty(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
