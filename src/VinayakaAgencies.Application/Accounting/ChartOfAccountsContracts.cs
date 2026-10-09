using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Read model for an account group, carrying its depth so a tree can be rendered without a
/// second query.
/// </summary>
public sealed record AccountGroupSummary(
    Guid Id,
    string Name,
    Guid? ParentGroupId,
    string MaterialisedPath,
    AccountNature Nature,
    bool AffectsGrossProfit,
    bool IsPrimary,
    bool IsSystem,
    bool IsActive,
    int Depth,
    int SortOrder);

/// <summary>Read model for a ledger.</summary>
public sealed record LedgerSummary(
    Guid Id,
    string Name,
    string? Alias,
    Guid AccountGroupId,
    string AccountGroupName,
    AccountNature Nature,
    bool IsSystem,
    bool AllowsManualPosting,
    bool IsActive,
    string? Description);

/// <param name="Alias">Short code used by fast search; optional.</param>
public sealed record CreateLedgerRequest(
    string Name,
    Guid AccountGroupId,
    string? Alias = null,
    string? Description = null);

public sealed record UpdateLedgerRequest(
    Guid Id,
    string Name,
    Guid AccountGroupId,
    string? Alias = null,
    string? Description = null);

public sealed record CreateAccountGroupRequest(
    string Name,
    Guid ParentGroupId,
    int SortOrder = 0);

/// <summary>
/// Chart-of-accounts masters: groups and the ledgers that hang off them.
/// </summary>
/// <remarks>
/// A group's <see cref="AccountNature"/> is inherited from its parent rather than chosen — a
/// sub-group of Current Assets that claimed to be Income would put balances on the wrong statement,
/// so the choice is not offered.
/// </remarks>
public interface IChartOfAccountsService
{
    Task<IReadOnlyList<AccountGroupSummary>> ListGroupsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LedgerSummary>> ListLedgersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ledgers under Bank Accounts / Bank OD A/c — whether a ledger "is a bank" is derived from its
    /// group's position in the chart, never stored on the ledger (spec §5).
    /// </summary>
    Task<IReadOnlyList<LedgerSummary>> ListBankLedgersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<LedgerSummary?> GetLedgerAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LedgerSummary> CreateLedgerAsync(
        CreateLedgerRequest request,
        CancellationToken cancellationToken = default);

    Task<LedgerSummary> UpdateLedgerAsync(
        UpdateLedgerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deactivates a ledger, hiding it from pickers while keeping its history (spec §4.6).</summary>
    Task DeactivateLedgerAsync(Guid id, CancellationToken cancellationToken = default);

    Task ActivateLedgerAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes a ledger. Permitted only when nothing references it; writes a full
    /// before-image to the edit log so the row is reconstructible from the log alone.
    /// </summary>
    Task DeleteLedgerAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AccountGroupSummary> CreateGroupAsync(
        CreateAccountGroupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a group. System groups may be renamed; their position may not change.</summary>
    Task<AccountGroupSummary> RenameGroupAsync(
        Guid id,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-parents a group and rebuilds the materialised path of it and every descendant, in one
    /// unit of work (spec §4.2, checkpoint C1.14).
    /// </summary>
    /// <remarks>
    /// <see cref="AccountGroup.MoveTo"/> and <see cref="AccountGroup.SetMaterialisedPath"/> have
    /// always documented that the caller must rebuild the affected subtree's paths in the same
    /// transaction — and until this method there was no caller at all, so the obligation was
    /// unfulfilled and C1.14's invariant was untestable through the application. The path is what
    /// every balance roll-up range-scans, so a subtree left with stale paths reads its balances
    /// from the wrong branch of the chart.
    /// <para>
    /// Refused when the group is a system group, when the destination is the group itself or one of
    /// its own descendants, when the destination's <see cref="AccountGroup.Nature"/> differs (that
    /// would move balances between the P&amp;L and the balance sheet silently), when a sibling of
    /// the destination already carries the name, or when the move would nest deeper than
    /// <see cref="AccountGroup.MaxDepth"/>.
    /// </para>
    /// <para>
    /// No screen calls this yet; Phase 1 specifies no re-parent UI. It exists so the invariant has
    /// an implementation to be asserted against rather than a comment asking for one.
    /// </para>
    /// </remarks>
    Task<AccountGroupSummary> MoveGroupAsync(
        Guid id,
        Guid newParentGroupId,
        CancellationToken cancellationToken = default);
}
