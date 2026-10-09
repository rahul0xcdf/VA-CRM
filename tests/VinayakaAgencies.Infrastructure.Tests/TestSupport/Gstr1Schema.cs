using System.Text.Json.Nodes;
using Json.Schema;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// Loads and evaluates the GSTR-1 return schemas kept in <c>Gst/Schemas</c> (checkpoint C5.1).
/// </summary>
/// <remarks>
/// Shared by the schema's own unit tests and by the end-to-end export test, so there is one
/// definition of where the schemas live and how a failure is reported.
/// </remarks>
internal static class Gstr1Schema
{
    /// <summary>
    /// The shape this build emits. <b>Not the GSTN schema</b> — see the file's own description and
    /// <c>open-questions.md</c> #23.
    /// </summary>
    public const string AsBuiltFileName = "gstr1-as-built.schema.json";

    public static JsonSchema Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Gst", "Schemas", fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Expected a GSTR-1 schema at {path}. The test project's csproj copies "
                + @"Gst\Schemas\*.json to the output directory.",
                path);
        }

        return JsonSchema.FromText(File.ReadAllText(path));
    }

    public static EvaluationResults Evaluate(string fileName, JsonNode document) =>
        Load(fileName).Evaluate(
            document,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

    /// <summary>Flattens a failed evaluation into one assertion message.</summary>
    public static string Explain(EvaluationResults results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var errors = results.Details
            .Where(detail => detail.HasErrors)
            .SelectMany(detail => detail.Errors!
                .Select(error => $"{detail.InstanceLocation}: {error.Value}"))
            .ToList();

        return errors.Count == 0
            ? "the evaluation reported no error detail"
            : string.Join("; ", errors);
    }
}
