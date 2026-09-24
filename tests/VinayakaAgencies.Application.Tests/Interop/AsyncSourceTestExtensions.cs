using VinayakaAgencies.Application.Interop;

namespace VinayakaAgencies.Application.Tests.Interop;

/// <summary>Drains an import source so a test can assert on what it produced.</summary>
internal static class AsyncSourceTestExtensions
{
    public static async Task<List<InteropRecord>> ReadAllAsync(
        this IInteropSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var records = new List<InteropRecord>();

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(record);
        }

        return records;
    }
}
