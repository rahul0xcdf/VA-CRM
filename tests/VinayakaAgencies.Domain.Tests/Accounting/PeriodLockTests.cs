using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>
/// Spec §12: <see cref="PeriodLock.LockedThroughDate"/> blocks postings on or before the locked
/// date — inclusive, not exclusive, so "locked through 31 March" really does close out the year.
/// </summary>
public class PeriodLockTests
{
    [Fact]
    public void IsLocked_DateEqualToLockedThroughDate_ReturnsTrue()
    {
        var lockRow = new PeriodLock(Guid.NewGuid(), lockedThroughDate: new DateOnly(2026, 3, 31));

        lockRow.IsLocked(new DateOnly(2026, 3, 31)).ShouldBeTrue();
    }

    [Fact]
    public void IsLocked_DateBeforeLockedThroughDate_ReturnsTrue()
    {
        var lockRow = new PeriodLock(Guid.NewGuid(), lockedThroughDate: new DateOnly(2026, 3, 31));

        lockRow.IsLocked(new DateOnly(2026, 1, 1)).ShouldBeTrue();
    }

    [Fact]
    public void IsLocked_DateAfterLockedThroughDate_ReturnsFalse()
    {
        var lockRow = new PeriodLock(Guid.NewGuid(), lockedThroughDate: new DateOnly(2026, 3, 31));

        lockRow.IsLocked(new DateOnly(2026, 4, 1)).ShouldBeFalse();
    }

    [Fact]
    public void IsLocked_NoLockSet_AlwaysReturnsFalse()
    {
        var lockRow = new PeriodLock(Guid.NewGuid());

        lockRow.IsLocked(new DateOnly(1900, 1, 1)).ShouldBeFalse();
        lockRow.IsLocked(new DateOnly(2999, 12, 31)).ShouldBeFalse();
    }

    [Fact]
    public void Unlock_BlankReason_ThrowsArgumentException()
    {
        var lockRow = new PeriodLock(Guid.NewGuid(), lockedThroughDate: new DateOnly(2026, 3, 31));

        Should.Throw<ArgumentException>(() => lockRow.Unlock("   "));
    }

    [Fact]
    public void Unlock_ValidReason_ClearsTheLock()
    {
        var lockRow = new PeriodLock(Guid.NewGuid(), lockedThroughDate: new DateOnly(2026, 3, 31));

        lockRow.Unlock("Correcting a posting error found after close");

        lockRow.LockedThroughDate.ShouldBeNull();
        lockRow.IsLocked(new DateOnly(2020, 1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_EmptyCompanyId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new PeriodLock(Guid.Empty));
    }
}
