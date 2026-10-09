using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

namespace VinayakaAgencies.Infrastructure.Tests.Security;

/// <summary>
/// The password verifier behind `phase-9-multiuser.md` §4a D2.
/// </summary>
/// <remarks>
/// Resolved through the real composition rather than constructed directly, so these also assert
/// that <c>AddInfrastructure</c> registers a hasher at all — an unregistered one would fail at the
/// login screen on a customer machine rather than here.
/// </remarks>
public sealed class Pbkdf2PasswordHasherTests
{
    [Fact]
    public async Task Verify_CorrectPassword_ReturnsTrue()
    {
        await using var host = new SqliteTestHost();

        var verified = await host.InScopeAsync(sp =>
        {
            var hasher = sp.GetRequiredService<IPasswordHasher>();
            var stored = hasher.Hash("correct horse battery");

            return Task.FromResult(hasher.Verify("correct horse battery", stored));
        });

        verified.ShouldBeTrue();
    }

    [Fact]
    public async Task Verify_WrongPassword_ReturnsFalse()
    {
        await using var host = new SqliteTestHost();

        var verified = await host.InScopeAsync(sp =>
        {
            var hasher = sp.GetRequiredService<IPasswordHasher>();
            var stored = hasher.Hash("correct horse battery");

            return Task.FromResult(hasher.Verify("correct horse batteries", stored));
        });

        verified.ShouldBeFalse();
    }

    [Fact]
    public async Task Hash_SamePasswordTwice_ProducesDifferentVerifiers()
    {
        // Per-password salt. Without it, two people who chose the same password would be visibly
        // the same row in the database, and one precomputed table would open both accounts.
        await using var host = new SqliteTestHost();

        var (first, second) = await host.InScopeAsync(sp =>
        {
            var hasher = sp.GetRequiredService<IPasswordHasher>();

            return Task.FromResult((hasher.Hash("same password"), hasher.Hash("same password")));
        });

        first.ShouldNotBe(second);
    }

    [Fact]
    public async Task Hash_Always_EmitsTheParametersNeededToVerifyIt()
    {
        // The verifier is self-describing so the iteration count can be raised later without a
        // schema change and without locking out existing accounts. If this shape ever stops
        // carrying its own cost, that upgrade path quietly disappears.
        await using var host = new SqliteTestHost();

        var stored = await host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<IPasswordHasher>().Hash("whatever it is")));

        var fields = stored.Split('$');

        fields.Length.ShouldBe(4);
        fields[0].ShouldBe("PBKDF2-SHA256");
        int.TryParse(fields[1], CultureInfo.InvariantCulture, out var iterations).ShouldBeTrue();
        iterations.ShouldBeGreaterThanOrEqualTo(600_000);
        fields[2].ShouldNotBeNullOrWhiteSpace();
        fields[3].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Hash_Always_StoresNothingResemblingThePassword()
    {
        await using var host = new SqliteTestHost();

        var stored = await host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<IPasswordHasher>().Hash("Motor-Pump-2026")));

        stored.ShouldNotContain("Motor-Pump-2026");
        stored.ShouldNotContain("Motor", Case.Insensitive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-verifier")]
    [InlineData("PBKDF2-SHA256$notanumber$c2FsdA==$a2V5")]
    [InlineData("PBKDF2-SHA256$0$c2FsdA==$a2V5")]
    [InlineData("PBKDF2-SHA256$600000$not-base64!$a2V5")]
    [InlineData("SCRYPT$600000$c2FsdA==$a2V5")]
    [InlineData("PBKDF2-SHA256$600000$c2FsdA==")]
    public async Task Verify_MalformedStoredValue_ReturnsFalseRatherThanThrowing(string stored)
    {
        // A corrupt verifier has to fail the sign-in, not crash the login screen: whatever damaged
        // the row, the operator's recourse is the same and a thrown exception denies them all of it.
        await using var host = new SqliteTestHost();

        var verified = await host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<IPasswordHasher>().Verify("anything", stored)));

        verified.ShouldBeFalse();
    }
}
