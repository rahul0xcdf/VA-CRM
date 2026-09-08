namespace VinayakaAgencies.App.ViewModels;

/// <summary>One display row of the HSN/SAC master grid: the code plus the rate currently
/// effective for it (already formatted at the view-model boundary).</summary>
public sealed record HsnSacCodeRow(
    Guid Id,
    string Code,
    string Description,
    bool IsActive,
    string RatePercent,
    string CessPercent);
