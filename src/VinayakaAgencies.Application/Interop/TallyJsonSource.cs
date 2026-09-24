using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Reads a Tally JSON file as records (TallyPrime 7.0+).
/// </summary>
/// <remarks>
/// <para>
/// Each <c>TALLYMESSAGE</c> entry is transcoded back to the element shape and handed to
/// <see cref="TallyXmlReader.ParseMessage"/>, so both formats are understood by one parser. The
/// alternative — a second parser reading JSON directly — is how the two formats end up accepting
/// different field sets without anyone noticing.
/// </para>
/// <para>
/// JSON is read as a document rather than incrementally. That is a real difference from
/// <see cref="TallyXmlSource"/> and it is deliberate: §4a D1 makes XML the mandatory path and the one
/// C8.1 is proved against, and a full history is expected to arrive as XML. If a JSON file ever does
/// arrive at migration scale, this is the class to convert to <c>Utf8JsonReader</c>, and nothing
/// above it changes.
/// </para>
/// </remarks>
public sealed class TallyJsonSource : IInteropSource
{
    private readonly Func<CancellationToken, Task<Stream>> _open;

    public TallyJsonSource(string description, Func<CancellationToken, Task<Stream>> open)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(open);

        Description = description;
        _open = open;
    }

    public string Description { get; }

    public string FormatName => "Tally JSON";

    public static TallyJsonSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new TallyJsonSource(
            Path.GetFileName(path),
            _ => Task.FromResult<Stream>(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true)));
    }

    public static TallyJsonSource FromText(string json, string description = "Tally JSON")
    {
        ArgumentNullException.ThrowIfNull(json);

        return new TallyJsonSource(
            description,
            _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false)));
    }

    public async IAsyncEnumerable<InteropRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stream = await _open(cancellationToken).ConfigureAwait(false);
        JsonNode? root = null;
        string? parseError = null;

        await using (stream.ConfigureAwait(false))
        {
            try
            {
                root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                parseError = ex.Message;
            }
        }

        if (parseError is not null)
        {
            // A malformed file is one reported problem, not an exception out of the import. The
            // validation pass is supposed to end with a report the owner can act on, and "the file
            // is not JSON" is the most actionable report there is.
            yield return new InteropRecord.Unreadable(
                new InteropSourceLocation(1, Description),
                $"The file is not valid JSON: {parseError}",
                FormatName);

            yield break;
        }

        var messages = MessagesOf(root);

        if (messages is null)
        {
            yield return new InteropRecord.Unreadable(
                new InteropSourceLocation(1, Description),
                "The file has no ENVELOPE/BODY/IMPORTDATA/REQUESTDATA/TALLYMESSAGE content, so it is "
                + "not a Tally export.",
                FormatName);

            yield break;
        }

        var ordinal = 0;

        for (var index = 0; index < messages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = InteropSourceLocation.AtIndex(ordinal + 1, TallyXmlNames.TallyMessage, index);

            if (messages[index] is not JsonObject message)
            {
                ordinal++;
                yield return new InteropRecord.Unreadable(location, "The entry is not an object.", FormatName);
                continue;
            }

            foreach (var (name, value) in message)
            {
                ordinal++;

                yield return TallyXmlReader.ParseMessage(
                    TallyJsonTranscoder.ElementFor(name, value),
                    InteropSourceLocation.AtIndex(ordinal, TallyXmlNames.TallyMessage, index));
            }
        }
    }

    private static IList<JsonNode?>? MessagesOf(JsonNode? root)
    {
        var requestData = root?[TallyXmlNames.Envelope]
            ?[TallyXmlNames.Body]
            ?[TallyXmlNames.ImportData]
            ?[TallyXmlNames.RequestData];

        return requestData?[TallyXmlNames.TallyMessage] switch
        {
            JsonArray array => array,

            // A single message is a legitimate shape for a one-record file, even though the writer
            // always emits an array. The node is wrapped in a plain array rather than a JsonArray
            // because a JsonNode may have only one parent, and this one already has it.
            JsonObject single => new JsonNode?[] { single },
            _ => null,
        };
    }
}
