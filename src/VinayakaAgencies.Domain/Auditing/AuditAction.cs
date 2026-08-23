namespace VinayakaAgencies.Domain.Auditing;

/// <summary>
/// What happened to the audited row.
/// </summary>
/// <remarks>
/// MCA Rule 3(1) requires each of these to be recorded for every voucher and master, with the
/// before and after values. In force since 1 April 2023 — see <c>project.md</c> §4.1.
/// </remarks>
public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
}
