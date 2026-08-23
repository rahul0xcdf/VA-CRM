namespace VinayakaAgencies.App.Branding;

/// <summary>
/// The name and mark this install presents to its users.
/// </summary>
/// <remarks>
/// Kept in configuration rather than hard-coded so that the on-screen name is a settings edit
/// rather than a recompile — which matters if this is ever resold to a second dealer. The root
/// namespace now matches the trading name (open-questions.md #7, resolved), but the two remain
/// separate concerns: the namespace is a compile-time identity, this is what the operator reads.
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
