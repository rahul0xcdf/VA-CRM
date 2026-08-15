using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>
/// ADR-0002: the voucher's content lives on the revision, and only the current (non-superseded)
/// revision may still take lines. <see cref="VoucherRevision.IsBalanced"/> is the exact-comparison
/// guard behind the trial-balance invariant, so its edge case — off by a single minor unit — is the
/// one that matters most here.
/// </summary>
public class VoucherRevisionTests
{
    private static VoucherRevision NewRevision(VoucherStatus status = VoucherStatus.Posted) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            revisionNumber: 1,
            voucherDate: new DateOnly(2026, 8, 14),
            number: "PMT/0001",
            status: status);

    [Fact]
    public void IsBalanced_DebitsEqualCredits_ReturnsTrue()
    {
        var revision = NewRevision();
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 1, Guid.NewGuid(), 500m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 2, Guid.NewGuid(), -500m));

        revision.IsBalanced.ShouldBeTrue();
        revision.NetMinor.ShouldBe(0L);
    }

    [Fact]
    public void IsBalanced_OffByOneMinorUnit_ReturnsFalse()
    {
        // ₹0.0001 out — the smallest possible imbalance — must still be rejected. No tolerance,
        // no epsilon (spec §8.1, ADR-0003).
        var revision = NewRevision();
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 1, Guid.NewGuid(), 500m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 2, Guid.NewGuid(), -499.9999m));

        revision.IsBalanced.ShouldBeFalse();
        revision.NetMinor.ShouldBe(1L);
    }

    [Fact]
    public void NetMinor_MultipleLines_IsTheExactSignedSum()
    {
        var revision = NewRevision();
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 1, Guid.NewGuid(), 300m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 2, Guid.NewGuid(), 200m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 3, Guid.NewGuid(), -500m));

        revision.NetMinor.ShouldBe(0L);
    }

    [Fact]
    public void TotalDebit_SumsOnlyPositiveLines()
    {
        var revision = NewRevision();
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 1, Guid.NewGuid(), 300m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 2, Guid.NewGuid(), 200m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 3, Guid.NewGuid(), -500m));

        revision.TotalDebit.ShouldBe(500m);
    }

    [Fact]
    public void TotalCredit_SumsOnlyNegativeLinesShownPositive()
    {
        var revision = NewRevision();
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 1, Guid.NewGuid(), 500m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 2, Guid.NewGuid(), -300m));
        revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 3, Guid.NewGuid(), -200m));

        revision.TotalCredit.ShouldBe(500m);
    }

    [Fact]
    public void AddLine_OnSupersededRevision_ThrowsInvalidOperationException()
    {
        var revision = NewRevision();
        revision.SupersedeBy(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(
            () => revision.AddLine(new JournalLine(Guid.NewGuid(), revision.Id, 1, Guid.NewGuid(), 100m)));
    }

    [Fact]
    public void SupersedeBy_CalledTwice_ThrowsInvalidOperationException()
    {
        var revision = NewRevision();
        revision.SupersedeBy(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() => revision.SupersedeBy(Guid.NewGuid()));
    }

    [Fact]
    public void SupersedeBy_EmptyGuid_ThrowsArgumentException()
    {
        var revision = NewRevision();

        Should.Throw<ArgumentException>(() => revision.SupersedeBy(Guid.Empty));
    }

    [Theory]
    [InlineData("pmt/0001", "PMT/0001 ")]
    [InlineData("PMT/0001", " pmt / 0001 ")]
    public void Normalise_TrivallyDifferentSpellings_AreEqual(string first, string second)
    {
        VoucherRevision.Normalise(first).ShouldBe(VoucherRevision.Normalise(second));
    }

    [Fact]
    public void NumberNormalised_SetFromConstructor_MatchesStaticNormalise()
    {
        var revision = NewRevision();

        revision.NumberNormalised.ShouldBe(VoucherRevision.Normalise("PMT/0001"));
    }

    [Fact]
    public void IsBalanced_CancelledRevisionWithNoLines_IsTriviallyTrue()
    {
        var revision = NewRevision(VoucherStatus.Cancelled);

        revision.Lines.ShouldBeEmpty();
        revision.IsBalanced.ShouldBeTrue();
        revision.NetMinor.ShouldBe(0L);
    }

    [Fact]
    public void IsCurrent_NotYetSuperseded_ReturnsTrue()
    {
        var revision = NewRevision();

        revision.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void IsCurrent_AfterSupersedeBy_ReturnsFalse()
    {
        var revision = NewRevision();
        revision.SupersedeBy(Guid.NewGuid());

        revision.IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_RevisionNumberLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new VoucherRevision(
            Guid.NewGuid(),
            Guid.NewGuid(),
            revisionNumber: 0,
            voucherDate: new DateOnly(2026, 8, 14),
            number: "PMT/0001"));
    }
}
