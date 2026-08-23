namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// Discovers every <see cref="IScreenModule"/> in this assembly.
/// </summary>
/// <remarks>
/// Two modules claiming nothing is not an error — an empty catalog simply leaves an empty pane —
/// but two modules with the same <see cref="IScreenModule.Name"/> would render as one merged
/// group, so that case fails loudly rather than quietly blending menus.
/// </remarks>
public static class ScreenModuleCatalog
{
    public static IReadOnlyList<IScreenModule> Discover()
    {
        var modules = typeof(ScreenModuleCatalog).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Where(typeof(IScreenModule).IsAssignableFrom)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(Create)
            .ToList();

        var duplicate = modules
            .GroupBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"More than one screen module is named '{duplicate.Key}'. " +
                "Module names become navigation groups, so they must be unique.");
        }

        return modules
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IScreenModule Create(Type type) =>
        (IScreenModule)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(
                $"{type.FullName} implements {nameof(IScreenModule)} but could not be created. " +
                "A screen module needs a public parameterless constructor, because the shell " +
                "discovers modules by reflection rather than through the container."));
}
