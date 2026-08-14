using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Tests.Common;

/// <summary>
/// A second, unrelated <see cref="Entity"/> subclass. <see cref="Entity.Equals(Entity?)"/> compares
/// by type as well as id, so this type exists purely to prove that two different entity kinds
/// sharing a GUID are not treated as equal — a real bug class if the id alone were used (e.g. a
/// stray collision between a voucher id and a stock item id imported from Tally).
/// </summary>
internal sealed class AnotherTestEntity : Entity
{
    public AnotherTestEntity(Guid id)
        : base(id)
    {
    }
}
