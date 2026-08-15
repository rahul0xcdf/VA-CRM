using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>
/// Spec §4.2: <see cref="AccountGroup.MaterialisedPath"/> is a rebuildable cache of the
/// <see cref="AccountGroup.ParentGroupId"/> chain, capped at <see cref="AccountGroup.MaxDepth"/> so
/// the column stays inside SQL Server's 900-byte index key limit at Phase 9.
/// </summary>
public class AccountGroupTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void SetMaterialisedPath_WithParent_ComposesParentThenSelf()
    {
        var parent = new AccountGroup(CompanyId, "Current Assets", AccountNature.Asset);
        parent.SetMaterialisedPath(null);

        var child = new AccountGroup(CompanyId, "Cash-in-Hand", AccountNature.Asset, parent.Id);
        child.SetMaterialisedPath(parent.MaterialisedPath);

        child.MaterialisedPath.ShouldBe($"/{parent.Id:N}/{child.Id:N}/");
        child.MaterialisedPath.ShouldStartWith(parent.MaterialisedPath);
    }

    [Fact]
    public void SetMaterialisedPath_NoParent_IsRootPath()
    {
        var root = new AccountGroup(CompanyId, "Current Assets", AccountNature.Asset);
        root.SetMaterialisedPath(null);

        root.MaterialisedPath.ShouldBe($"/{root.Id:N}/");
    }

    [Fact]
    public void Depth_EachLevelOfNesting_IncrementsByOne()
    {
        AccountGroup? previous = null;

        for (var level = 1; level <= AccountGroup.MaxDepth; level++)
        {
            var group = new AccountGroup(CompanyId, $"Level {level}", AccountNature.Asset, previous?.Id);
            group.SetMaterialisedPath(previous?.MaterialisedPath);

            group.Depth.ShouldBe(level);
            previous = group;
        }
    }

    [Fact]
    public void SetMaterialisedPath_ExceedingMaxDepth_ThrowsInvalidOperationException()
    {
        AccountGroup? previous = null;

        for (var level = 1; level <= AccountGroup.MaxDepth; level++)
        {
            var group = new AccountGroup(CompanyId, $"Level {level}", AccountNature.Asset, previous?.Id);
            group.SetMaterialisedPath(previous?.MaterialisedPath);
            previous = group;
        }

        var overflow = new AccountGroup(CompanyId, "One level too many", AccountNature.Asset, previous!.Id);

        Should.Throw<InvalidOperationException>(() => overflow.SetMaterialisedPath(previous.MaterialisedPath));
    }

    [Fact]
    public void MoveTo_SystemGroup_ThrowsInvalidOperationException()
    {
        var group = new AccountGroup(CompanyId, "Sundry Debtors", AccountNature.Asset, isSystem: true);

        Should.Throw<InvalidOperationException>(() => group.MoveTo(Guid.NewGuid()));
    }

    [Fact]
    public void Deactivate_SystemGroup_ThrowsInvalidOperationException()
    {
        var group = new AccountGroup(CompanyId, "Sundry Debtors", AccountNature.Asset, isSystem: true);

        Should.Throw<InvalidOperationException>(group.Deactivate);
    }

    [Fact]
    public void MoveTo_OwnId_ThrowsInvalidOperationException()
    {
        var group = new AccountGroup(CompanyId, "Miscellaneous", AccountNature.Asset);

        Should.Throw<InvalidOperationException>(() => group.MoveTo(group.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankName_ThrowsArgumentException(string? blankName)
    {
        Should.Throw<ArgumentException>(
            () => new AccountGroup(CompanyId, blankName!, AccountNature.Asset));
    }

    [Fact]
    public void Constructor_EmptyCompanyId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(
            () => new AccountGroup(Guid.Empty, "Current Assets", AccountNature.Asset));
    }

    [Fact]
    public void MoveTo_NonSystemGroup_ReparentsSuccessfully()
    {
        var group = new AccountGroup(CompanyId, "Custom Group", AccountNature.Asset);
        var newParentId = Guid.NewGuid();

        group.MoveTo(newParentId);

        group.ParentGroupId.ShouldBe(newParentId);
    }
}
