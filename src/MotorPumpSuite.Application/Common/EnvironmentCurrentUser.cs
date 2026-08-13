using MotorPumpSuite.Domain.Abstractions;

namespace MotorPumpSuite.Application.Common;

/// <summary>
/// Single-user identity: the signed-in Windows account.
/// </summary>
/// <remarks>
/// Phases 0–8 are single-user (project.md §5), so the Windows account is the best attribution
/// available. Phase 9 replaces this with the application account of the signed-in operator. The
/// audit trail is only as meaningful as this value, so it must never resolve to blank.
/// </remarks>
public sealed class EnvironmentCurrentUser : ICurrentUser
{
    public const string UnknownUser = "unknown";

    public string UserName
    {
        get
        {
            var machine = Environment.MachineName;
            var user = Environment.UserName;

            if (string.IsNullOrWhiteSpace(user))
            {
                return UnknownUser;
            }

            return string.IsNullOrWhiteSpace(machine) ? user : $@"{machine}\{user}";
        }
    }
}
