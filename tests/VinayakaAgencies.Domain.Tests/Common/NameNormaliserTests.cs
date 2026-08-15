using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tests.Common;

/// <summary>
/// The comparison form behind every unique master-name index. It exists because SQLite's default
/// BINARY collation is case-sensitive while SQL Server's default is not: without it, "Cash" and
/// "cash" would both be accepted for Phases 1–8 and the Phase 9 index creation would then fail on
/// data that is already duplicated.
/// </summary>
public class NameNormaliserTests
{
    [Theory]
    [InlineData("Cash", "cash")]
    [InlineData("Cash", "CASH")]
    [InlineData("Cash", " Cash ")]
    [InlineData("Sundry Debtors", "sundry  debtors")]
    [InlineData("Sundry Debtors", "\tSUNDRY\r\nDebtors  ")]
    public void Normalise_SpellingsThatDifferOnlyByCaseOrWhitespace_ProduceTheSameValue(
        string first,
        string second)
    {
        NameNormaliser.Normalise(first).ShouldBe(NameNormaliser.Normalise(second));
    }

    [Fact]
    public void Normalise_TypicalName_UpperCasesAndCollapsesWhitespace()
    {
        NameNormaliser.Normalise("  sundry   debtors ").ShouldBe("SUNDRY DEBTORS");
    }

    [Fact]
    public void Normalise_NamesDifferingOnlyByAWordBreak_RemainDistinct()
    {
        // Whitespace is collapsed, not stripped — unlike VoucherRevision.Normalise, which strips it
        // because a document number has no words to keep apart. "SundryDebtors" is a different
        // master from "Sundry Debtors" and must not be rejected as a duplicate of it.
        NameNormaliser.Normalise("Sundry Debtors")
            .ShouldNotBe(NameNormaliser.Normalise("SundryDebtors"));
    }

    [Fact]
    public void Normalise_WhitespaceOnly_ReturnsEmpty()
    {
        NameNormaliser.Normalise("   ").ShouldBe(string.Empty);
    }

    [Fact]
    public void Normalise_Null_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => NameNormaliser.Normalise(null!));
    }
}
