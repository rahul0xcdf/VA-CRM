using MotorPumpSuite.Domain.Tests.Common;

namespace MotorPumpSuite.Domain.Tests.Abstractions;

public class SpecificationTests
{
    [Fact]
    public void Constructor_Parameterless_LeavesCriteriaNull()
    {
        var specification = new ProbeSpecification();

        specification.Criteria.ShouldBeNull();
    }

    [Fact]
    public void Constructor_WithCriteria_AssignsCriteria()
    {
        var specification = new ProbeSpecification(e => e.Id != Guid.Empty);

        specification.Criteria.ShouldNotBeNull();
    }

    [Fact]
    public void Where_SetsCriteria()
    {
        var specification = new ProbeSpecification();

        specification.ExposeWhere(e => e.Id != Guid.Empty);

        specification.Criteria.ShouldNotBeNull();
    }

    [Fact]
    public void Where_CalledTwice_LastCriteriaOverwritesFirst()
    {
        var specification = new ProbeSpecification();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        specification.ExposeWhere(e => e.Id == firstId);
        specification.ExposeWhere(e => e.Id == secondId);

        var compiled = specification.Criteria!.Compile();
        compiled(new TestEntity(secondId)).ShouldBeTrue();
        compiled(new TestEntity(firstId)).ShouldBeFalse();
    }

    [Fact]
    public void AddInclude_NoIncludesAdded_IncludesIsEmpty()
    {
        var specification = new ProbeSpecification();

        specification.Includes.ShouldBeEmpty();
    }

    [Fact]
    public void AddInclude_OneInclude_IncludesContainsIt()
    {
        var specification = new ProbeSpecification();

        specification.ExposeAddInclude(e => e.Id);

        specification.Includes.Count.ShouldBe(1);
    }

    [Fact]
    public void AddInclude_CalledTwice_IncludesContainsBothInOrder()
    {
        var specification = new ProbeSpecification();

        specification.ExposeAddInclude(e => e.Id);
        specification.ExposeAddInclude(e => e.Id);

        specification.Includes.Count.ShouldBe(2);
    }

    [Fact]
    public void ApplyOrderBy_SetsOrderBy()
    {
        var specification = new ProbeSpecification();

        specification.ExposeApplyOrderBy(e => e.Id);

        specification.OrderBy.ShouldNotBeNull();
        specification.OrderByDescending.ShouldBeNull();
    }

    [Fact]
    public void ApplyOrderByDescending_SetsOrderByDescending()
    {
        var specification = new ProbeSpecification();

        specification.ExposeApplyOrderByDescending(e => e.Id);

        specification.OrderByDescending.ShouldNotBeNull();
        specification.OrderBy.ShouldBeNull();
    }

    [Fact]
    public void ApplyPaging_ValidSkipAndTake_SetsBothProperties()
    {
        var specification = new ProbeSpecification();

        specification.ExposeApplyPaging(20, 10);

        specification.Skip.ShouldBe(20);
        specification.Take.ShouldBe(10);
    }

    [Fact]
    public void ApplyPaging_ZeroSkip_IsAccepted()
    {
        var specification = new ProbeSpecification();

        specification.ExposeApplyPaging(0, 10);

        specification.Skip.ShouldBe(0);
    }

    [Fact]
    public void ApplyPaging_NegativeSkip_ThrowsArgumentOutOfRangeException()
    {
        var specification = new ProbeSpecification();

        Should.Throw<ArgumentOutOfRangeException>(() => specification.ExposeApplyPaging(-1, 10));
    }

    [Fact]
    public void ApplyPaging_ZeroTake_ThrowsArgumentOutOfRangeException()
    {
        var specification = new ProbeSpecification();

        Should.Throw<ArgumentOutOfRangeException>(() => specification.ExposeApplyPaging(0, 0));
    }

    [Fact]
    public void ApplyPaging_NegativeTake_ThrowsArgumentOutOfRangeException()
    {
        var specification = new ProbeSpecification();

        Should.Throw<ArgumentOutOfRangeException>(() => specification.ExposeApplyPaging(0, -5));
    }

    [Fact]
    public void ApplyPaging_InvalidCall_LeavesSkipAndTakeUnset()
    {
        var specification = new ProbeSpecification();

        Should.Throw<ArgumentOutOfRangeException>(() => specification.ExposeApplyPaging(-1, 10));

        specification.Skip.ShouldBeNull();
        specification.Take.ShouldBeNull();
    }

    [Fact]
    public void AsNoTracking_DefaultsToFalse()
    {
        var specification = new ProbeSpecification();

        specification.AsNoTracking.ShouldBeFalse();
    }

    [Fact]
    public void ApplyNoTracking_SetsAsNoTrackingTrue()
    {
        var specification = new ProbeSpecification();

        specification.ExposeApplyNoTracking();

        specification.AsNoTracking.ShouldBeTrue();
    }
}
