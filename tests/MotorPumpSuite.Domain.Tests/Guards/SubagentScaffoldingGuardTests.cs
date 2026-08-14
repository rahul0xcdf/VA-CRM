using MotorPumpSuite.Domain.Tests.TestSupport;

namespace MotorPumpSuite.Domain.Tests.Guards;

/// <summary>
/// C0.4 — "All 8 subagent files exist". project.md §7 defines exactly 8 subagents, each of which
/// must carry <c>name</c>/<c>description</c>/<c>tools</c>/<c>model</c> YAML frontmatter for Claude
/// Code to load it. A missing or malformed agent file silently drops that agent's remit onto
/// whichever agent happens to pick up the work — this test exists to catch that at the earliest
/// possible point rather than during a live session.
/// </summary>
public class SubagentScaffoldingGuardTests
{
    private static readonly string[] RequiredFrontmatterKeys = ["name", "description", "tools", "model"];

    private static string AgentsDirectory => Path.Combine(RepoRoot.Find(), ".claude", "agents");

    [Fact]
    public void AgentsDirectory_ContainsExactlyEightMarkdownFiles()
    {
        var files = Directory.GetFiles(AgentsDirectory, "*.md");

        files.Length.ShouldBe(8, $"expected 8 agent files, found: {string.Join(", ", files.Select(Path.GetFileName))}");
    }

    public static IEnumerable<object[]> AgentFilePaths()
    {
        // Enumerated at discovery time so that, if the count guard above already caught a missing
        // file, this still yields whatever files do exist rather than throwing before xUnit can
        // report the more specific per-file frontmatter failures too.
        var directory = Path.Combine(RepoRoot.Find(), ".claude", "agents");

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(directory, "*.md").OrderBy(f => f))
        {
            yield return [file];
        }
    }

    [Theory]
    [MemberData(nameof(AgentFilePaths))]
    public void AgentFile_Frontmatter_ContainsAllFourRequiredKeys(string agentFilePath)
    {
        var frontmatter = AgentFrontmatter.Parse(agentFilePath);

        foreach (var key in RequiredFrontmatterKeys)
        {
            frontmatter.ContainsKey(key).ShouldBeTrue(
                $"{Path.GetFileName(agentFilePath)} is missing '{key}:' in its frontmatter");
        }
    }

    [Theory]
    [MemberData(nameof(AgentFilePaths))]
    public void AgentFile_Frontmatter_HasNonBlankValuesForAllFourKeys(string agentFilePath)
    {
        var frontmatter = AgentFrontmatter.Parse(agentFilePath);

        foreach (var key in RequiredFrontmatterKeys)
        {
            if (frontmatter.TryGetValue(key, out var value))
            {
                value.ShouldNotBeNullOrWhiteSpace(
                    $"{Path.GetFileName(agentFilePath)}'s '{key}:' value must not be blank");
            }
        }
    }

    [Fact]
    public void ComplianceAgent_Model_IsFrontierTierNotDowngraded()
    {
        // CLAUDE.md: "compliance-agent runs at the frontier model tier. Do not downgrade it." At
        // the time this test was written the frontier tier alias is "opus" — see project.md §7 and
        // the agent's own file. Any change to that value is a deliberate downgrade decision that
        // should be caught here, not discovered mid-Phase-5/6/7 GST work.
        var complianceAgentPath = Path.Combine(AgentsDirectory, "compliance-agent.md");

        File.Exists(complianceAgentPath).ShouldBeTrue("compliance-agent.md must exist");

        var frontmatter = AgentFrontmatter.Parse(complianceAgentPath);

        frontmatter.ContainsKey("model").ShouldBeTrue();
        frontmatter["model"].ShouldBe("opus");
    }

    [Fact]
    public void AllAgentFiles_HaveDistinctNameValues()
    {
        var names = AgentFilePaths()
            .Select(row => (string)row[0])
            .Select(path => AgentFrontmatter.Parse(path))
            .Where(fm => fm.TryGetValue("name", out _))
            .Select(fm => fm["name"])
            .ToList();

        names.Distinct().Count().ShouldBe(names.Count, "each subagent's 'name:' must be unique");
    }
}
