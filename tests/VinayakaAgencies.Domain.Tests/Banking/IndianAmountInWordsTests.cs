using Shouldly;
using VinayakaAgencies.Domain.Banking;

namespace VinayakaAgencies.Domain.Tests.Banking;

/// <summary>
/// The words a cheque carries (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// Worth this many cases because the words are the legally operative amount: where the figures and
/// the words on a cheque disagree, the words are what the bank honours. A conversion that is wrong
/// by a lakh is wrong by a lakh in a way nobody can dispute afterwards.
/// </remarks>
public class IndianAmountInWordsTests
{
    [Theory]
    [InlineData(0L, "Zero")]
    [InlineData(1L, "One")]
    [InlineData(9L, "Nine")]
    [InlineData(10L, "Ten")]
    [InlineData(13L, "Thirteen")]
    [InlineData(19L, "Nineteen")]
    [InlineData(20L, "Twenty")]
    [InlineData(21L, "Twenty One")]
    [InlineData(99L, "Ninety Nine")]
    public void Convert_BelowAHundred_HandlesTheIrregularTeensAndTheRegularTens(
        long value,
        string expected) =>
        IndianAmountInWords.Convert(value).ShouldBe(expected);

    [Theory]
    [InlineData(100L, "One Hundred")]
    [InlineData(101L, "One Hundred One")]
    [InlineData(999L, "Nine Hundred Ninety Nine")]
    [InlineData(1_000L, "One Thousand")]
    [InlineData(10_000L, "Ten Thousand")]
    [InlineData(99_999L, "Ninety Nine Thousand Nine Hundred Ninety Nine")]
    public void Convert_UpToALakh_GroupsThousandsThenHundreds(long value, string expected) =>
        IndianAmountInWords.Convert(value).ShouldBe(expected);

    /// <summary>
    /// The Indian system, not the Western one: lakhs at 10^5 and crores at 10^7.
    /// </summary>
    [Theory]
    [InlineData(100_000L, "One Lakh")]
    [InlineData(1_234_567L, "Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven")]
    [InlineData(10_000_000L, "One Crore")]
    [InlineData(12_34_56_789L, "Twelve Crore Thirty Four Lakh Fifty Six Thousand Seven Hundred Eighty Nine")]
    public void Convert_AtLakhAndCrore_UsesIndianGroupingAndNotMillions(long value, string expected) =>
        IndianAmountInWords.Convert(value).ShouldBe(expected);

    /// <summary>
    /// Above a crore the system repeats rather than inventing a new word: 10^9 is "One Hundred
    /// Crore". A crore group capped at ninety-nine would be unable to say it.
    /// </summary>
    [Fact]
    public void Convert_AboveACrore_RepeatsTheSystemRatherThanNamingANewUnit()
    {
        IndianAmountInWords.Convert(1_000_000_000L).ShouldBe("One Hundred Crore");
        IndianAmountInWords.Convert(100_000_000_000L).ShouldBe("Ten Thousand Crore");
    }

    [Fact]
    public void ForCheque_WrapsTheWordsAsACheqeRequires()
    {
        IndianAmountInWords.ForCheque(5_000m).ShouldBe("Rupees Five Thousand Only");
        IndianAmountInWords.ForCheque(1_25_000m)
            .ShouldBe("Rupees One Lakh Twenty Five Thousand Only");
    }

    /// <summary>
    /// The trailing "Only" is not decoration — it is what stops somebody extending the written
    /// amount by adding words after it.
    /// </summary>
    [Fact]
    public void ForCheque_AlwaysTerminatesWithOnly() =>
        IndianAmountInWords.ForCheque(1m).ShouldEndWith(" Only");

    [Fact]
    public void ForCheque_WithPaise_SpellsThemSeparately() =>
        IndianAmountInWords.ForCheque(1_234.50m)
            .ShouldBe("Rupees One Thousand Two Hundred Thirty Four and Fifty Paise Only");

    [Fact]
    public void ForCheque_WithNoPaise_DoesNotMentionThem() =>
        IndianAmountInWords.ForCheque(1_234m).ShouldNotContain("Paise");

    /// <summary>
    /// Rounding the paise can carry into the rupee. 99.999 is a hundred rupees, not ninety-nine
    /// rupees and a hundred paise — which is a sentence no cheque should ever print.
    /// </summary>
    [Fact]
    public void ForCheque_WhereRoundingThePaiseCarries_AddsTheRupee() =>
        IndianAmountInWords.ForCheque(99.999m).ShouldBe("Rupees One Hundred Only");

    [Fact]
    public void ForCheque_ForZero_IsStillWellFormed() =>
        IndianAmountInWords.ForCheque(0m).ShouldBe("Rupees Zero Only");

    [Fact]
    public void ForCheque_ForANegativeAmount_IsRefused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => IndianAmountInWords.ForCheque(-1m));

    /// <summary>
    /// Indian digit grouping, and from an explicit format rather than an "en-IN" culture lookup —
    /// the machine may not have that culture installed, and a cheque that groups one way on one
    /// machine and another way elsewhere is a defect nobody notices until a bank returns one.
    /// </summary>
    [Theory]
    [InlineData(1_234_567, "12,34,567.00")]
    [InlineData(100_000, "1,00,000.00")]
    [InlineData(999, "999.00")]
    [InlineData(1_000, "1,000.00")]
    public void ToIndianFigures_GroupsThreeThenTwo(decimal amount, string expected) =>
        IndianAmountInWords.ToIndianFigures(amount).ShouldBe(expected);

    [Fact]
    public void ForChequeMinor_ReadsMinorUnitsAsRupees() =>
        IndianAmountInWords.ForChequeMinor(50_000_000L).ShouldBe("Rupees Five Thousand Only");
}
