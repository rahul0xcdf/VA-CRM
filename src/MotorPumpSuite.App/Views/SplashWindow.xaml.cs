using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Options;
using MotorPumpSuite.App.Branding;

namespace MotorPumpSuite.App.Views;

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

        TryApplyLogo(options.LogoPath);
    }

    /// <summary>Updates the line under the progress sheen. Must be called on the UI thread.</summary>
    public void ReportStatus(string status) => StatusText.Text = status;

    /// <summary>
    /// Swaps the drawn monogram for the owner's artwork when it is present.
    /// </summary>
    /// <remarks>
    /// The logo has not been supplied yet, so a missing file is the expected case, not an error —
    /// hence the silent fallback to the monogram rather than a throw or a broken-image box.
    /// </remarks>
    private void TryApplyLogo(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(fullPath))
        {
            return;
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
            LogoImage.Visibility = Visibility.Visible;
            Monogram.Visibility = Visibility.Collapsed;
        }
        catch (NotSupportedException)
        {
            // An unreadable or corrupt image file should cost us the logo, not the launch.
        }
        catch (IOException)
        {
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
