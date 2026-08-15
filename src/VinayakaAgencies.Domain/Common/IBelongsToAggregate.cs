namespace VinayakaAgencies.Domain.Common;

/// <summary>
/// Declares the aggregate a child entity belongs to, for the audit trail.
/// </summary>
/// <remarks>
/// <c>specs/phase-1-accounting-core.md</c> §11.1 requires every edit-log row to carry "the owning
/// aggregate type and id (so a <c>JournalLine</c> change is findable from its voucher)". The
/// persistence layer can infer that relationship on its own only for EF-owned types; for an
/// ordinary child entity with several foreign keys — a journal line references its revision, its
/// ledger, its cost centre and its company — no general rule picks the right one, and guessing
/// would put a wrong aggregate on an audit row, which is worse than an empty one.
/// <para>
/// So the entity states it. This lives on the entity rather than in a registry or an attribute for
/// the same reason §11.2 rejects an optional interceptor: an implemented interface is visible to
/// the compiler and to a reviewer, whereas a registration can be dropped by a future edit and
/// nothing complains.
/// </para>
/// </remarks>
public interface IBelongsToAggregate
{
    /// <summary>Type name of the aggregate root this entity's history belongs under.</summary>
    string AggregateType { get; }

    /// <summary>Identifier of the aggregate root, formatted as the audit trail records ids.</summary>
    string AggregateId { get; }
}
