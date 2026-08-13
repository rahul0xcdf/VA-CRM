namespace MotorPumpSuite.Domain.Common;

/// <summary>
/// Base type for all persisted entities.
/// </summary>
/// <remarks>
/// The identifier is a GUID generated on the client at construction time, never by the database.
/// Phase 8's Tally XML export relies on Tally's "overwrite if GUID exists" behaviour, which only
/// works if a record's GUID is stable across an export / re-import cycle. See ADR-0001.
/// </remarks>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity()
        : this(Guid.NewGuid())
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity id must not be an empty GUID.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; private set; }

    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Different entity types with the same id are still different entities.
        return GetType() == other.GetType() && Id == other.Id;
    }

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
