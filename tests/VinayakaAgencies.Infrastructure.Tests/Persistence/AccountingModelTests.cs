using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

// Aliased because this assembly has its own Tests.Persistence namespace, which would otherwise win
// the lookup for an unqualified `Persistence.` prefix.
using AppDbContext = VinayakaAgencies.Infrastructure.Persistence.AppDbContext;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// Invariants asserted over the EF Core model itself, so a future mapping cannot break them quietly.
/// </summary>
/// <remarks>
/// These are the guards that outlive Phase 1. Each one is a rule that is cheap to hold now and a
/// rewrite to restore later: no stored balance (C1.12, §8.3), no <c>decimal</c> on a TEXT column
/// (ADR-0003's implementation note), and no cascade delete anywhere (§5, ADR-0002).
/// </remarks>
public sealed class AccountingModelTests
{
    /// <summary>The property-name terms checkpoint C1.12 forbids.</summary>
    private static readonly string[] StoredBalanceNames = ["Balance", "ClosingBal", "Total"];

    [Fact]
    public async Task Model_HasNoMappedPropertyNamedLikeAStoredBalance()
    {
        // Checkpoint C1.12. Ledger balances, group balances, the trial balance, the P&L and the
        // balance sheet are all SUM(AmountMinor) over JournalLine (§8.3). A mapped TotalDebit or
        // ClosingBalance would be an independently-writable copy of a derived figure — the one
        // thing that makes an audited set of books unprovable.
        var model = await ReadModelAsync();

        var offenders = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .Select(property => $"{entityType.DisplayName()}.{property.Name}"))
            .Where(name => StoredBalanceNames.Any(term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_MapsNoDecimalPropertyOntoATextAffinityColumn()
    {
        // The enforcement ADR-0003 promised. SQLite has no decimal type: EF maps decimal to TEXT,
        // and SUM() over a TEXT column makes SQLite apply numeric affinity and aggregate in
        // IEEE-754 double. A trial balance computed that way misses zero by a fraction of a paisa,
        // and the only ways to hide that are an epsilon or a display rounding — neither defensible
        // under MCA Rule 3(1). Money is a long of minor units instead.
        var model = await ReadModelAsync();

        var offenders = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .Where(IsDecimalInTheStore)
                .Select(property =>
                    $"{entityType.DisplayName()}.{property.Name} -> {property.GetColumnType()}"))
            .ToList();

        offenders.ShouldBeEmpty(
            "A decimal mapped to a TEXT-affinity column is aggregated in floating point by SQLite "
            + "(ADR-0003). Persist money and quantity as a long of minor units.");
    }

    [Fact]
    public async Task Model_MapsJournalLineAmountOntoAnIntegerColumn()
    {
        // The positive half of the rule above: the one monetary column in the phase is an integer,
        // so Σ AmountMinor == 0 is exact with no tolerance anywhere in the codebase.
        var model = await ReadModelAsync();

        var amount = model.FindEntityType(typeof(JournalLine))
            ?.FindProperty(nameof(JournalLine.AmountMinor));

        amount.ShouldNotBeNull();
        amount.ClrType.ShouldBe(typeof(long));
        amount.GetColumnType().ShouldBe("INTEGER");
    }

    [Fact]
    public async Task Model_ConfiguresEveryRelationshipAsRestrict()
    {
        // Spec §5, without exception. EF defaults a required relationship to Cascade, which would
        // put a cascade-delete path through JournalLine — a delete path for posted lines, which
        // ADR-0002 forbids outright. Restrict on Ledger → JournalLine also gives §4.6's "hard
        // delete only when the row has no journal line" a database-level backstop.
        var model = await ReadModelAsync();

        var offenders = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(foreignKey => foreignKey.DeleteBehavior != DeleteBehavior.Restrict)
            .Select(foreignKey => string.Join(
                    ".",
                    foreignKey.DeclaringEntityType.DisplayName(),
                    string.Join("+", foreignKey.Properties.Select(property => property.Name)))
                + $" -> {foreignKey.PrincipalEntityType.DisplayName()} ({foreignKey.DeleteBehavior})")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_GeneratesNoPrimaryKeyValueInTheStore()
    {
        // ADR-0001 §5: ids are client-generated so a record's GUID survives a Phase 8 Tally export
        // and re-import, which Tally matches on. AuditLogEntry is the one deliberate exception —
        // its monotonic Sequence is what the §11.5 hash chain orders by.
        var model = await ReadModelAsync();

        var offenders = model.GetEntityTypes()
            .Where(entityType => entityType.ClrType != typeof(AuditLogEntry))
            .SelectMany(entityType =>
                (entityType.FindPrimaryKey()?.Properties ?? (IReadOnlyList<IProperty>)[])
                .Where(property => property.ValueGenerated != ValueGenerated.Never)
                .Select(property => $"{entityType.DisplayName()}.{property.Name}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_StoresAccountingEnumsAsTheirNames()
    {
        // Spec §5: a later reordering of an enum must not silently change what an existing row
        // means, and Phase 5 / Phase 8 reporting SQL filters on these values.
        var model = await ReadModelAsync();

        var enums = new (Type EntityType, string PropertyName)[]
        {
            (typeof(AccountGroup), nameof(AccountGroup.Nature)),
            (typeof(VoucherType), nameof(VoucherType.Kind)),
            (typeof(VoucherRevision), nameof(VoucherRevision.Status)),
            (typeof(VoucherNumberSeries), nameof(VoucherNumberSeries.ResetPolicy)),
        };

        foreach (var (entityType, propertyName) in enums)
        {
            var property = model.FindEntityType(entityType)?.FindProperty(propertyName);

            property.ShouldNotBeNull($"{entityType.Name}.{propertyName} is not mapped.");
            property.GetProviderClrType().ShouldBe(
                typeof(string),
                $"{entityType.Name}.{propertyName} must be stored as its name, not its integer.");
        }
    }

    [Fact]
    public async Task Model_IgnoresTheComputedProjectionsOnJournalLineAndVoucherRevision()
    {
        var model = await ReadModelAsync();

        var journalLine = model.FindEntityType(typeof(JournalLine));
        var revision = model.FindEntityType(typeof(VoucherRevision));

        journalLine.ShouldNotBeNull();
        revision.ShouldNotBeNull();

        foreach (var name in new[] { "Amount", "DebitAmount", "CreditAmount", "IsDebit", "IsCredit" })
        {
            journalLine.FindProperty(name).ShouldBeNull(
                $"JournalLine.{name} is computed from AmountMinor and must never become a column.");
        }

        foreach (var name in new[] { "NetMinor", "IsBalanced", "IsCurrent", "TotalDebit", "TotalCredit" })
        {
            revision.FindProperty(name).ShouldBeNull(
                $"VoucherRevision.{name} is computed from the lines and must never become a column.");
        }
    }

    [Fact]
    public async Task Model_ShipsCostCentreSchemaWithANullableJournalLineForeignKey()
    {
        // Checkpoint C1.13, model half. Decision D3: schema now, UI later.
        var model = await ReadModelAsync();

        var costCategory = model.FindEntityType(typeof(CostCategory));
        var costCentre = model.FindEntityType(typeof(CostCentre));

        costCategory.ShouldNotBeNull();
        costCentre.ShouldNotBeNull();

        costCategory.GetTableName().ShouldBe("CostCategories");
        costCentre.GetTableName().ShouldBe("CostCentres");

        var costCentreId = model.FindEntityType(typeof(JournalLine))
            ?.FindProperty(nameof(JournalLine.CostCentreId));

        costCentreId.ShouldNotBeNull();
        costCentreId.IsNullable.ShouldBeTrue();
    }

    /// <summary>
    /// True when the value that reaches the database is a <see cref="decimal"/> and the column has
    /// SQLite TEXT affinity.
    /// </summary>
    /// <remarks>
    /// SQLite assigns TEXT affinity to any declared type containing CHAR, CLOB or TEXT
    /// (https://www.sqlite.org/datatype3.html §3.1).
    /// </remarks>
    private static bool IsDecimalInTheStore(IProperty property)
    {
        var storeType = property.GetProviderClrType() ?? property.ClrType;
        storeType = Nullable.GetUnderlyingType(storeType) ?? storeType;

        if (storeType != typeof(decimal))
        {
            return false;
        }

        var columnType = property.GetColumnType() ?? string.Empty;

        return columnType.Contains("CHAR", StringComparison.OrdinalIgnoreCase)
            || columnType.Contains("CLOB", StringComparison.OrdinalIgnoreCase)
            || columnType.Contains("TEXT", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IModel> ReadModelAsync()
    {
        await using var host = new SqliteTestHost();

        return await host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<AppDbContext>().Model));
    }
}
