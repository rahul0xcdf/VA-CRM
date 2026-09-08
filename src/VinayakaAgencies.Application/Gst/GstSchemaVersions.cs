namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// The GSTN offline-utility schema versions this build targets, pinned at implementation start
/// (phase-5-gst-returns.md §4a).
/// </summary>
/// <remarks>
/// Every exported return is stamped with the version it was produced for plus the export
/// timestamp (spec §2), so a schema change is a visible, dated event rather than a silent break:
/// a portal rejecting an old-format file can be traced to the exact build that wrote it.
/// </remarks>
public static class GstSchemaVersions
{
    /// <summary>The GSTN GSTR-1 offline-utility JSON shape every export in this build follows.</summary>
    public const string CurrentGstr1 = "v3.2.4";

    /// <summary>The portal GSTR-2B JSON shape the importer in this build parses.</summary>
    public const string CurrentGstr2B = "v3.2.4";
}
