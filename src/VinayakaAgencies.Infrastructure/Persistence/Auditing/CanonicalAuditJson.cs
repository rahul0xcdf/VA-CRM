using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace VinayakaAgencies.Infrastructure.Persistence.Auditing;

/// <summary>
/// Renders a tracked entity's property values as the canonical JSON the edit log stores and hashes.
/// </summary>
/// <remarks>
/// <c>specs/phase-1-accounting-core.md</c> §11.3. These strings are inputs to the SHA-256 chain in
/// §11.5, so the encoding has to be stable across .NET versions, machine locales and provider
/// changes — a re-verification that fails because a runtime upgrade changed a number's rendering is
/// indistinguishable, to an auditor, from a re-verification that fails because someone edited a row.
/// Every rule below exists to remove one such source of drift:
/// <list type="bullet">
/// <item>only scalar and shadow properties are written — <see cref="EntityEntry.Properties"/>
/// excludes navigations by construction, so a graph is never serialised twice (each entity has its
/// own change-tracker entry and therefore its own log row);</item>
/// <item>properties are ordered ordinally by name, so the order does not depend on the model's
/// declaration order or on a future reordering of a class's members;</item>
/// <item><see cref="decimal"/> is written as an invariant-culture string at 4 decimal places, never
/// as a JSON number, so no reader can round-trip a rupee value through a double;</item>
/// <item>enums are written by name, so a later renumbering of an enum cannot silently change what a
/// historical row means;</item>
/// <item>null is written as JSON <c>null</c> rather than omitted, so a property that was cleared and
/// a property that never existed are distinguishable.</item>
/// </list>
/// </remarks>
internal static class CanonicalAuditJson
{
    /// <summary>Decimal places money and quantity are recorded to (ADR-0003, CLAUDE.md).</summary>
    private const string DecimalFormat = "0.0000";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,

        // Relaxed escaping writes non-ASCII text (a party name in Kannada, a rupee sign) as literal
        // UTF-8 rather than \uXXXX escapes. Both forms are deterministic; literal text is what an
        // auditor reading the column wants, and the "unsafe" in the name refers to embedding JSON
        // in HTML, which this column never is.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The values the entity currently holds — the after-image.</summary>
    public static string CurrentImage(EntityEntry entry) => Write(entry, current: true);

    /// <summary>The values the entity was loaded with — the before-image.</summary>
    public static string OriginalImage(EntityEntry entry) => Write(entry, current: false);

    /// <summary>
    /// Formats one value as bare text, using the same rules the images use.
    /// </summary>
    /// <remarks>
    /// Identifier columns (<c>EntityId</c>, <c>AggregateId</c>) go through this, so an id in a
    /// column and the same id inside a before/after image are written identically and a search for
    /// one finds the other.
    /// </remarks>
    public static string Scalar(object? value) => value switch
    {
        null => string.Empty,

        // Ahead of the numeric cases deliberately: recording the name rather than the number is the
        // whole point of §11.3's enum rule.
        Enum enumValue => enumValue.ToString(),

        string text => text,
        bool flag => flag ? "true" : "false",
        decimal money => money.ToString(DecimalFormat, CultureInfo.InvariantCulture),
        DateOnly businessDate => businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset stamp => stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime stamp => stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        Guid id => id.ToString("D", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),

        // No accounting type may use binary floating point (CLAUDE.md, ADR-0003). Should one reach
        // here anyway, the round-trip format records exactly what was stored rather than a value
        // that re-reads differently.
        double approximate => approximate.ToString("R", CultureInfo.InvariantCulture),
        float approximate => approximate.ToString("R", CultureInfo.InvariantCulture),

        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>
    /// One value rendered as the JSON fragment it contributes to an image.
    /// </summary>
    /// <remarks>
    /// The same <see cref="WriteValue"/> the images use, so asserting on this asserts on the real
    /// encoding rather than on a parallel implementation. Returns <c>"1234.5000"</c> — quoted,
    /// because §11.3 requires money to be a JSON string and not a number.
    /// </remarks>
    public static string Json(object? value)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteValue(writer, value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Write(EntityEntry entry, bool current)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();

            foreach (var property in entry.Properties
                .OrderBy(p => p.Metadata.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Metadata.Name);
                WriteValue(writer, current ? property.CurrentValue : property.OriginalValue);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Writes one value. Only <c>null</c>, booleans and integers become JSON literals; everything
    /// else — including <see cref="decimal"/> — becomes a string, via <see cref="Scalar"/>.
    /// </summary>
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;

            case bool flag:
                writer.WriteBooleanValue(flag);
                break;

            // A boxed enum matches none of the integral patterns, so these are genuine integers.
            case byte or sbyte or short or ushort or int:
                writer.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                break;

            case uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;

            case ulong unsigned:
                writer.WriteNumberValue(unsigned);
                break;

            default:
                writer.WriteStringValue(Scalar(value));
                break;
        }
    }
}
