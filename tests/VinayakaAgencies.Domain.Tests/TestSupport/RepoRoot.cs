namespace VinayakaAgencies.Domain.Tests.TestSupport;

/// <summary>
/// Locates the repository root from the test runner's working directory.
/// </summary>
/// <remarks>
/// The guard tests in this project (C0.4, C0.5, C0.6, C0.8) assert against files that live outside
/// the compiled test assembly — csproj files, <c>.claude/agents</c>, <c>.claude/settings.json</c>.
/// <c>AppContext.BaseDirectory</c> is the test's <c>bin/Debug/net8.0</c> output folder, several
/// levels below the solution file, so every guard test needs a reliable way back up to the root.
/// The solution file is the one marker guaranteed to exist and to be unique.
/// </remarks>
internal static class RepoRoot
{
    private const string SolutionFileName = "VinayakaAgencies.sln";

    /// <summary>
    /// Walks upward from <see cref="AppContext.BaseDirectory"/> until a directory containing
    /// <c>VinayakaAgencies.sln</c> is found.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no ancestor directory contains the solution file — this would mean the test
    /// assembly is running somewhere unexpected, which is itself a signal worth failing loudly on.
    /// </exception>
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles(SolutionFileName).Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {SolutionFileName} by walking up from {AppContext.BaseDirectory}.");
    }
}
