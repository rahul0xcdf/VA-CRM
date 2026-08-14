using System.Text.RegularExpressions;
using MotorPumpSuite.Domain.Tests.TestSupport;

namespace MotorPumpSuite.Domain.Tests.Guards;

/// <summary>
/// C0.8 — "No business logic leaked in". phase-0-foundations.md §1 is explicit: "Phase 0 writes no
/// business logic. No vouchers, no stock, no GST, no pricing." <c>Company</c> is the one entity
/// Phase 0 is allowed. This guard exists so that scope creep — someone reaching for a
/// <c>StockItem</c> or a <c>Voucher</c> class because a later phase's concept felt convenient to
/// bolt on early — fails the build instead of quietly landing on <c>main</c>.
/// </summary>
public class NoBusinessLogicGuardTests
{
    /// <summary>
    /// Type-name fragments that belong to Phase 1+ (double-entry, stock, GST, pricing). Matched as
    /// whole C# type declarations, not substrings, so that an unrelated identifier containing one
    /// of these words as a fragment does not produce a false positive.
    /// </summary>
    private static readonly string[] ForbiddenTypeNames =
    [
        "Voucher",
        "Ledger",
        "JournalLine",
        "JournalEntry",
        "StockItem",
        "Invoice",
        "GstRate",
        "GstType",
        "PriceList",
        "TaxRate",
        "TdsSection",
        "TcsSection",
        "PurchaseOrder",
        "SalesOrder",
        "Quotation",
    ];

    private static readonly Regex TypeDeclarationPattern = new(
        @"\b(class|record|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static string SrcDirectory => Path.Combine(RepoRoot.Find(), "src");

    [Fact]
    public void EntitiesDirectory_ContainsExactlyOneFile_CompanyCs()
    {
        var entitiesDirectory = Path.Combine(SrcDirectory, "MotorPumpSuite.Domain", "Entities");

        var files = Directory.GetFiles(entitiesDirectory, "*.cs");

        files.Length.ShouldBe(1, $"expected exactly one entity file (Company.cs); found: {string.Join(", ", files.Select(Path.GetFileName))}");
        Path.GetFileName(files[0]).ShouldBe("Company.cs");
    }

    [Fact]
    public void SourceTree_ContainsNoTypeDeclarationsNamedAfterForbiddenBusinessConcepts()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(SrcDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);

            foreach (Match match in TypeDeclarationPattern.Matches(content))
            {
                var typeName = match.Groups[2].Value;

                if (ForbiddenTypeNames.Any(forbidden =>
                        string.Equals(typeName, forbidden, StringComparison.Ordinal)))
                {
                    offenders.Add($"{typeName} in {Path.GetRelativePath(SrcDirectory, file)}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"Phase 0 forbids voucher/stock/GST/pricing types; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void OnlyEntityInDomain_IsCompany()
    {
        // Belt-and-braces version of the directory-listing check above: verifies the guard would
        // also catch a business entity dropped in the wrong folder (e.g. Domain/Common) rather than
        // relying solely on the Entities/ directory being the sole place anyone would think to look.
        var domainDirectory = Path.Combine(SrcDirectory, "MotorPumpSuite.Domain");
        var entityDeclaringFiles = new List<string>();

        // Matches "class Foo : Entity" / "class Foo : Entity, ISomething" directly — deliberately
        // narrower than matching any ": Entity" substring, which also fires on the unrelated
        // "where TEntity : Entity" generic constraint used by IRepository<T>, Specification<T> etc.
        var classDerivingFromEntity = new Regex(@"class\s+\w+\s*:\s*Entity\b", RegexOptions.Compiled);

        foreach (var file in Directory.GetFiles(domainDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);

            if (classDerivingFromEntity.IsMatch(content))
            {
                entityDeclaringFiles.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        entityDeclaringFiles.ShouldBe(["Company"]);
    }
}
