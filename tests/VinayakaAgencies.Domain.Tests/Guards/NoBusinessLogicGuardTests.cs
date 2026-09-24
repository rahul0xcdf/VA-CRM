using System.Text.RegularExpressions;
using VinayakaAgencies.Domain.Tests.TestSupport;

namespace VinayakaAgencies.Domain.Tests.Guards;

/// <summary>
/// C0.8 (Phase 0) — "No business logic leaked in", updated for Phases 1 through 7.
/// </summary>
/// <remarks>
/// Phase 0's version of this guard asserted the domain contained exactly one entity, <c>Company</c>,
/// and that no voucher/ledger/stock/GST/pricing type existed anywhere. Phase 1 (spec
/// <c>specs/phase-1-accounting-core.md</c> §5) legitimately adds the chart-of-accounts, journal and
/// numbering entities plus the audit log; Phase 2 (spec <c>specs/phase-2-inventory.md</c> §1,
/// Approved 22 Aug 2026) adds the inventory masters and the unified stock ledger; Phase 3 (spec
/// <c>specs/phase-3-sales-purchase-pipeline.md</c>, Approved 22 Aug 2026) adds the trade documents
/// and print templates; Phase 4 (spec <c>specs/phase-4-customer-pricing.md</c>, Approved 23 Aug 2026)
/// adds the customer master, its category and the price lines — which is why "PriceList" stays on
/// the forbidden list as an exact name: the approved types are named
/// <c>CategoryPriceListLine</c>/<c>CustomerSpecialPrice</c>, not <c>PriceList</c>. A guard that
/// forbade a phase's own approved concepts would fail correctly-scoped work.
/// <para>
/// What must not change is the *reason* the guard exists: a later phase's concept must not leak in
/// early. So this file is updated per phase, never deleted — <see cref="ApprovedEntityTypeNames"/>
/// carries the closed set each approved spec added, so an unapproved Phase 5+ entity (a GST rate
/// row, an HSN code table) dropped into <c>src/VinayakaAgencies.Domain</c> still fails the build.
/// </para>
/// </remarks>
public class NoBusinessLogicGuardTests
{
    /// <summary>
    /// Type-name fragments that still belong to Phase 2+ (stock, GST, pricing, sales documents).
    /// Matched as whole C# type declarations, not substrings, so that an unrelated identifier
    /// containing one of these words as a fragment does not produce a false positive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Voucher</c>, <c>Ledger</c> and <c>JournalLine</c> were forbidden under Phase 0 and are
    /// removed here because Phase 1 was approved to introduce exactly those concepts (spec §5).
    /// <c>JournalEntry</c> stays forbidden even though it is a Phase 1 concept in spirit — the actual
    /// type is named <c>JournalLine</c> (spec §5, ADR-0002), and "JournalEntry" appearing anywhere
    /// would mean someone invented a second, unapproved name for the same thing.
    /// </para>
    /// <para>
    /// <c>TdsSection</c> and <c>TcsSection</c> were forbidden until Phase 7 approved them
    /// (<c>specs/phase-7-tds-payroll-banking.md</c>, Approved 18 Sep 2026) and are removed here. The
    /// implementation uses neither name — one <c>TaxSection</c> master carries both kinds, as
    /// <c>HsnSac</c> carries HSN and SAC — but leaving them forbidden after their phase is approved
    /// would make the guard assert a scope rule that no longer exists. <c>TaxRate</c> stays forbidden
    /// as an exact name: the approved type is <c>TaxRateSchedule</c>, and a bare <c>TaxRate</c>
    /// appearing anywhere would mean a rate had been modelled without its effective dates, which is
    /// precisely what checkpoint C7.4 forbids.
    /// </para>
    /// </remarks>
    private static readonly string[] ForbiddenTypeNames =
    [
        "JournalEntry",
        "StockItem",
        "Invoice",
        "GstRate",
        "GstType",
        "PriceList",
        "TaxRate",
        "PurchaseOrder",
        "SalesOrder",
        "Quotation",
    ];

    /// <summary>
    /// The closed set of types deriving directly from <c>Entity</c> that Phases 0 through 8 were
    /// approved to add (Phase 0: <c>Company</c>; Phase 1: spec §5's entity table; Phase 2:
    /// <c>specs/phase-2-inventory.md</c> §1 as approved; Phase 3:
    /// <c>specs/phase-3-sales-purchase-pipeline.md</c> §1–§2 — trade documents and print
    /// templates; Phase 4: <c>specs/phase-4-customer-pricing.md</c> §2 — customers, categories,
    /// price lines and sales settings; Phase 5-6: GST, e-invoicing and e-way bill;
    /// Phase 7: <c>specs/phase-7-tds-payroll-banking.md</c> §2 — the TDS/TCS section master, its
    /// dated rate schedules, payee profiles, Section 197 certificates, posted deductions and the
    /// settings row carrying the 194Q/206C(1H) flags; Phase 8:
    /// <c>specs/phase-8-interoperability.md</c> §2 — the saved Excel column mapping and its columns,
    /// which are the only rows that phase persists: every other part of it reads and writes files).
    /// Deliberately a fixed list, not "however many exist today" —
    /// a new entry here is a spec change, not a test update to follow the code.
    /// </summary>
    private static readonly string[] ApprovedEntityTypeNames =
    [
        "AccountGroup",
        "BankReconciliationMark",
        "CategoryPriceListLine",
        "Company",
        "CostCategory",
        "CostCentre",
        "Customer",
        "CustomerCategory",
        "CustomerSpecialPrice",
        "EinvoiceApiCall",
        "EinvoiceRegistration",
        "EinvoiceSettings",
        "EwayBill",
        "ExcelColumnMapping",
        "ExcelColumnMappingColumn",
        "FinancialYear",
        "GstRateSchedule",
        "GstSettings",
        "HsnSac",
        "Godown",
        "InventorySettings",
        "Item",
        "ItemKitComponent",
        "JournalLine",
        "Ledger",
        "LowerDeductionCertificate",
        "Employee",
        "PayHead",
        "PaymentInstrument",
        "PayrollSettings",
        "ProfessionalTaxSlab",
        "PeriodLock",
        "PrintTemplate",
        "PrintTemplateRevision",
        "SalesSettings",
        "SerialNumber",
        "StockGroup",
        "StockLedgerEntry",
        "StockVoucher",
        "SalaryStructureLine",
        "SalaryVoucher",
        "SalaryVoucherLine",
        "StatutoryRateSchedule",
        "StockVoucherLine",
        "TaxDeduction",
        "TaxPayeeProfile",
        "TaxRateSchedule",
        "TaxSection",
        "TdsSettings",
        "TradeDocument",
        "TradeDocumentLine",
        "UnitOfMeasure",
        "Voucher",
        "VoucherNumberSequence",
        "VoucherNumberSeries",
        "VoucherRevision",
        "VoucherType",
    ];

    private static readonly Regex TypeDeclarationPattern = new(
        @"\b(class|record|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches "class Foo : Entity" / "class Foo : Entity, ISomething" directly — deliberately
    /// narrower than matching any ": Entity" substring, which also fires on the unrelated
    /// "where TEntity : Entity" generic constraint used by IRepository&lt;T&gt;, Specification&lt;T&gt;
    /// etc.
    /// </summary>
    private static readonly Regex ClassDerivingFromEntity = new(
        @"class\s+\w+\s*:\s*Entity\b", RegexOptions.Compiled);

    private static string SrcDirectory => Path.Combine(RepoRoot.Find(), "src");

    [Fact]
    public void EntitiesDirectory_ContainsExactlyOneFile_CompanyCs()
    {
        // Phase 1's entities live in Domain/Accounting and Domain/Auditing (spec §5), not
        // Domain/Entities, so this Phase 0 assertion is still literally true and is left unchanged.
        var entitiesDirectory = Path.Combine(SrcDirectory, "VinayakaAgencies.Domain", "Entities");

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
            $"Phase 3 forbids GST/pricing types until their phases approve them; found: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EntityTypesInDomain_MatchExactlyThePhase0Through8ApprovedSet()
    {
        // Belt-and-braces version of the two checks above: verifies the guard would also catch a
        // business entity dropped in the wrong folder (e.g. Domain/Common), and — unlike the Phase 0
        // version of this test, which asserted a single fixed name — verifies the *set* is exactly
        // what the approved phase specs added, so an entity from a not-yet-approved phase (e.g. a
        // hypothetical `TdsSection : Entity` from Phase 7) still fails even though it does not
        // collide with any name in ForbiddenTypeNames.
        var domainDirectory = Path.Combine(SrcDirectory, "VinayakaAgencies.Domain");
        var entityDeclaringFiles = new List<string>();

        foreach (var file in Directory.GetFiles(domainDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);

            if (ClassDerivingFromEntity.IsMatch(content))
            {
                entityDeclaringFiles.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        entityDeclaringFiles
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(ApprovedEntityTypeNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void AuditLogEntry_IsNotAmongTheEntityDerivedTypes()
    {
        // Restates C1.4(a) from the guard-file angle rather than the type-system angle covered in
        // AuditLogEntryTests: a source scan for "class X : Entity" must never find AuditLogEntry,
        // because if it ever did, Repository<AuditLogEntry>() would compile and every caller would
        // gain an Update/Remove path onto the audit log.
        ApprovedEntityTypeNames.ShouldNotContain("AuditLogEntry");

        var auditLogFile = Path.Combine(SrcDirectory, "VinayakaAgencies.Domain", "Auditing", "AuditLogEntry.cs");
        File.Exists(auditLogFile).ShouldBeTrue($"expected to find {auditLogFile}");

        var content = File.ReadAllText(auditLogFile);
        ClassDerivingFromEntity.IsMatch(content).ShouldBeFalse(
            "AuditLogEntry.cs must not declare 'class AuditLogEntry : Entity' — see AuditLogEntryTests " +
            "for the reflection-based version of this same guarantee.");
    }
}
