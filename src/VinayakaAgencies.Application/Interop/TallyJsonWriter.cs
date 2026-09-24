using System.Text.Json;
using System.Text.Json.Nodes;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Writes an <see cref="InteropDocument"/> as Tally's JSON interchange form (TallyPrime 7.0+).
/// </summary>
/// <remarks>
/// The second serialiser §4a D1 asks for, and deliberately a thin one: it builds the identical
/// envelope <see cref="TallyXmlWriter"/> builds and transcodes it through
/// <see cref="TallyJsonTranscoder"/>. Adding a field to the export therefore adds it to both formats
/// at once, and the C8.2 round-trip through JSON is testing the same field set as the one through
/// XML rather than a parallel implementation that has drifted.
/// </remarks>
public static class TallyJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>Renders the document to a JSON string.</summary>
    public static string Write(InteropDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return TallyJsonTranscoder
            .ToJson(TallyXmlWriter.BuildEnvelope(document))
            .ToJsonString(Options);
    }

    /// <summary>Renders the document straight to a stream.</summary>
    public static async Task WriteAsync(
        InteropDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        JsonNode node = TallyJsonTranscoder.ToJson(TallyXmlWriter.BuildEnvelope(document));

        await JsonSerializer
            .SerializeAsync(destination, node, Options, cancellationToken)
            .ConfigureAwait(false);
    }
}
