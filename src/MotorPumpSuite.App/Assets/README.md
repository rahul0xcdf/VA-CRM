# Assets

Drop the Vinayaka Agencies logo here as **`logo.png`**.

Nothing else needs to change: `BrandingOptions.LogoPath` already points at `Assets/logo.png`, the
build copies this folder to the output directory, and `SplashWindow` swaps the drawn "VA" monogram
for the image the moment the file exists. If the file is missing or unreadable the monogram stays,
so a missing logo never blocks a launch.

Guidance for the artwork:

- Square, transparent background (PNG), at least 256 × 256 px.
- It renders at 60 × 60 px on the startup screen, so avoid fine detail or small text.
- It sits on a dark indigo/teal ground — check that it reads against a dark backdrop.

The display name and tagline are configuration, not code: edit `Branding` in `appsettings.json`.
