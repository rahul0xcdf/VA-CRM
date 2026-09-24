using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Streams a Tally XML file as records, one <c>TALLYMESSAGE</c> at a time.
/// </summary>
/// <remarks>
/// <para>
/// Only the message currently being parsed is held in memory, which is what §4a D2 asks for: a full
/// history is not a file this application gets to assume it can load. The whole-document
/// <see cref="TallyXmlReader.Read"/> overload exists for tests and small files and is deliberately
/// not what the importer calls.
/// </para>
/// <para>
/// The source is opened afresh on every enumeration, because an import reads the file twice — once
/// to validate everything, once to write it (see <see cref="IInteropSource"/>).
/// </para>
/// </remarks>
public sealed class TallyXmlSource : IInteropSource
{
    private readonly Func<CancellationToken, Task<Stream>> _open;

    public TallyXmlSource(string description, Func<CancellationToken, Task<Stream>> open)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(open);

        Description = description;
        _open = open;
    }

    public string Description { get; }

    public string FormatName => "Tally XML";

    /// <summary>Reads from a file on disk.</summary>
    public static TallyXmlSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new TallyXmlSource(
            Path.GetFileName(path),
            _ => Task.FromResult<Stream>(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true)));
    }

    /// <summary>Reads from a string already in memory. For tests and for pasted content.</summary>
    public static TallyXmlSource FromText(string xml, string description = "Tally XML")
    {
        ArgumentNullException.ThrowIfNull(xml);

        return new TallyXmlSource(
            description,
            _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false)));
    }

    public async IAsyncEnumerable<InteropRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,

            // An import file arrives from outside. A DTD is the one part of XML that can be made to
            // consume the machine's memory before a single record is read, and nothing Tally writes
            // needs one.
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        var stream = await _open(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            using var reader = XmlReader.Create(stream, settings);
            var ordinal = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Malformed XML has to arrive as a reported row rather than as an exception out of
                // the enumerator: §2 promises the owner a report of everything wrong with the file,
                // and "it stops being XML at line 900" is part of that report. A yield cannot sit
                // inside a catch, which is why the outcome is captured and acted on afterwards.
                var (moved, failure) = await TryReadAsync(reader).ConfigureAwait(false);

                if (failure is not null)
                {
                    yield return Malformed(ordinal + 1, failure);
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                if (reader.NodeType != XmlNodeType.Element
                    || !string.Equals(reader.LocalName, TallyXmlNames.TallyMessage, StringComparison.Ordinal))
                {
                    continue;
                }

                var lineNumber = (reader as IXmlLineInfo)?.LineNumber ?? 0;
                var (message, loadFailure) = await TryLoadAsync(reader, cancellationToken).ConfigureAwait(false);

                if (loadFailure is not null || message is null)
                {
                    yield return Malformed(ordinal + 1, loadFailure ?? "the message could not be read.");
                    yield break;
                }

                foreach (var element in message.Elements())
                {
                    ordinal++;

                    var location = lineNumber > 0
                        ? InteropSourceLocation.AtLine(ordinal, LineOf(element, lineNumber))
                        : new InteropSourceLocation(ordinal, $"record {ordinal}");

                    yield return TallyXmlReader.ParseMessage(element, location);
                }
            }
        }
    }

    private static async Task<(bool Moved, string? Failure)> TryReadAsync(XmlReader reader)
    {
        try
        {
            return (await reader.ReadAsync().ConfigureAwait(false), null);
        }
        catch (XmlException ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<(XElement? Message, string? Failure)> TryLoadAsync(
        XmlReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            // Through a subtree reader, not the outer one. XElement.LoadAsync insists the reader
            // finish at end-of-file, so handing it the document reader would load one message and
            // then throw because the rest of the file is still there. The subtree ends where the
            // message ends, which is exactly the boundary this needs.
            using var subtree = reader.ReadSubtree();

            return (await XElement
                .LoadAsync(subtree, LoadOptions.SetLineInfo, cancellationToken)
                .ConfigureAwait(false), null);
        }
        catch (XmlException ex)
        {
            return (null, ex.Message);
        }
    }

    private InteropRecord.Unreadable Malformed(int ordinal, string reason) =>
        new InteropRecord.Unreadable(
            new InteropSourceLocation(ordinal, Description),
            $"The file is not valid XML: {reason}",
            FormatName);

    private static int LineOf(XElement element, int fallback) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : fallback;
}
