using System.IO;
using System.Reflection;
using VinayakaAgencies.App.Tests.TestSupport;
using VinayakaAgencies.Application.Reporting;

namespace VinayakaAgencies.App.Tests.Guards;

/// <summary>
/// The scan halves of Phase 1 checkpoints C1.11, C1.13 and C1.15 — each specified in
/// `specs/phase-1-accounting-core.md` §14 and each previously unwritten, which is why all three
/// stood at "partially met" in `specs/evidence/phase-1-evidence.md`.
/// </summary>
/// <remarks>
/// These are the assertions that cannot be made by exercising behaviour: that a rule has not been
/// broken *anywhere*. Each is deliberately narrow enough to mean something — a scan that fires on
/// "Select a group and type its new name first." or on a millimetre offset would be turned off
/// within a week, and a guard nobody trusts is worse than none.
/// </remarks>
public class Phase1ArchitectureGuardTests
{
    /// <summary>The two folders that hold every screen.</summary>
    private static readonly string[] UiFolders = ["Views", "ViewModels"];

    /// <summary>The two projects that must never contain SQL (ADR-0001).</summary>
    private static readonly string[] SqlFreeProjects =
        ["VinayakaAgencies.Application", "VinayakaAgencies.App"];

    [Fact]
    public void NoSqlStatement_AppearsOutsideTheDialectLayer()
    {
        // C1.11's first half. Matching upper-case SELECT only is what makes this precise: SQL in
        // this codebase is written in upper case (see AccountingSqliteStatements), while the
        // false-positive risk is all prose and member names — "Select a group…", "SelectedRow",
        // ".Select(" — none of which are upper case.
        var offenders = new List<string>();

        foreach (var project in SqlFreeProjects)
        {
            var root = Path.Combine(RepoRoot.Find(), "src", project);

            foreach (var file in EnumerateSourceFiles(root))
            {
                var text = File.ReadAllText(file);

                if (text.Contains("SELECT", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(RepoRoot.Find(), file));
                }
            }
        }

        offenders.ShouldBeEmpty(
            "SQL belongs only in a dialect's statement source, resolved by a named SqlQueryKey "
            + "(ADR-0001) — otherwise the Phase 9 SQL Server move is a hunt through two projects; "
            + $"found SQL in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryAccountingQueryKey_IsUniqueAndNamespaced()
    {
        // The companion to the dialect walk in SqlStatementSourceTests: that one proves every key
        // resolves, this one proves two keys cannot quietly become the same key — which would make
        // one report silently serve another's SQL.
        var names = AccountingQueryKeys.All.Select(key => key.Name).ToList();

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(names.Count);
        names.ShouldAllBe(name => name.StartsWith("Accounting.", StringComparison.Ordinal));
    }

    [Fact]
    public void NoViewOrViewModel_ReferencesCostCentre()
    {
        // C1.13's second half. Cost centres ship as schema with no UI (open-questions.md #12): the
        // nullable column is in the Phase 1 migration because adding a dimension to JournalLines
        // once it holds production rows is far more expensive than carrying an unused column. The
        // moment a screen references one, that decision has been reversed by accident.
        var app = Path.Combine(RepoRoot.Find(), "src", "VinayakaAgencies.App");

        var offenders = UiFolders
            .Select(folder => Path.Combine(app, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory
                .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
            .Where(file => File.ReadAllText(file)
                .Contains("CostCentre", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot.Find(), file))
            .ToList();

        offenders.ShouldBeEmpty(
            "cost centres are schema-only until a phase specifies their UI; "
            + $"found references in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoAccountingOrReportingType_ExposesABinaryFloatingPointNumber()
    {
        // C1.15's second half, by reflection rather than by scanning text — a source scan trips on
        // the word "double-counts" in a comment, which is exactly the kind of noise that gets a
        // guard deleted.
        var assemblies = new[]
        {
            typeof(TrialBalance).Assembly,                                   // Application
            typeof(VinayakaAgencies.Domain.Accounting.JournalLine).Assembly, // Domain
        };

        var offenders = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsAccountingOrReportingType)
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => new { Type = type, Member = member, Kind = TypeOfMember(member) })
                .Where(candidate => candidate.Kind is not null && IsBinaryFloat(candidate.Kind)))
            .Select(candidate => $"{candidate.Type.FullName}.{candidate.Member.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "money and quantity are decimal in the domain and scaled integers in the store "
            + "(ADR-0003); a double anywhere on an accounting or reporting type reintroduces the "
            + $"rounding this project went to some trouble to remove; found: {string.Join(", ", offenders)}");

        // The scan has to have found the accounting surface, or it proved nothing.
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Count(IsAccountingOrReportingType)
            .ShouldBeGreaterThan(10);
    }

    [Fact]
    public void TheDoublesThatDoExist_AreOnlyTheTwoNonMonetaryOnes()
    {
        // Documented rather than merely excluded, so the next reader knows these were considered.
        // `PrintFieldPlacement` carries millimetres and a font size — positions on paper, where a
        // double is the right type — and `InteropImportProgress.Fraction` is a progress-bar ratio.
        // Neither is money and neither sits on an accounting or reporting type. If a third
        // appears, this test names it and someone has to decide deliberately.
        var offenders = typeof(TrialBalance).Assembly
            .GetTypes()
            .Where(type => type.Namespace is not null && !IsAccountingOrReportingType(type))
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(member => new { Type = type, Member = member, Kind = TypeOfMember(member) })
                .Where(candidate => candidate.Kind is not null && IsBinaryFloat(candidate.Kind)))
            .Select(candidate => $"{candidate.Type.Name}.{candidate.Member.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBe(
            [
                "InteropImportProgress.Fraction",
                "PrintFieldPlacement.FontSize",
                "PrintFieldPlacement.LeftMm",
                "PrintFieldPlacement.TopMm",
                "PrintFieldPlacement.WidthMm",
            ],
            "the only binary floating-point members outside accounting are paper positions and a "
            + "progress ratio; anything else needs a decision, not a default");
    }

    // ------------------------------------------------------------------------------- helpers

    private static bool IsAccountingOrReportingType(Type type) =>
        type.Namespace is { } ns
        && (ns.EndsWith(".Accounting", StringComparison.Ordinal)
            || ns.EndsWith(".Reporting", StringComparison.Ordinal)
            || ns.EndsWith(".Auditing", StringComparison.Ordinal));

    private static bool IsBinaryFloat(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(double) || underlying == typeof(float);
    }

    private static Type? TypeOfMember(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => null,
    };

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
