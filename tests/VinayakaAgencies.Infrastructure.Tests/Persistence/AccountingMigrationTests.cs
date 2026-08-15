using Microsoft.Data.Sqlite;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// The <c>AddAccountingCore</c> migration, applied to a real, empty SQLite file.
/// </summary>
/// <remarks>
/// A migration that has only ever been generated has not been tested. These assertions read the
/// schema back out of <c>sqlite_master</c> after the migration has run, rather than reading the EF
/// model — the model is what we asked for, and the database is what we got.
/// <para>
/// Covers checkpoint C1.13's migration half (cost-centre schema exists, journal-line column is
/// nullable) and the index list spec §5 tabulates.
/// </para>
/// </remarks>
public sealed class AccountingMigrationTests
{
    /// <summary>The column-name terms §8.3 promises a reviewer will not find in this schema.</summary>
    private static readonly string[] StoredBalanceNames = ["Balance", "ClosingBal", "Total"];

    [Fact]
    public async Task MigrateAsync_OnFreshDatabase_AppliesAddAccountingCore()
    {
        await using var host = new SqliteTestHost();

        var applied = await host.MigrateAsync();

        applied.ShouldContain(name => name.EndsWith("AddAccountingCore", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("AccountGroups")]
    [InlineData("Ledgers")]
    [InlineData("FinancialYears")]
    [InlineData("PeriodLocks")]
    [InlineData("VoucherTypes")]
    [InlineData("VoucherNumberSeries")]
    [InlineData("VoucherNumberSequences")]
    [InlineData("Vouchers")]
    [InlineData("VoucherRevisions")]
    [InlineData("JournalLines")]
    [InlineData("CostCategories")]
    [InlineData("CostCentres")]
    [InlineData("BankReconciliationMarks")]
    public async Task AddAccountingCore_OnFreshDatabase_CreatesTheAccountingTable(string tableName)
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var tables = await ReadNamesAsync(host.DatabasePath, "table");

        tables.ShouldContain(tableName);
    }

    [Theory]
    [InlineData("IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised")]
    [InlineData("IX_AccountGroups_CompanyId_MaterialisedPath")]
    [InlineData("IX_Ledgers_CompanyId_NameNormalised")]
    [InlineData("IX_FinancialYears_CompanyId_StartDate")]
    [InlineData("IX_PeriodLocks_CompanyId")]
    [InlineData("IX_VoucherTypes_CompanyId_NameNormalised")]
    [InlineData("IX_VoucherNumberSequences_SeriesId_FinancialYearId")]
    [InlineData("IX_VoucherRevisions_VoucherId_RevisionNumber")]
    [InlineData("IX_VoucherRevisions_CompanyId_VoucherTypeId_SeriesId_FinancialYearId_NumberNormalised")]
    [InlineData("IX_JournalLines_CompanyId_LedgerId")]
    [InlineData("IX_JournalLines_VoucherRevisionId_LineNumber")]
    [InlineData("IX_BankReconciliationMarks_JournalLineId")]
    public async Task AddAccountingCore_CreatesTheIndexTheSchemaDeclares(string indexName)
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var indexes = await ReadNamesAsync(host.DatabasePath, "index");

        indexes.ShouldContain(indexName);
    }

    [Theory]
    [InlineData("IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised")]
    [InlineData("IX_Ledgers_CompanyId_NameNormalised")]
    [InlineData("IX_FinancialYears_CompanyId_StartDate")]
    [InlineData("IX_PeriodLocks_CompanyId")]
    [InlineData("IX_VoucherTypes_CompanyId_NameNormalised")]
    [InlineData("IX_VoucherNumberSequences_SeriesId_FinancialYearId")]
    [InlineData("IX_VoucherRevisions_VoucherId_RevisionNumber")]
    [InlineData("IX_BankReconciliationMarks_JournalLineId")]
    public async Task AddAccountingCore_MakesTheDeclaredUniqueIndexesUnique(string indexName)
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var sql = await ReadIndexSqlAsync(host.DatabasePath, indexName);

        // A non-unique index would let a duplicate name, a duplicate financial year or a second
        // period lock through, and every one of those is a defect the schema is supposed to make
        // impossible rather than one the application has to remember to check.
        sql.ShouldStartWith("CREATE UNIQUE INDEX");
    }

    [Fact]
    public async Task AddAccountingCore_CreatesTheCurrentRevisionNumberIndexAsAPartialUniqueIndex()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var sql = await ReadIndexSqlAsync(
            host.DatabasePath,
            "IX_VoucherRevisions_CompanyId_VoucherTypeId_SeriesId_FinancialYearId_NumberNormalised");

        sql.ShouldStartWith("CREATE UNIQUE INDEX");

        // Filtered to the current revision (spec §7.2, §8.2). Without the filter a superseded
        // revision would still occupy the number and an alteration could never be saved; without
        // uniqueness, two live vouchers could share an invoice number, which is a GST problem.
        sql.ShouldContain("[SupersededByRevisionId] IS NULL");
    }

    [Fact]
    public async Task AddAccountingCore_ShipsCostCentreSchemaWithANullableJournalLineColumn()
    {
        // Checkpoint C1.13, migration half: decision D3 puts the cost-centre schema in Phase 1 and
        // the UI in a later phase, because adding a dimension column to a populated JournalLines
        // table means a full-table copy under SQLite.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var tables = await ReadNamesAsync(host.DatabasePath, "table");

        tables.ShouldContain("CostCategories");
        tables.ShouldContain("CostCentres");

        var costCentreId = await ReadColumnAsync(host.DatabasePath, "CostCentreId");

        costCentreId.ShouldNotBeNull();
        costCentreId.Value.IsNotNull.ShouldBeFalse(
            "JournalLine.CostCentreId must be nullable: no Phase 1 posting sets it (D3).");
    }

    [Fact]
    public async Task AddAccountingCore_CreatesNoColumnHoldingABalanceOrATotal()
    {
        // Checkpoint C1.12 against the database rather than the model. §8.3 promises a reviewer can
        // check the "balances are always derived" claim in one step, by looking for a *Balance* or
        // *Total* column and finding none.
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();

        var offenders = await ReadStoredBalanceColumnsAsync(host.DatabasePath);

        offenders.ShouldBeEmpty(
            "No table may hold an independently-writable balance or total; every reported figure is "
            + "a SUM over JournalLine (spec §8.3).");
    }

    private static async Task<IReadOnlyList<string>> ReadNamesAsync(string databasePath, string objectType)
    {
        await using var connection = await OpenAsync(databasePath);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type;";
        command.Parameters.AddWithValue("$type", objectType);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<string> ReadIndexSqlAsync(string databasePath, string indexName)
    {
        await using var connection = await OpenAsync(databasePath);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name;";
        command.Parameters.AddWithValue("$name", indexName);

        var sql = await command.ExecuteScalarAsync();

        sql.ShouldNotBeNull($"Index '{indexName}' does not exist.");

        return (string)sql;
    }

    private static async Task<(string Name, bool IsNotNull)?> ReadColumnAsync(
        string databasePath,
        string columnName)
    {
        await using var connection = await OpenAsync(databasePath);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('JournalLines');";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return (reader.GetString(1), reader.GetInt32(3) == 1);
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> ReadStoredBalanceColumnsAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath);

        var tables = new List<string>();

        await using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

            await using var tableReader = await tableCommand.ExecuteReaderAsync();
            while (await tableReader.ReadAsync())
            {
                tables.Add(tableReader.GetString(0));
            }
        }

        var offenders = new List<string>();

        foreach (var table in tables)
        {
            await using var columnCommand = connection.CreateCommand();

            // pragma_table_info is the table-valued form of PRAGMA table_info, so the table name
            // travels as a bound parameter rather than being concatenated into the statement.
            columnCommand.CommandText = "SELECT name FROM pragma_table_info($table);";
            columnCommand.Parameters.AddWithValue("$table", table);

            await using var columnReader = await columnCommand.ExecuteReaderAsync();
            while (await columnReader.ReadAsync())
            {
                var column = columnReader.GetString(0);

                if (StoredBalanceNames.Any(term =>
                        column.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{table}.{column}");
                }
            }
        }

        return offenders;
    }

    private static async Task<SqliteConnection> OpenAsync(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        return connection;
    }
}
