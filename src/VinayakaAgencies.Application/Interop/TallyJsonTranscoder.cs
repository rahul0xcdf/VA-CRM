using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Converts between the Tally envelope as XML and the same envelope as JSON.
/// </summary>
/// <remarks>
/// <para>
/// §4a D1 puts both formats in scope and asks for two serialisers over one model rather than two
/// pipelines. This is how that promise is kept literally: the JSON writer builds the same
/// <see cref="XElement"/> envelope the XML writer builds and transcodes it, and the JSON reader
/// transcodes back and hands the result to <see cref="TallyXmlReader.ParseMessage"/>. A field can
/// therefore never exist in one format and be missing from the other — there is only one place
/// fields are decided.
/// </para>
/// <para>
/// The ambiguity every XML-to-JSON conversion has to resolve is when a single child becomes a
/// one-element array rather than an object. Tally answers it in its own naming: a repeating
/// collection is named with a <c>.LIST</c> suffix. That rule is used here rather than "array if it
/// happens to occur twice", which would change the shape of a file depending on how many vouchers
/// it holds.
/// </para>
/// <para>
/// Attributes carry an <c>@</c> prefix. That is the common convention rather than a documented Tally
/// one, and is among the assumptions a real file has to settle (open-questions.md #22).
/// </para>
/// </remarks>
internal static class TallyJsonTranscoder
{
    private const string AttributePrefix = "@";
    private const string ListSuffix = ".LIST";

    /// <summary>True when an element name denotes a repeating collection.</summary>
    internal static bool IsCollection(string name) =>
        name.EndsWith(ListSuffix, StringComparison.Ordinal)
        || string.Equals(name, TallyXmlNames.TallyMessage, StringComparison.Ordinal);

    internal static JsonObject ToJson(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return new JsonObject { [element.Name.LocalName] = NodeFor(element) };
    }

    private static JsonNode? NodeFor(XElement element)
    {
        var hasChildren = element.Elements().Any();
        var hasAttributes = element.Attributes().Any();

        if (!hasChildren && !hasAttributes)
        {
            return JsonValue.Create(element.Value);
        }

        var result = new JsonObject();

        foreach (var attribute in element.Attributes())
        {
            result[AttributePrefix + attribute.Name.LocalName] = JsonValue.Create(attribute.Value);
        }

        foreach (var group in element.Elements().GroupBy(child => child.Name.LocalName, StringComparer.Ordinal))
        {
            var name = group.Key;

            if (IsCollection(name))
            {
                var array = new JsonArray();

                foreach (var child in group)
                {
                    array.Add(NodeFor(child));
                }

                result[name] = array;
                continue;
            }

            // A non-.LIST name occurring more than once is not something Tally's schema produces, but
            // a hand-edited file can. Carrying it as an array loses nothing and is better than
            // keeping whichever copy happened to be last.
            if (group.Skip(1).Any())
            {
                var array = new JsonArray();

                foreach (var child in group)
                {
                    array.Add(NodeFor(child));
                }

                result[name] = array;
                continue;
            }

            result[name] = NodeFor(group.First());
        }

        return result;
    }

    /// <summary>Rebuilds the XML element a JSON object describes.</summary>
    internal static XElement? ToXml(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var first = root.FirstOrDefault();

        // An array at the root has no single element to become. Nothing Tally writes looks like
        // that, and returning null lets the caller report an unreadable file rather than inventing a
        // wrapper element that the reader would then fail to recognise.
        return first.Key is null || first.Value is JsonArray
            ? null
            : ElementFor(first.Key, first.Value);
    }

    internal static XElement ElementFor(string name, JsonNode? node)
    {
        var element = new XElement(name);

        switch (node)
        {
            case null:
                break;

            case JsonValue value:
                element.Value = value.ToString();
                break;

            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    if (key.StartsWith(AttributePrefix, StringComparison.Ordinal))
                    {
                        element.SetAttributeValue(key[AttributePrefix.Length..], child?.ToString() ?? string.Empty);
                        continue;
                    }

                    if (child is JsonArray array)
                    {
                        foreach (var item in array)
                        {
                            element.Add(ElementFor(key, item));
                        }

                        continue;
                    }

                    element.Add(ElementFor(key, child));
                }

                break;

            // An array is always destructured by the object that holds it, above, so one cannot
            // reach here: a repeated name becomes repeated sibling elements, which is what XML has
            // instead of arrays.
            default:
                break;
        }

        return element;
    }
}
