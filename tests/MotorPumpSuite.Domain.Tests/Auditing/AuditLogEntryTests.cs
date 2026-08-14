using System.Reflection;
using MotorPumpSuite.Domain.Auditing;
using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Tests.Auditing;

/// <summary>
/// C1.4: the audit trail cannot be turned off. This file covers defence (a) from spec §11.4 — the
/// type-system guarantee that <see cref="AuditLogEntry"/> is unreachable through
/// <c>IUnitOfWork.Repository&lt;TEntity&gt;()</c> — plus the tamper-evidence hash chain (§11.5).
/// </summary>
public class AuditLogEntryTests
{
    private static AuditLogEntry NewEntry(
        DateTimeOffset? occurredAtUtc = null,
        string userName = "rahul",
        string? afterJson = "{\"Name\":\"Cash\"}") =>
        new(
            occurredAtUtc ?? new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero),
            userName,
            "WORKSTATION-1",
            Guid.NewGuid(),
            AuditAction.Create,
            entityType: "Ledger",
            entityId: Guid.NewGuid().ToString(),
            changedProperties: "Name",
            beforeJson: null,
            afterJson: afterJson,
            companyId: Guid.NewGuid());

    [Fact]
    public void Seal_ThenVerifyHash_ReturnsTrue()
    {
        var entry = NewEntry();

        entry.Seal(AuditLogEntry.GenesisHash);

        entry.VerifyHash().ShouldBeTrue();
    }

    [Fact]
    public void Seal_GenesisRow_StoresGenesisHashAsPreviousHashAndVerifies()
    {
        var entry = NewEntry();

        entry.Seal(AuditLogEntry.GenesisHash);

        entry.PreviousHash.ShouldBe(AuditLogEntry.GenesisHash);
        entry.RowHash.ShouldNotBeNullOrWhiteSpace();
        entry.RowHash.ShouldNotBe(AuditLogEntry.GenesisHash);
        entry.VerifyHash().ShouldBeTrue();
    }

    [Fact]
    public void Seal_BlankPreviousHash_ThrowsArgumentException()
    {
        var entry = NewEntry();

        Should.Throw<ArgumentException>(() => entry.Seal("   "));
    }

    [Fact]
    public void RowHash_TwoRowsWithIdenticalContentButDifferentPreviousHash_Differ()
    {
        // This is the property that makes it a *chain*: content alone is not enough to reproduce a
        // row's hash, so a tampered row cannot be "fixed" merely by recomputing from its own fields.
        var occurredAt = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
        var userName = "rahul";
        var afterJson = "{\"Name\":\"Cash\"}";

        var first = NewEntry(occurredAt, userName, afterJson);
        var second = NewEntry(occurredAt, userName, afterJson);

        var previousHashA = new string('a', 64);
        var previousHashB = new string('b', 64);

        first.Seal(previousHashA);
        second.Seal(previousHashB);

        first.RowHash.ShouldNotBe(second.RowHash);
    }

    [Fact]
    public void VerifyHash_FieldMutatedAfterSealing_ReturnsFalse()
    {
        // The only way to prove a hash actually detects tampering is to tamper with a sealed row and
        // watch verification fail. There is no public mutator for a sealed field — as there should
        // not be — so reflection stands in for "someone edited the SQLite file directly", which is
        // exactly the threat model spec §11.5 describes.
        var entry = NewEntry();
        entry.Seal(AuditLogEntry.GenesisHash);
        entry.VerifyHash().ShouldBeTrue();

        var afterJsonProperty = typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.AfterJson))
            ?? throw new InvalidOperationException("AfterJson property not found via reflection.");

        afterJsonProperty.SetValue(entry, "{\"Name\":\"Tampered\"}");

        entry.VerifyHash().ShouldBeFalse();
    }

    [Fact]
    public void AuditLogEntry_DoesNotDeriveFromEntity()
    {
        // C1.4(a): IRepository<TEntity> and IUnitOfWork.Repository<TEntity>() are both constrained
        // to `where TEntity : Entity`. If AuditLogEntry ever derived from Entity, Repository<AuditLogEntry>()
        // would compile and hand out Update/Remove on the audit log — the no-delete guarantee would
        // then depend on nobody calling it, rather than on the type system refusing to compile. This
        // test exists so that a future "tidy up the hierarchy" change trips it immediately.
        typeof(AuditLogEntry).IsAssignableTo(typeof(Entity)).ShouldBeFalse(
            "AuditLogEntry must not derive from Entity — doing so would make Repository<AuditLogEntry>() " +
            "compile, which would give every caller an Update/Remove path onto the audit log " +
            "(spec §11.4, checkpoint C1.4).");
    }

    [Fact]
    public void AuditLogEntry_HasNoPublicParameterlessConstructor_OtherThanEfMaterialisation()
    {
        // Belt-and-braces on the same guarantee: confirms there is exactly one constructor reachable
        // from outside the assembly, and it requires every field — nothing can be half-built and
        // patched afterwards through a public setter.
        var publicConstructors = typeof(AuditLogEntry)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        publicConstructors.Length.ShouldBe(1);
        publicConstructors[0].GetParameters().Length.ShouldBeGreaterThan(0);
    }
}
