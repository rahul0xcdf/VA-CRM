namespace VinayakaAgencies.Domain.Tests.TestSupport;

/// <summary>
/// A deliberately minimal parser for the flat <c>key: value</c> YAML frontmatter block that
/// Claude Code reads from the top of each <c>.claude/agents/*.md</c> file.
/// </summary>
/// <remarks>
/// The test suite has no dependency on a YAML library — frontmatter here is always a single-level
/// block of scalar keys bounded by <c>---</c> lines, so a line-oriented parse is sufficient and
/// keeps this guard test free of an extra package reference for something this project doesn't
/// otherwise need.
/// </remarks>
internal static class AgentFrontmatter
{
    public static IReadOnlyDictionary<string, string> Parse(string agentFilePath)
    {
        var lines = File.ReadAllLines(agentFilePath);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return result;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.Trim() == "---")
            {
                break;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }
}
