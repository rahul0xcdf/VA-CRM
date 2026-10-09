using System.IO;
using System.Reflection;
using System.Text.Json;
using VinayakaAgencies.App.Tests.TestSupport;
using VinayakaAgencies.Application.Auditing;

namespace VinayakaAgencies.App.Tests.Guards;

/// <summary>
/// Defences (d) and (e) of checkpoint C1.4 — "the audit trail cannot be turned off"
/// (`specs/phase-1-accounting-core.md` §11.4, non-negotiable #3, MCA Rule 3(1)).
/// </summary>
/// <remarks>
/// Defences (a) to (c) are asserted elsewhere and were in place from the start: the log row does
/// not derive from <c>Entity</c>, so no repository can be opened over it
/// (<c>AuditLogEntryTests.AuditLogEntry_DoesNotDeriveFromEntity</c>); marking a row modified or
/// deleted through the context throws
/// (<c>AuditTrailCaptureTests.SaveChangesAsync_WhenALogRowIsMarked…</c>); and raw SQL against the
/// table is aborted by the trigger
/// (<c>AuditTrailCaptureTests.RawConnection_AlteringOrRemovingALogRow_IsAbortedByTheTrigger</c>).
/// <para>
/// These last two were specified and never written. They are the two that guard against the
/// trail being switched off rather than written around — a member someone adds in good faith
/// ("purge the log before the demo"), or a configuration key that makes auditing optional. Both
/// are the kind of change that looks reasonable in review and is unlawful in production, so the
/// build is where they should be refused.
/// </para>
/// </remarks>
public class AuditTrailCannotBeDisabledGuardTests
{
    /// <summary>Verbs that would amount to switching the trail off or editing it after the fact.</summary>
    private static readonly string[] ForbiddenVerbs = ["Disable", "Purge", "Clear", "Delete", "Truncate"];

    [Fact]
    public void AuditTypes_InApplicationAndApp_DeclareNoMemberThatCouldSwitchTheTrailOff()
    {
        var assemblies = new[]
        {
            typeof(IAuditLogReader).Assembly,
            typeof(global::VinayakaAgencies.App.App).Assembly,
        };

        var offenders = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsAuditType)
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => new { Type = type, Member = member }))
            .Where(pair => ForbiddenVerbs.Any(verb =>
                pair.Member.Name.Contains(verb, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => $"{pair.Type.Name}.{pair.Member.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "no audit type may expose a way to disable, purge, clear or delete the edit log "
            + "(non-negotiable #3 — no user, including admin, may switch it off); "
            + $"found: {string.Join(", ", offenders)}");

        // The scan is only worth anything if it actually found the audit surface to scan.
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Count(IsAuditType)
            .ShouldBeGreaterThan(0, "the scan matched no audit type, so it proved nothing");
    }

    [Fact]
    public void Appsettings_OnDisk_HasNoKeyThatCouldSwitchTheTrailOff()
    {
        var path = Path.Combine(
            RepoRoot.Find(), "src", "VinayakaAgencies.App", "appsettings.json");

        File.Exists(path).ShouldBeTrue($"expected the shipped configuration at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var offenders = new List<string>();
        Walk(document.RootElement, string.Empty, offenders);

        offenders.ShouldBeEmpty(
            "configuration must not offer any key that governs auditing — a trail that can be "
            + "turned off in a text file is not a trail; "
            + $"found: {string.Join(", ", offenders)}");
    }

    private static void Walk(JsonElement element, string path, List<string> offenders)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childPath = path.Length == 0 ? property.Name : $"{path}:{property.Name}";

                if (property.Name.Contains("audit", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Replace("-", string.Empty, StringComparison.Ordinal)
                        .Replace("_", string.Empty, StringComparison.Ordinal)
                        .Contains("editlog", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add(childPath);
                }

                Walk(property.Value, childPath, offenders);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;

            foreach (var item in element.EnumerateArray())
            {
                Walk(item, $"{path}[{index++}]", offenders);
            }
        }
    }

    /// <summary>
    /// A type is "an audit type" if its name names the trail. Deliberately broad: the point is to
    /// catch a new <c>AuditSettings</c> or <c>EditLogMaintenance</c> on the day it is written.
    /// </summary>
    private static bool IsAuditType(Type type) =>
        type.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase)
        || type.Name.Contains("EditLog", StringComparison.OrdinalIgnoreCase);
}
