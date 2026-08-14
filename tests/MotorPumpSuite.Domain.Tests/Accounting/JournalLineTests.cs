using MotorPumpSuite.Domain.Accounting;

namespace MotorPumpSuite.Domain.Tests.Accounting;

/// <summary>
/// One signed column rather than separate Dr/Cr columns (spec §8.1): positive is a debit, negative a
/// credit, and it must be structurally impossible for a line to carry both.
/// </summary>
public class JournalLineTests
{
    [Fact]
    public void Constructor_PositiveAmount_IsDebitNotCredit()
    {
        var line = new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 100.50m);

        line.IsDebit.ShouldBeTrue();
        line.IsCredit.ShouldBeFalse();
        line.AmountMinor.ShouldBe(1_005_000L);
    }

    [Fact]
    public void Constructor_NegativeAmount_IsCreditNotDebit()
    {
        var line = new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), -100.50m);

        line.IsCredit.ShouldBeTrue();
        line.IsDebit.ShouldBeFalse();
        line.AmountMinor.ShouldBe(-1_005_000L);
    }

    [Fact]
    public void DebitAmount_OnDebitLine_ReturnsAmountAndCreditAmountIsZero()
    {
        var line = new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 250.75m);

        line.DebitAmount.ShouldBe(250.75m);
        line.CreditAmount.ShouldBe(0m);
    }

    [Fact]
    public void CreditAmount_OnCreditLine_ReturnsPositiveMagnitudeAndDebitAmountIsZero()
    {
        var line = new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), -250.75m);

        // Presentation columns are conventionally shown positive even for a credit; only the stored
        // AmountMinor carries the sign.
        line.CreditAmount.ShouldBe(250.75m);
        line.DebitAmount.ShouldBe(0m);
    }

    [Fact]
    public void Constructor_ZeroAmount_ThrowsArgumentException()
    {
        // A zero line records nothing and would let a fabricated "balanced" voucher pad its line
        // count without moving any value.
        Should.Throw<ArgumentException>(
            () => new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 0m));
    }

    [Fact]
    public void Constructor_MoreThanFourDecimalPlaces_ThrowsArgumentOutOfRangeException()
    {
        // Delegates to MoneyScale.ToMinor (ADR-0003), which refuses to round silently.
        Should.Throw<ArgumentOutOfRangeException>(
            () => new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 1.00005m));
    }

    [Fact]
    public void Constructor_EmptyLedgerId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(
            () => new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.Empty, 100m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_LineNumberLessThanOne_ThrowsArgumentOutOfRangeException(int invalidLineNumber)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new JournalLine(Guid.NewGuid(), Guid.NewGuid(), invalidLineNumber, Guid.NewGuid(), 100m));
    }

    [Fact]
    public void Constructor_EmptyCompanyId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(
            () => new JournalLine(Guid.Empty, Guid.NewGuid(), 1, Guid.NewGuid(), 100m));
    }

    [Fact]
    public void Constructor_BlankNarration_BecomesNull()
    {
        var line = new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 100m, narration: "   ");

        line.Narration.ShouldBeNull();
    }

    [Fact]
    public void Amount_ReflectsTheSignedMinorValue()
    {
        var line = new JournalLine(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), -0.0001m);

        line.Amount.ShouldBe(-0.0001m);
        line.AmountMinor.ShouldBe(-1L);
    }
}
