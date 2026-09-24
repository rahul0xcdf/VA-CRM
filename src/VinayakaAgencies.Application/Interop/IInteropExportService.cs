namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Builds the canonical document out of the books, and writes it in a chosen format.
/// </summary>
/// <remarks>
/// Building and writing are separate calls because they fail for different reasons and at different
/// times. Building reads the database and can find nothing to export; writing touches a file and can
/// find the path unwritable. A screen that reported "export failed" for both would leave the owner
/// guessing which.
/// </remarks>
public interface IInteropExportService
{
    /// <summary>Reads the books into the canonical model.</summary>
    Task<InteropDocument> BuildAsync(
        InteropExportRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renders a built document to a stream.</summary>
    Task<InteropExportSummary> WriteAsync(
        InteropDocument document,
        InteropFormat format,
        Stream destination,
        CancellationToken cancellationToken = default);
}
