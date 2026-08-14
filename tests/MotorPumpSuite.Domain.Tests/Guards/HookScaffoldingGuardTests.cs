using System.Text.Json;
using System.Text.RegularExpressions;
using MotorPumpSuite.Domain.Tests.TestSupport;

namespace MotorPumpSuite.Domain.Tests.Guards;

/// <summary>
/// C0.5 — "Both hook types exist and fire correctly on a test edit". project.md §8 requires four
/// hook events (SessionStart, PreToolUse, Stop, PreCompact); the spec-gate behaviour itself
/// (block/allow on the phase spec's Status field) is exercised separately by running
/// <c>spec-gate.ps1</c> as a process — this test only proves the declarative wiring in
/// <c>settings.json</c> is complete and that every script it points at actually exists on disk, so
/// a missing hook script fails at test time rather than silently no-op'ing inside a live session.
/// </summary>
public class HookScaffoldingGuardTests
{
    private static readonly string[] RequiredHookEvents = ["SessionStart", "PreToolUse", "Stop", "PreCompact"];

    private static string SettingsPath => Path.Combine(RepoRoot.Find(), ".claude", "settings.json");

    [Fact]
    public void SettingsJson_Exists()
    {
        File.Exists(SettingsPath).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(RequiredEvents))]
    public void SettingsJson_DeclaresHookEvent(string eventName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));

        var hooks = document.RootElement.GetProperty("hooks");

        hooks.TryGetProperty(eventName, out var eventElement).ShouldBeTrue(
            $"settings.json is missing the '{eventName}' hook event");
        eventElement.GetArrayLength().ShouldBeGreaterThan(0, $"'{eventName}' hook has no entries");
    }

    public static IEnumerable<object[]> RequiredEvents() =>
        RequiredHookEvents.Select(name => new object[] { name });

    [Fact]
    public void SettingsJson_EveryReferencedHookScript_ExistsOnDisk()
    {
        var hooksDirectory = Path.Combine(RepoRoot.Find(), ".claude", "hooks");
        var scriptNames = ExtractReferencedScriptNames();

        scriptNames.ShouldNotBeEmpty("expected at least one *.ps1 script reference in settings.json");

        foreach (var scriptName in scriptNames)
        {
            var scriptPath = Path.Combine(hooksDirectory, scriptName);
            File.Exists(scriptPath).ShouldBeTrue(
                $"hook script '{scriptName}' referenced in settings.json does not exist at {scriptPath}");
        }
    }

    /// <summary>
    /// Walks every hook's <c>command</c> string (already JSON-unescaped by <see cref="JsonDocument"/>,
    /// so the path separator here is a single backslash) and pulls out the <c>*.ps1</c> file name
    /// each one invokes via <c>-File</c>.
    /// </summary>
    private static List<string> ExtractReferencedScriptNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        var hooks = document.RootElement.GetProperty("hooks");
        var scriptNames = new List<string>();

        foreach (var eventProperty in hooks.EnumerateObject())
        {
            foreach (var group in eventProperty.Value.EnumerateArray())
            {
                foreach (var hook in group.GetProperty("hooks").EnumerateArray())
                {
                    var command = hook.GetProperty("command").GetString() ?? string.Empty;
                    var match = Regex.Match(command, @"([A-Za-z0-9_.-]+\.ps1)");
                    if (match.Success)
                    {
                        scriptNames.Add(match.Groups[1].Value);
                    }
                }
            }
        }

        return scriptNames.Distinct().ToList();
    }

    [Fact]
    public void HooksDirectory_ContainsTheFourExpectedScripts()
    {
        var hooksDirectory = Path.Combine(RepoRoot.Find(), ".claude", "hooks");

        var expected = new[] { "session-start.ps1", "spec-gate.ps1", "stop-gate.ps1", "pre-compact.ps1" };

        foreach (var script in expected)
        {
            File.Exists(Path.Combine(hooksDirectory, script)).ShouldBeTrue($"expected hook script {script} to exist");
        }
    }
}
