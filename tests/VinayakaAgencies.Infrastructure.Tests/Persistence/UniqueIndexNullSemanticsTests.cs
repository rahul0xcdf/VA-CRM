using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

// Aliased because this assembly has its own Tests.Persistence namespace, which would otherwise win
// the lookup for an unqualified `Persistence.` prefix.
using AppDbContext = VinayakaAgencies.Infrastructure.Persistence.AppDbContext;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// The two model-level guards that keep the Phase 9 provider switch from failing on real data:
/// checkpoint C1.20 (no unique index may depend on NULL semantics) and checkpoint C1.17
/// (<c>SupersededByRevisionId</c> deliberately carries no foreign key).
/// </summary>
/// <remarks>
/// Both are here rather than in <see cref="AccountingModelTests"/> because both guard a decision
/// that a well-meaning future contributor would otherwise "tidy up" — and in each case the tidying
/// breaks something that will only be discovered on a provider migration or on the next alteration.
/// </remarks>
public sealed class UniqueIndexNullSemanticsTests
{
    [Fact]
    public async Task Model_EveryUniqueIndex_EitherHasNoNullableKeyColumnOrHasAFilter()
    {
        // Checkpoint C1.20, and the rule spec §5 states for this phase and every later one.
        //
        // SQLite and SQL Server disagree about NULL inside a unique index, in opposite directions
        // from each other and from the SQL standard: SQLite treats two NULLs as distinct and accepts
        // the duplicate rows, SQL Server treats them as equal and rejects them. An unfiltered unique
        // index over a nullable column therefore means one thing through Phase 8 and another from
        // Phase 9 — and the difference surfaces as a CREATE UNIQUE INDEX that fails on production
        // data during the upgrade, which is the worst possible moment to find out.
        //
        // A filter makes the intent explicit and identical on both providers, so the rule is: no
        // nullable key column, or a filter that decides the NULL case.
        var model = await ReadModelAsync();

        var offenders = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetIndexes()
                .Where(index => index.IsUnique)
                .Where(index => index.GetFilter() is null)
                .Where(index => index.Properties.Any(property => property.IsNullable))
                .Select(index => string.Concat(
                    entityType.DisplayName(),
                    " (",
                    string.Join(", ", index.Properties.Select(FormatProperty)),
                    ")")))
            .ToList();

        offenders.ShouldBeEmpty(
            "Every unique index must have either no nullable key column or an explicit filter " +
            "(spec §5, checkpoint C1.20). SQLite treats two NULLs in a unique index as distinct " +
            "and SQL Server treats them as equal, so an unfiltered index over a nullable column " +
            "silently changes meaning at the Phase 9 provider switch. Offenders: " +
            string.Join("; ", offenders));
    }

    [Fact]
    public async Task Model_SupersededByRevisionId_HasNoForeignKey()
    {
        // Checkpoint C1.17. This asserts the *absence* of something, which is unusual, so the
        // reasoning belongs next to the assertion rather than only in the ADR.
        //
        // A successor revision carries the same number as the predecessor it replaces. For as long
        // as both rows have a null SupersededByRevisionId they are both "current", so they collide
        // in the filtered unique index over (CompanyId, VoucherTypeId, SeriesId, FinancialYearId,
        // NumberNormalised). A foreign key on SupersededByRevisionId would force the successor's
        // INSERT to precede the predecessor's UPDATE, because the update depends on the inserted
        // row — and at that instant the index rejects the insert. The reverse order violates the
        // foreign key. Neither provider defers unique-index checking to commit (SQL Server has no
        // deferral at all), so with the foreign key present ADR-0002's central operation cannot be
        // saved in any ordering, by any number of saves.
        //
        // Hence: no foreign key, and the write order is mandated by spec §8.2 instead. See ADR-0002
        // Amendment 1. If this test fails because someone added the "missing" foreign key, the fix
        // is to remove it again, not to weaken the test.
        var model = await ReadModelAsync();

        var revision = model.FindEntityType(typeof(VoucherRevision));
        revision.ShouldNotBeNull();

        var offenders = revision.GetForeignKeys()
            .Where(foreignKey => foreignKey.Properties.Any(property =>
                property.Name == nameof(VoucherRevision.SupersededByRevisionId)))
            .Select(foreignKey => string.Join(
                ", ",
                foreignKey.Properties.Select(property => property.Name)))
            .ToList();

        offenders.ShouldBeEmpty(
            "VoucherRevision.SupersededByRevisionId must carry no foreign key (ADR-0002 " +
            "Amendment 1, spec §8.2). With one declared, the supersede-then-insert alteration " +
            "cannot be saved in any ordering: the successor shares the predecessor's number, so " +
            "both rows collide in the filtered current-revision unique index while both are " +
            "current, and no provider defers that check to commit. Remove the foreign key rather " +
            "than relaxing this assertion.");
    }

    private static string FormatProperty(IProperty property) =>
        property.IsNullable ? string.Concat(property.Name, "?") : property.Name;

    private static async Task<IModel> ReadModelAsync()
    {
        await using var host = new SqliteTestHost();

        return await host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<AppDbContext>().Model));
    }
}
