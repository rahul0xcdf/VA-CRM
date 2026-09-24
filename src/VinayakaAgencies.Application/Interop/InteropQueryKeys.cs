using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Application.Interop;

/// <summary>The named queries Phase 8 adds.</summary>
public static class InteropQueryKeys
{
    /// <summary>
    /// The books' whole-life debit and credit totals, in minor units.
    /// Parameters: <c>CompanyId</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C8.6 compares what an import moved the books by against what the file carried, and that needs
    /// a figure spanning every date the file touches. The trial balance cannot supply it: it is
    /// scoped to the financial year containing the date it is asked for (see
    /// <c>AccountingReportService.FinancialYearStartForAsync</c>), so a migration carrying five years
    /// of history would reconcile against one of them and report a difference that is not real.
    /// </para>
    /// <para>
    /// Like every other reporting query it reads through the current-revision predicate. An
    /// aggregation that forgot it would count an altered voucher twice — and the number it produced
    /// would then disagree with the import for a reason that has nothing to do with the import.
    /// </para>
    /// </remarks>
    public static readonly SqlQueryKey JournalTotals = new("Interop.JournalTotals");
}
