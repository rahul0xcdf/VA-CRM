using Shouldly;
using VinayakaAgencies.Application.Einvoicing;

namespace VinayakaAgencies.Application.Tests.Einvoicing;

/// <summary>
/// Credential redaction (phase-6-einvoice-eway.md §2, checkpoint C6.6).
/// </summary>
/// <remarks>
/// C6.6 asks that credentials never appear in logs, exports or the audit trail. This is the unit-level
/// half of that: the scrubber removes secrets by field name <em>and</em> by exact value, and the two
/// passes catch different leaks. The end-to-end half — that nothing unredacted is ever persisted —
/// lives in the Infrastructure tests, where a real call path can be exercised.
/// </remarks>
public sealed class SensitiveTextTests
{
    private const string Secret = "s3cr3t-client-value";

    [Fact]
    public void Redact_RemovesAValueUnderACredentialKey_EvenWhenTheValueIsUnknown()
    {
        // The key pass matters because the portal can return a secret under a field name we never
        // anticipated, and we have nothing to string-match against.
        var body = """{"UserName":"vinayaka","Password":"never-seen-before","Gstin":"29AAAAA9999A1Z5"}""";

        var redacted = SensitiveText.Redact(body);

        redacted.ShouldNotContain("never-seen-before");
        redacted.ShouldNotContain("vinayaka");

        // Non-credential fields survive, or the record would be useless.
        redacted.ShouldContain("29AAAAA9999A1Z5");
    }

    [Fact]
    public void Redact_RemovesAKnownSecretUnderAnUnexpectedFieldName()
    {
        // The value pass matters because an error message can quote a credential back at us under a
        // name like "ErrorMessage", which no key list would ever cover.
        var body = $$"""{"ErrorMessage":"Invalid credentials for {{Secret}}"}""";

        SensitiveText.Redact(body, [Secret]).ShouldNotContain(Secret);
    }

    [Fact]
    public void Redact_HandlesTheCommonKeySpellings()
    {
        var body = """
            {"client_id":"a-client-id","clientSecret":"a-secret","AppKey":"an-app-key",
             "Sek":"a-session-key","AuthToken":"a-token"}
            """;

        var redacted = SensitiveText.Redact(body);

        redacted.ShouldNotContain("a-client-id");
        redacted.ShouldNotContain("a-secret");
        redacted.ShouldNotContain("an-app-key");
        redacted.ShouldNotContain("a-session-key");
        redacted.ShouldNotContain("a-token");
    }

    [Fact]
    public void Redact_ReplacesLongerSecretsFirst_SoAContainedSecretIsNotHalfRemoved()
    {
        // "abcdef123456" contains "abcdef1234". Replacing the shorter one first would leave "56"
        // dangling beside a placeholder — and, worse, would reveal that the longer secret ends in 56.
        var text = "token=abcdef123456";

        var redacted = SensitiveText.Redact(text, ["abcdef1234", "abcdef123456"]);

        redacted.ShouldBe($"token={SensitiveText.Placeholder}");
    }

    [Fact]
    public void Redact_IgnoresVeryShortSecrets_SoOrdinaryTextIsNotCorrupted()
    {
        // A three-character "secret" would match inside half the words in a JSON body; blanking those
        // would destroy the record without protecting anything.
        SensitiveText.Redact("the cat sat on the mat", ["cat"])
            .ShouldBe("the cat sat on the mat");
    }

    [Fact]
    public void Redact_WithNoSecretsAndNoCredentialKeys_LeavesTextUnchanged()
    {
        const string body = """{"Irn":"abc","AckNo":112}""";

        SensitiveText.Redact(body).ShouldBe(body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_WithNothingToScrub_ReturnsEmpty(string? text)
    {
        SensitiveText.Redact(text).ShouldBe(string.Empty);
    }

    [Fact]
    public void ContainsAny_IsTheAssertionHelperTheCheckpointUses()
    {
        SensitiveText.ContainsAny($"body with {Secret} in it", [Secret]).ShouldBeTrue();
        SensitiveText.ContainsAny("a clean body", [Secret]).ShouldBeFalse();
        SensitiveText.ContainsAny(null, [Secret]).ShouldBeFalse();
    }

    [Fact]
    public void ContainsAny_AfterRedact_IsAlwaysFalse()
    {
        // The property that actually matters, stated directly.
        var body = $$"""{"Password":"{{Secret}}","note":"also {{Secret}} here"}""";

        SensitiveText.ContainsAny(SensitiveText.Redact(body, [Secret]), [Secret]).ShouldBeFalse();
    }
}
