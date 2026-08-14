using MotorPumpSuite.Domain.Abstractions;

namespace MotorPumpSuite.Application.Common;

/// <summary>
/// Single-user identity: the signed-in Windows account.
/// </summary>
/// <remarks>
/// Phases 0–8 are single-user (project.md §5), so the Windows account is the best attribution
/// available. Phase 9 replaces this with the application account of the signed-in operator. The
/// audit trail is only as meaningful as this value, so it must never resolve to blank.
/// <para>
/// The two names are read through injected accessors rather than straight off
/// <see cref="Environment"/>. <c>Environment.MachineName</c> and <c>Environment.UserName</c> call
/// Win32 directly and ignore the process environment block, so with the statics hard-wired the
/// fallback branches below could not be tested on any real host — and an untested fallback in the
/// one class that names the author of every audited change is not worth having.
/// </para>
/// </remarks>
public sealed class EnvironmentCurrentUser : ICurrentUser
{
    public const string UnknownUser = "unknown";

    private readonly Func<string?> _machineNameAccessor;
    private readonly Func<string?> _userNameAccessor;

    public EnvironmentCurrentUser()
        : this(static () => Environment.MachineName, static () => Environment.UserName)
    {
    }

    /// <summary>Test seam: supplies the machine and user names directly.</summary>
    public EnvironmentCurrentUser(Func<string?> machineNameAccessor, Func<string?> userNameAccessor)
    {
        _machineNameAccessor = machineNameAccessor
            ?? throw new ArgumentNullException(nameof(machineNameAccessor));
        _userNameAccessor = userNameAccessor
            ?? throw new ArgumentNullException(nameof(userNameAccessor));
    }

    public string UserName
    {
        get
        {
            var machine = _machineNameAccessor();
            var user = _userNameAccessor();

            if (string.IsNullOrWhiteSpace(user))
            {
                return UnknownUser;
            }

            return string.IsNullOrWhiteSpace(machine) ? user : $@"{machine}\{user}";
        }
    }
}
