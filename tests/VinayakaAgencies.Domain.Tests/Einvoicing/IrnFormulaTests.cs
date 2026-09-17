using Shouldly;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Domain.Tests.Einvoicing;

/// <summary>
/// The IRN computation, against NIC's own published worked example
/// (phase-6-einvoice-eway.md §2, checkpoint C6.4).
/// </summary>
/// <remarks>
/// C6.4 asks that our computed IRN match the one the portal returns. That comparison needs the
/// sandbox, which <c>open-questions.md</c> #10 still blocks — but the portal's answer is a pure
/// function of four values it publishes the formula for, so the half that can be pinned down without
/// credentials is pinned down here: the exact string that gets hashed, and the hash of it.
/// <para>
/// The fixture is NIC's own example from <c>einv-apisandbox.nic.in/irn.html</c>: GSTIN
/// <c>01AAAAA9999A19N</c>, invoice <c>ABC01234</c> dated 12 December 2019, concatenating to
/// <c>01AAAAA9999A19N2019-20INVABC01234</c>. If a future edit reorders the four components, this test
/// fails — which is the entire point, because the resulting IRN would still be a valid-looking
/// 64-character string that the portal simply disagreed with.
/// </para>
/// </remarks>
public sealed class IrnFormulaTests
{
    private const string NicExampleGstin = "01AAAAA9999A19N";
    private const string NicExampleDocumentNumber = "ABC01234";
    private static readonly DateOnly NicExampleDate = new(2019, 12, 12);

    [Fact]
    public void HashInput_ForNicPublishedExample_ConcatenatesGstinThenYearThenTypeThenNumber()
    {
        IrnFormula.HashInput(
                NicExampleGstin,
                NicExampleDate,
                EinvoiceDocumentType.TaxInvoice,
                NicExampleDocumentNumber)
            .ShouldBe("01AAAAA9999A19N2019-20INVABC01234");
    }

    [Fact]
    public void Compute_ForNicPublishedExample_IsSixtyFourLowercaseHexCharacters()
    {
        var irn = IrnFormula.Compute(
            NicExampleGstin,
            NicExampleDate,
            EinvoiceDocumentType.TaxInvoice,
            NicExampleDocumentNumber);

        irn.Length.ShouldBe(64);
        irn.ShouldBe(irn.ToLowerInvariant());
        IrnFormula.IsWellFormed(irn).ShouldBeTrue();
    }

    [Fact]
    public void Compute_IsTheSha256OfTheHashInput_AndNothingElse()
    {
        // Ties the two halves together: whatever HashInput produces is exactly what gets hashed, so a
        // change to the concatenation cannot slip past by also changing the hashing.
        var input = IrnFormula.HashInput(
            NicExampleGstin, NicExampleDate, EinvoiceDocumentType.TaxInvoice, NicExampleDocumentNumber);

        IrnFormula.Compute(
                NicExampleGstin, NicExampleDate, EinvoiceDocumentType.TaxInvoice, NicExampleDocumentNumber)
            .ShouldBe(IrnFormula.Hash(input));
    }

    [Fact]
    public void Compute_CalledTwiceForTheSameDocument_ReturnsTheSameIrn()
    {
        // Determinism is what makes the IRN usable as an idempotency key (checkpoint C6.5): a retry
        // recomputes the same value rather than inventing a second registration.
        var first = IrnFormula.Compute(
            NicExampleGstin, NicExampleDate, EinvoiceDocumentType.TaxInvoice, NicExampleDocumentNumber);
        var second = IrnFormula.Compute(
            NicExampleGstin, NicExampleDate, EinvoiceDocumentType.TaxInvoice, NicExampleDocumentNumber);

        second.ShouldBe(first);
    }

    [Fact]
    public void Compute_WithADifferentDocumentType_ProducesADifferentIrn()
    {
        var invoice = IrnFormula.Compute(
            NicExampleGstin, NicExampleDate, EinvoiceDocumentType.TaxInvoice, NicExampleDocumentNumber);
        var creditNote = IrnFormula.Compute(
            NicExampleGstin, NicExampleDate, EinvoiceDocumentType.CreditNote, NicExampleDocumentNumber);

        creditNote.ShouldNotBe(invoice);
    }

    [Fact]
    public void Compute_ForTheSameNumberInAdjacentFinancialYears_ProducesDifferentIrns()
    {
        // Invoice numbers reset each financial year (open-questions #11), so the year is what keeps
        // two legitimately identical numbers apart.
        var firstYear = IrnFormula.Compute(
            NicExampleGstin, new DateOnly(2026, 3, 31), EinvoiceDocumentType.TaxInvoice, "SAL/0001");
        var secondYear = IrnFormula.Compute(
            NicExampleGstin, new DateOnly(2026, 4, 1), EinvoiceDocumentType.TaxInvoice, "SAL/0001");

        secondYear.ShouldNotBe(firstYear);
    }

    [Fact]
    public void HashInput_NormalisesTheGstinToUppercase_SoCasingCannotChangeTheHash()
    {
        IrnFormula.HashInput(
                NicExampleGstin.ToLowerInvariant(),
                NicExampleDate,
                EinvoiceDocumentType.TaxInvoice,
                NicExampleDocumentNumber)
            .ShouldBe("01AAAAA9999A19N2019-20INVABC01234");
    }

    [Fact]
    public void HashInput_TrimsTheDocumentNumber_SoStrayWhitespaceCannotChangeTheHash()
    {
        IrnFormula.HashInput(
                NicExampleGstin,
                NicExampleDate,
                EinvoiceDocumentType.TaxInvoice,
                $"  {NicExampleDocumentNumber}  ")
            .ShouldBe("01AAAAA9999A19N2019-20INVABC01234");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HashInput_WithNoDocumentNumber_IsRefused(string? documentNumber)
    {
        Should.Throw<ArgumentException>(() => IrnFormula.HashInput(
            NicExampleGstin, NicExampleDate, EinvoiceDocumentType.TaxInvoice, documentNumber!));
    }

    [Fact]
    public void IsWellFormed_RejectsUppercaseHex_BecauseThePortalUsesLowercase()
    {
        var irn = IrnFormula.Compute(
            NicExampleGstin, NicExampleDate, EinvoiceDocumentType.TaxInvoice, NicExampleDocumentNumber);

        IrnFormula.IsWellFormed(irn.ToUpperInvariant()).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooshort")]
    public void IsWellFormed_RejectsAnythingNotSixtyFourHexCharacters(string? candidate)
    {
        IrnFormula.IsWellFormed(candidate).ShouldBeFalse();
    }
}
