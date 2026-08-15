using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tests.Common;

/// <summary>
/// Minimal concrete <see cref="Entity"/> used to exercise the abstract base type's identity and
/// equality behaviour without pulling in a real domain concept (Phase 0 has exactly one: Company).
/// </summary>
internal sealed class TestEntity : Entity
{
    public TestEntity()
    {
    }

    public TestEntity(Guid id)
        : base(id)
    {
    }
}
