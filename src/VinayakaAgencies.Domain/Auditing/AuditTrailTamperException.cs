namespace VinayakaAgencies.Domain.Auditing;

/// <summary>
/// Thrown when a save would update or delete an <see cref="AuditLogEntry"/>.
/// </summary>
/// <remarks>
/// The second of the five defences in <c>specs/phase-1-accounting-core.md</c> §11.4. The first
/// defence is the type system — <c>AuditLogEntry</c> does not derive from <c>Entity</c>, so
/// <c>IUnitOfWork.Repository&lt;AuditLogEntry&gt;()</c> does not compile and no application code can
/// reach an <c>Update</c> or <c>Remove</c> for it. That defence cannot see code inside the
/// persistence assembly itself, which holds the context directly; this exception closes that gap.
/// <para>
/// It is deliberately a distinct type rather than an <see cref="InvalidOperationException"/>: an
/// attempt to rewrite the edit log is not an ordinary programming error, and a test asserting the
/// attempt was rejected should not pass merely because some other invalid operation happened first.
/// </para>
/// </remarks>
public sealed class AuditTrailTamperException : Exception
{
    public AuditTrailTamperException()
        : base("The audit trail is append-only; audit log rows cannot be updated or deleted.")
    {
    }

    public AuditTrailTamperException(string message)
        : base(message)
    {
    }

    public AuditTrailTamperException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
