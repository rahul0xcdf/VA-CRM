namespace MotorPumpSuite.App.Branding;

/// <summary>
/// The name and mark this install presents to its users.
/// </summary>
/// <remarks>
/// Kept in configuration rather than hard-coded because the solution and root namespace are still
/// <c>MotorPumpSuite</c> (open-questions.md #7) while the trading name shown on screen is
/// "Vinayaka Agencies". Separating the two means a later decision to rename the product — or to
/// resell this to a second dealer — is a settings edit, not a recompile.
/// </remarks>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string DisplayName { get; set; } = "Vinayaka Agencies";

    public string Tagline { get; set; } = "Motors  ·  Pumps  ·  Spares";

    /// <summary>
    /// Optional logo file, relative to the application directory.
    /// </summary>
    /// <remarks>
    /// Empty until the owner supplies artwork; the splash falls back to a drawn monogram so the
    /// startup screen never shows a broken image placeholder. Dropping a file at
    /// <c>Assets\logo.png</c> is the only change needed to switch over.
    /// </remarks>
    public string LogoPath { get; set; } = "Assets/logo.png";
}
