namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// A group of related screens contributed to the shell's navigation pane.
/// </summary>
/// <remarks>
/// Modules are discovered by reflection over this assembly (see
/// <see cref="ScreenModuleCatalog.Discover"/>), mirroring how the Infrastructure dialect discovers
/// SQL statement sources: adding a screen to the product means adding a file, not editing a
/// registration list, so parallel feature work never collides on a shared composition-root edit.
/// </remarks>
public interface IScreenModule
{
    /// <summary>Caption of the navigation group this module contributes to. Unique per module.</summary>
    string Name { get; }

    /// <summary>Sort position among groups; lower appears first.</summary>
    int Order { get; }

    IReadOnlyList<ScreenDescriptor> Screens { get; }
}
