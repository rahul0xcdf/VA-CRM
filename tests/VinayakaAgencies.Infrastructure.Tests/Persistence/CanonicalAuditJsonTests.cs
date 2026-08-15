using System.Globalization;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Infrastructure.Persistence.Auditing;

namespace VinayakaAgencies.Infrastructure.Tests.Persistence;

/// <summary>
/// The canonical encoding the edit log stores and hashes (spec §11.3).
/// </summary>
/// <remarks>
/// These strings are inputs to the SHA-256 chain in §11.5. If the encoding drifts — a .NET upgrade
/// changing how a number renders, a machine with a different decimal separator, an enum renumbered
/// in a later phase — every historical row stops verifying, and a chain that fails for a benign
/// reason is indistinguishable, to an auditor, from one that fails because a row was edited. So the
/// rules are asserted individually rather than left implied by a round-trip test.
/// </remarks>
public sealed class CanonicalAuditJsonTests
{
    [Fact]
    public void Json_Decimal_IsAQuotedStringAtFourDecimalPlaces()
    {
        // A JSON number would let any reader round-trip a rupee value through a double. §11.3
        // forbids it, and ADR-0003 exists because that rounding is a real-money defect.
        CanonicalAuditJson.Json(1234.5m).ShouldBe("\"1234.5000\"");
    }

    [Fact]
    public void Json_Decimal_PadsAndTruncatesToExactlyFourDecimalPlaces()
    {
        CanonicalAuditJson.Json(0m).ShouldBe("\"0.0000\"");
        CanonicalAuditJson.Json(-1m).ShouldBe("\"-1.0000\"");
        CanonicalAuditJson.Json(0.1m + 0.2m).ShouldBe("\"0.3000\"");
        CanonicalAuditJson.Json(99999999.9999m).ShouldBe("\"99999999.9999\"");
    }

    [Fact]
    public void Json_Decimal_IgnoresTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            // de-DE writes 1234,5 — a comma inside a JSON number would not even parse, and a
            // comma inside the hashed payload would break every row sealed on a machine set to
            // another locale.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            CanonicalAuditJson.Json(1234.5m).ShouldBe("\"1234.5000\"");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Json_Enum_IsItsNameAndNotItsNumber()
    {
        // A later reordering of the enum must not silently change what a historical row means.
        CanonicalAuditJson.Json(AuditAction.Update).ShouldBe("\"Update\"");
        CanonicalAuditJson.Json(AuditAction.Delete).ShouldBe("\"Delete\"");
    }

    [Fact]
    public void Json_Null_IsJsonNullAndNotOmitted()
    {
        // A property that was cleared and a property that never existed must stay distinguishable.
        CanonicalAuditJson.Json(null).ShouldBe("null");
    }

    [Fact]
    public void Json_DateOnly_IsIsoCalendarDate()
    {
        // Business dates are DateOnly because Indian financial-year logic follows the local
        // calendar date (CLAUDE.md, spec §12).
        CanonicalAuditJson.Json(new DateOnly(2026, 4, 1)).ShouldBe("\"2026-04-01\"");
    }

    [Fact]
    public void Json_DateTimeOffset_IsRoundTripFormatNormalisedToUtc()
    {
        var istMidday = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.FromHours(5.5));

        CanonicalAuditJson.Json(istMidday).ShouldBe("\"2026-04-01T06:30:00.0000000+00:00\"");
    }

    [Fact]
    public void Json_Guid_IsTheDashedLowerCaseForm()
    {
        var id = new Guid("A1B2C3D4-E5F6-4708-9A0B-1C2D3E4F5061");

        CanonicalAuditJson.Json(id).ShouldBe("\"a1b2c3d4-e5f6-4708-9a0b-1c2d3e4f5061\"");
    }

    [Fact]
    public void Json_BoolAndInteger_AreJsonLiterals()
    {
        // Nothing is gained by quoting these, and an unquoted integer keeps the log readable.
        CanonicalAuditJson.Json(true).ShouldBe("true");
        CanonicalAuditJson.Json(false).ShouldBe("false");
        CanonicalAuditJson.Json(4).ShouldBe("4");
        CanonicalAuditJson.Json(9_223_372_036_854_775_807L).ShouldBe("9223372036854775807");
    }

    [Fact]
    public void Json_String_KeepsNonAsciiTextLiteral()
    {
        // Relaxed escaping, so a party name in Kannada or a rupee amount reads as itself in the
        // log rather than as a run of \uXXXX escapes. Deterministic either way.
        CanonicalAuditJson.Json("₹ ವಿನಾಯಕ").ShouldBe("\"₹ ವಿನಾಯಕ\"");
    }

    [Fact]
    public void Scalar_UsesTheSameRulesWithoutTheJsonQuotes()
    {
        // Identifier columns go through Scalar, so an id in EntityId and the same id inside an
        // image must be written identically or a search for one will not find the other.
        var id = Guid.NewGuid();

        CanonicalAuditJson.Scalar(id).ShouldBe(id.ToString("D", CultureInfo.InvariantCulture));
        CanonicalAuditJson.Scalar(1234.5m).ShouldBe("1234.5000");
        CanonicalAuditJson.Scalar(AuditAction.Create).ShouldBe("Create");
        CanonicalAuditJson.Scalar(null).ShouldBe(string.Empty);
    }
}
