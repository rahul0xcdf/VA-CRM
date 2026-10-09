namespace VinayakaAgencies.Application.Security;

/// <summary>
/// The rules a new password has to satisfy.
/// </summary>
/// <remarks>
/// The spec asks for application accounts (§4a D2) and does not say what makes a password
/// acceptable, so this is a stated default rather than a requirement read off the spec: a minimum
/// length, and nothing else. It is deliberately not a composition rule — no "one capital, one digit,
/// one symbol". Those push people towards a predictable shape and towards writing the result on a
/// note beside the counter, which on a shop floor is the actual threat model. Length is the property
/// that costs an attacker something.
/// <para>
/// Worth the owner's review at walkthrough; it is one constant and a message.
/// </para>
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Shortest acceptable password.</summary>
    public const int MinimumLength = 8;

    /// <summary>Returns the reasons <paramref name="password"/> is unacceptable, if any.</summary>
    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("A password is required.");
            return errors;
        }

        if (password.Length < MinimumLength)
        {
            errors.Add($"A password must be at least {MinimumLength} characters.");
        }

        return errors;
    }
}
