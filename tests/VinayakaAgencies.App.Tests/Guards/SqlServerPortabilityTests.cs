using System.IO;
using System.Text.RegularExpressions;
using VinayakaAgencies.App.Tests.TestSupport;
using Shouldly;

namespace VinayakaAgencies.App.Tests.Guards;

/// <summary>
/// How much of the reporting SQL is SQLite-only, and therefore how big the SQL Server port is.
/// </summary>
/// <remarks>
/// `phase-9-multiuser.md` §2 asks a question rather than only setting a task: the provider swap
/// "should be a provider swap plus a migration set, not a rewrite. If it turns out to be a rewrite,
/// that is a Phase 0 defect worth recording as an ADR." This answers it, and keeps the answer true.
/// <para>
/// The measurement, taken against the statement sources as they stand: <b>eight occurrences across
/// four construct kinds, in roughly seven hundred lines of SQL.</b> Everything else — the CTEs, the
/// <c>COALESCE</c>s, the <c>CAST</c>s, the date comparisons against <c>yyyy-MM-dd</c> text — is
/// ANSI and ports unchanged. The abstraction held; this is a swap.
/// </para>
/// <para>
/// <b>What this test is for.</b> Not to forbid SQLite-specific SQL — sometimes it is the right
/// statement to write — but to keep the cost of the port visible while it is still outstanding
/// (<c>open-questions.md</c> #24). A sixth construct added quietly is a port that grew without
/// anybody deciding it should. Adding one means updating the inventory below, which is the moment
/// to notice.
/// </para>
/// <para>
/// The constructs that must stay <i>absent</i> are listed too, and they matter more than the ones
/// present: a date computed with <c>strftime</c> or <c>julianday</c> has no mechanical translation,
/// and would turn the port from a swap into the rewrite §2 warns about.
/// </para>
/// </remarks>
public sealed class SqlServerPortabilityTests
{
    /// <summary>
    /// The SQLite-only constructs the statement sources currently use, and what each becomes.
    /// </summary>
    private static readonly (string Name, string Pattern, int Expected, string TSqlEquivalent)[] Inventory =
    [
        ("string concatenation", @"\|\|", 4, "the + operator, or CONCAT()"),
        ("LIMIT", @"\bLIMIT\b", 2, "OFFSET 0 ROWS FETCH NEXT n ROWS ONLY, which needs an ORDER BY"),
        ("substr()", @"\bsubstr\s*\(", 1, "SUBSTRING()"),
        ("length()", @"\blength\s*\(", 1, "LEN()"),
    ];

    /// <summary>
    /// Constructs with no mechanical T-SQL equivalent. One of these appearing changes the shape of
    /// the port, not just its size.
    /// </summary>
    private static readonly string[] MustStayAbsent =
    [
        @"\bstrftime\s*\(",
        @"\bjulianday\s*\(",
        @"\bIFNULL\s*\(",
        @"\bGROUP_CONCAT\s*\(",
        @"\bprintf\s*\(",
        @"\btypeof\s*\(",
        @"\bAUTOINCREMENT\b",
        @"\sGLOB\s",
        @"\bdatetime\s*\(",
    ];

    [Fact]
    public void ReportingSql_UsesExactlyTheSqliteOnlyConstructsTheInventoryRecords()
    {
        var sql = ReadAllStatementSources();
        var drifted = new List<string>();

        foreach (var (name, pattern, expected, equivalent) in Inventory)
        {
            var found = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase).Count;

            if (found != expected)
            {
                drifted.Add(
                    $"{name}: expected {expected}, found {found} (T-SQL: {equivalent})");
            }
        }

        drifted.ShouldBeEmpty(
            "the SQL Server port's cost changed. Update the inventory in this test and in "
            + $"specs/evidence/phase-9-evidence.md: {string.Join("; ", drifted)}");
    }

    [Fact]
    public void ReportingSql_UsesNoConstructWithoutATSqlEquivalent()
    {
        // These are the ones that would make the port a rewrite rather than a swap, which §2 says
        // would be a Phase 0 defect. None are present, and that is worth keeping true.
        var sql = ReadAllStatementSources();

        var offenders = MustStayAbsent
            .Where(pattern => Regex.IsMatch(sql, pattern, RegexOptions.IgnoreCase))
            .ToList();

        offenders.ShouldBeEmpty(
            "these have no mechanical T-SQL translation and would change the shape of the Phase 9 "
            + $"port: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ReportingSql_IsStillSmallEnoughForTheMeasurementToMeanSomething()
    {
        // The inventory above is only reassuring in proportion. "Eight divergences" says nothing
        // without knowing it is eight out of hundreds of lines rather than eight out of ten.
        var lines = StatementSourceFiles()
            .Sum(file => File.ReadAllLines(file).Length);

        lines.ShouldBeGreaterThan(500);
    }

    private static string ReadAllStatementSources() =>
        string.Join("\n", StatementSourceFiles().Select(File.ReadAllText));

    private static IEnumerable<string> StatementSourceFiles() =>
        Directory.EnumerateFiles(
            Path.Combine(RepoRoot.Find(), "src", "VinayakaAgencies.Infrastructure", "Data"),
            "*SqliteStatements.cs");
}
