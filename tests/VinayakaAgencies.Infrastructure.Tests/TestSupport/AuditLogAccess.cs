using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Infrastructure.DependencyInjection;

namespace VinayakaAgencies.Infrastructure.Tests.TestSupport;

/// <summary>
/// Reads the edit log, and reaches the raw context the way a mistake inside Infrastructure would.
/// </summary>
/// <remarks>
/// <c>AppDbContext</c> is internal and stays that way (ADR-0001), so this resolves it from the
/// container by name and works through the public <see cref="DbContext"/> surface. No
/// <c>InternalsVisibleTo</c>: granting the test assembly access to Infrastructure's internals would
/// let these tests exercise a seam no caller can reach, and §11.4's tamper guard is precisely a
/// guard on what a caller <i>can</i> reach.
/// <para>
/// There is no <c>IEditLogQueryService</c> yet — §11.4 item 4 makes that read-only service the only
/// API onto the log, and it arrives with the Phase 1 reporting queries. Until then a test reads the
/// table directly, which is a test concern and not a production path.
/// </para>
/// </remarks>
public static class AuditLogAccess
{
    private const string ContextTypeName = "VinayakaAgencies.Infrastructure.Persistence.AppDbContext";

    /// <summary>The context instance for this scope, seen only as a <see cref="DbContext"/>.</summary>
    public static DbContext RawContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var contextType = typeof(InfrastructureServiceCollectionExtensions).Assembly
            .GetType(ContextTypeName, throwOnError: true)!;

        return (DbContext)services.GetRequiredService(contextType);
    }

    /// <summary>Every log row, in the order the database assigned sequences.</summary>
    public static async Task<IReadOnlyList<AuditLogEntry>> AllAsync(IServiceProvider services) =>
        await RawContext(services)
            .Set<AuditLogEntry>()
            .AsNoTracking()
            .OrderBy(row => row.Sequence)
            .ToListAsync();

    /// <summary>The log rows written for one entity type.</summary>
    public static async Task<IReadOnlyList<AuditLogEntry>> ForEntityAsync(
        IServiceProvider services,
        string entityType) =>
        await RawContext(services)
            .Set<AuditLogEntry>()
            .AsNoTracking()
            .Where(row => row.EntityType == entityType)
            .OrderBy(row => row.Sequence)
            .ToListAsync();
}
