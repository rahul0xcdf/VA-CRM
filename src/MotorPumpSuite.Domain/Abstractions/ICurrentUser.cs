namespace MotorPumpSuite.Domain.Abstractions;

/// <summary>
/// Identity of the user performing the current operation.
/// </summary>
/// <remarks>
/// Through Phase 8 this is the single Windows user of the install. Phase 9 introduces real
/// application accounts and this becomes the signed-in user. Every audit-trail row carries this
/// value, so it must never be blank — an unattributable change is not an audit trail.
/// </remarks>
public interface ICurrentUser
{
    string UserName { get; }
}
