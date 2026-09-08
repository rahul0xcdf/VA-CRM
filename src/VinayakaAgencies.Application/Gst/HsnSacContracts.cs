namespace VinayakaAgencies.Application.Gst;

/// <summary>Read model for one HSN/SAC master row.</summary>
public sealed record HsnSacSummary(
    Guid Id,
    string Code,
    string? Description,
    bool IsActive);

/// <summary>
/// Creates or revises an HSN/SAC master row. A null <see cref="Id"/> creates; a value revises.
/// </summary>
public sealed record SaveHsnSacRequest(
    Guid? Id,
    string Code,
    string? Description,
    bool IsActive = true);
