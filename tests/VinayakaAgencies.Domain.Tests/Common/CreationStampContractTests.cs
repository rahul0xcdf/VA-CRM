using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tests.Common;

/// <summary>
/// The stamping interceptor keys off these interfaces, so their relationship is a contract rather
/// than a convenience: an append-only type records only who created it, a mutable one records that
/// plus who last changed it.
/// </summary>
public class CreationStampContractTests
{
    [Fact]
    public void IAuditStamped_ExtendsICreationStamped()
    {
        typeof(ICreationStamped).IsAssignableFrom(typeof(IAuditStamped)).ShouldBeTrue(
            "every audit-stamped entity is also creation-stamped; splitting the two lets an " +
            "append-only type such as VoucherRevision get an author without pretending it has a " +
            "Modified* pair.");
    }

    [Fact]
    public void ICreationStamped_ExposesSettableCreationStamps()
    {
        // The interceptor writes through these setters, so they must stay settable on the interface
        // even though the implementing entities keep everything else private.
        typeof(ICreationStamped).GetProperty(nameof(ICreationStamped.CreatedAtUtc))!
            .CanWrite.ShouldBeTrue();
        typeof(ICreationStamped).GetProperty(nameof(ICreationStamped.CreatedBy))!
            .CanWrite.ShouldBeTrue();
    }
}
