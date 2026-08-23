using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>
/// Decision D2: gaps are blocked — <see cref="VoucherNumberSequence.LastAllocatedNumber"/> is a
/// high-water mark that only ever rises, so a value it has already returned can never be handed out
/// again, whether by automatic allocation or a subsequent manual override.
/// </summary>
public class VoucherNumberSequenceTests
{
    [Fact]
    public void Allocate_RepeatedCalls_AreStrictlyIncreasing()
    {
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid());

        var first = sequence.Allocate(1);
        var second = sequence.Allocate(1);
        var third = sequence.Allocate(1);

        first.ShouldBe(1L);
        second.ShouldBe(2L);
        third.ShouldBe(3L);
    }

    [Fact]
    public void Allocate_FirstCall_RespectsStartNumber()
    {
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid());

        var first = sequence.Allocate(startNumber: 1001);

        first.ShouldBe(1001L);
    }

    [Fact]
    public void Allocate_StartNumberBelowExistingHighWaterMark_IsIgnored()
    {
        // Once the sequence has moved past a start number (e.g. because of a manual override), a
        // later call with a lower startNumber must not step backwards into already-issued territory.
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid(), lastAllocatedNumber: 50);

        var next = sequence.Allocate(startNumber: 1);

        next.ShouldBe(51L);
    }

    [Fact]
    public void Allocate_NeverReturnsAPreviouslyReturnedValue_AcrossManyAllocations()
    {
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid());
        var issued = new HashSet<long>();

        for (var i = 0; i < 1000; i++)
        {
            var number = sequence.Allocate(1);

            issued.Add(number).ShouldBeTrue($"number {number} was already issued");
        }

        issued.Count.ShouldBe(1000);
    }

    [Fact]
    public void ObserveManualNumber_AboveHighWaterMark_RaisesIt()
    {
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid(), lastAllocatedNumber: 5);

        sequence.ObserveManualNumber(20);

        sequence.LastAllocatedNumber.ShouldBe(20L);
    }

    [Fact]
    public void ObserveManualNumber_BelowHighWaterMark_DoesNotLowerIt()
    {
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid(), lastAllocatedNumber: 20);

        sequence.ObserveManualNumber(5);

        sequence.LastAllocatedNumber.ShouldBe(20L);
    }

    [Fact]
    public void ObserveManualNumber_ThenAllocate_ContinuesAfterTheOverride()
    {
        var sequence = new VoucherNumberSequence(Guid.NewGuid(), Guid.NewGuid());
        sequence.Allocate(1); // 1
        sequence.Allocate(1); // 2

        sequence.ObserveManualNumber(500); // a manual override skipped ahead to 500

        var next = sequence.Allocate(1);

        next.ShouldBe(501L);
    }

    [Fact]
    public void Constructor_EmptySeriesId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new VoucherNumberSequence(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void Constructor_EmptyFinancialYearId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new VoucherNumberSequence(Guid.NewGuid(), Guid.Empty));
    }
}
