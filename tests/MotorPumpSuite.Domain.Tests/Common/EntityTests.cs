using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Tests.Common;

public class EntityTests
{
    [Fact]
    public void Constructor_ParameterlessOverload_AssignsNonEmptyGuid()
    {
        var entity = new TestEntity();

        entity.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_TwoInstances_AssignDifferentIds()
    {
        var first = new TestEntity();
        var second = new TestEntity();

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void Constructor_ExplicitId_AssignsThatId()
    {
        var id = Guid.NewGuid();

        var entity = new TestEntity(id);

        entity.Id.ShouldBe(id);
    }

    [Fact]
    public void Constructor_EmptyGuid_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new TestEntity(Guid.Empty));
    }

    [Fact]
    public void Equals_SameTypeSameId_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var first = new TestEntity(id);
        var second = new TestEntity(id);

        first.Equals(second).ShouldBeTrue();
        (first == second).ShouldBeTrue();
        (first != second).ShouldBeFalse();
    }

    [Fact]
    public void Equals_SameTypeDifferentId_ReturnsFalse()
    {
        var first = new TestEntity(Guid.NewGuid());
        var second = new TestEntity(Guid.NewGuid());

        first.Equals(second).ShouldBeFalse();
        (first == second).ShouldBeFalse();
        (first != second).ShouldBeTrue();
    }

    [Fact]
    public void Equals_DifferentEntityTypesSameId_ReturnsFalse()
    {
        // Entity identity must be scoped by type, not just by GUID: a voucher and a stock item
        // must never compare equal merely because their ids collide (see Entity.Equals remarks).
        var id = Guid.NewGuid();
        var testEntity = new TestEntity(id);
        var anotherEntity = new AnotherTestEntity(id);

        testEntity.Equals(anotherEntity).ShouldBeFalse();
    }

    [Fact]
    public void Equals_SameReference_ReturnsTrue()
    {
        var entity = new TestEntity();

        entity.Equals(entity).ShouldBeTrue();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var entity = new TestEntity();

        entity.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void EqualityOperator_BothNull_ReturnsTrue()
    {
        TestEntity? left = null;
        TestEntity? right = null;

        (left == right).ShouldBeTrue();
    }

    [Fact]
    public void EqualityOperator_LeftNullRightNotNull_ReturnsFalse()
    {
        TestEntity? left = null;
        var right = new TestEntity();

        (left == right).ShouldBeFalse();
        (right == left).ShouldBeFalse();
    }

    [Fact]
    public void GetHashCode_EqualEntities_ReturnSameHashCode()
    {
        var id = Guid.NewGuid();
        var first = new TestEntity(id);
        var second = new TestEntity(id);

        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentTypesSameId_ReturnDifferentHashCodes()
    {
        // Not a strict correctness requirement (hash collisions are legal), but the type is folded
        // into the hash specifically so that this pair does not collide in practice.
        var id = Guid.NewGuid();
        var testEntity = new TestEntity(id);
        var anotherEntity = new AnotherTestEntity(id);

        testEntity.GetHashCode().ShouldNotBe(anotherEntity.GetHashCode());
    }

    [Fact]
    public void Equals_ObjectOverloadWithUnrelatedType_ReturnsFalse()
    {
        var entity = new TestEntity();

        entity.Equals((object)"not an entity").ShouldBeFalse();
    }
}
