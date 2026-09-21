using VinayakaAgencies.Domain.Printing;

namespace VinayakaAgencies.Domain.Tests.Printing;

/// <summary>
/// Template identity and versioning guards: versions append one at a time, the current-revision
/// pointer follows, and selection data (priority, active) stays sane
/// (phase-3-sales-purchase-pipeline.md §2).
/// </summary>
public class PrintTemplateTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void Constructor_NegativePriority_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewTemplate(priority: -1));
    }

    [Fact]
    public void Constructor_BlankName_Throws()
    {
        Should.Throw<ArgumentException>(() => NewTemplate(priority: 10, name: "   "));
    }

    [Fact]
    public void AttachRevision_AdvancesOneVersionAtATime()
    {
        var template = NewTemplate(priority: 10);

        template.AttachRevision(Guid.NewGuid(), version: 1);
        template.CurrentVersion.ShouldBe(1);

        template.AttachRevision(Guid.NewGuid(), version: 2);
        template.CurrentVersion.ShouldBe(2);
        template.CurrentRevisionId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void AttachRevision_SkippingAVersion_IsRefused()
    {
        var template = NewTemplate(priority: 10);
        template.AttachRevision(Guid.NewGuid(), version: 1);

        var refusal = Should.Throw<InvalidOperationException>(
            () => template.AttachRevision(Guid.NewGuid(), version: 3));

        refusal.Message.ShouldContain("one at a time");
    }

    [Fact]
    public void Rename_UpdatesTheNormalisedFormUsedForUniqueness()
    {
        var template = NewTemplate(priority: 10, name: "Tax Invoice — Detailed");

        template.Rename("Tax Invoice — Site Copy");

        template.Name.ShouldBe("Tax Invoice — Site Copy");
        template.NameNormalised.ShouldNotBe(NameOf("Tax Invoice — Detailed"));
        template.NameNormalised.ShouldBe(NameOf("Tax Invoice — Site Copy"));

        static string NameOf(string name) =>
            VinayakaAgencies.Domain.Common.NameNormaliser.Normalise(name);
    }

    [Fact]
    public void DeactivateAndActivate_FlipTheSelectionFlag()
    {
        var template = NewTemplate(priority: 20);

        template.IsActive.ShouldBeTrue();

        template.Deactivate();
        template.IsActive.ShouldBeFalse();

        template.Activate();
        template.IsActive.ShouldBeTrue();
    }

    private static PrintTemplate NewTemplate(int priority, string? name = null) =>
        new(
            CompanyId,
            PrintDocumentKind.TaxInvoice,
            name ?? "Tax Invoice — Detailed",
            priority,
            description: null);
}
