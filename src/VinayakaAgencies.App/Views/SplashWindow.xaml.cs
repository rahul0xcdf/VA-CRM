using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Options;
using VinayakaAgencies.App.Branding;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// The branded startup screen shown while the host starts and the schema is brought up to date.
/// </summary>
/// <remarks>
/// It exists to give the startup work somewhere to be visible. Applying migrations on a cold start
/// is not instant, and a window that appears only once that finishes reads as a slow launch.
/// </remarks>
public partial class SplashWindow : Window
{
    public SplashWindow(IOptions<BrandingOptions> branding)
    {
        ArgumentNullException.ThrowIfNull(branding);

        InitializeComponent();

        var options = branding.Value;

        Wordmark.Text = options.DisplayName;
        Tagline.Text = options.Tagline;
        Monogram.Text = BuildMonogram(options.DisplayName);

        if (TryApplyLogo(options.LogoPath))
        {
            // The artwork already carries the trading name, so showing the drawn wordmark too
            // would print "Vinayaka Agencies" twice on the same screen.
            FallbackMark.Visibility = Visibility.Collapsed;
            LogoImage.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Updates the line under the progress sheen. Must be called on the UI thread.</summary>
    public void ReportStatus(string status) => StatusText.Text = status;

    /// <summary>
    /// Loads the owner's artwork, returning <c>false</c> when there is none to show.
    /// </summary>
    /// <remarks>
    /// A missing file is a supported state, not an error — the drawn monogram stands in until
    /// artwork exists, so a fresh clone still launches. An unreadable file costs the logo, never
    /// the launch.
    /// </remarks>
    private bool TryApplyLogo(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            // Decode eagerly so the file is not left locked for the life of the process, which
            // would stop the owner from replacing the logo while the app is running.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            LogoImage.Source = bitmap;
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Initials of the trading name, used until real artwork exists.</summary>
    private static string BuildMonogram(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "VA";
        }

        var initials = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(word => char.ToUpperInvariant(word[0]));

        return string.Concat(initials);
    }
}
