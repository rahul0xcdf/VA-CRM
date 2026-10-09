using System.IO;

namespace VinayakaAgencies.App.Tests.TestSupport;

/// <summary>
/// Locates the repository root from the test runner's working directory.
/// </summary>
/// <remarks>
/// The same helper as the one in <c>VinayakaAgencies.Domain.Tests</c>, duplicated rather than
/// shared because each test project stands alone and a shared test-support assembly would be a
/// new project in the solution for twenty lines. Guard tests that assert against files outside
/// the compiled assembly — here, the shipped <c>appsettings.json</c> — need a way back up from
/// <c>bin/Debug/net8.0-windows</c>, and the solution file is the one unique marker.
/// </remarks>
internal static class RepoRoot
{
    private const string SolutionFileName = "VinayakaAgencies.sln";

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
