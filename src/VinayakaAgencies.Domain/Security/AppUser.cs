using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Security;

/// <summary>
/// An application account: who signs in, and under which <see cref="AppRole"/>.
/// </summary>
/// <remarks>
/// `phase-9-multiuser.md` §4a D2 chose application accounts over Windows authentication, and the
/// deciding reason was the audit trail rather than convenience. Non-negotiable #3 requires every
/// create/alter/delete to name a user; a workgroup shop typically shares one Windows login per
/// machine, so Windows authentication would stamp the machine's habitual user on every action —
/// a name that looks authoritative and identifies nobody.
/// <para>
/// <b>The password is stored only as a verifier.</b> <see cref="PasswordHash"/> holds the output of
/// <c>IPasswordHasher</c> and there is deliberately no path back to the password. The Phase 6 DPAPI
/// credential protector is the wrong tool here and is not used: it protects secrets this
/// application has to replay to the IRP, whereas a password must never be recoverable even by us.
/// </para>
/// </remarks>
public sealed class AppUser : Entity, IAuditStamped
{
    private AppUser()
    {
        // EF Core materialisation.
    }

    public AppUser(
        Guid companyId,
        string userName,
        string displayName,
        string passwordHash,
        Guid roleId,
        bool mustChangePassword = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("A user must be assigned a role.", nameof(roleId));
        }

        CompanyId = companyId;
        SetUserName(userName);
        SetDisplayName(displayName);
        PasswordHash = passwordHash;
        RoleId = roleId;
        MustChangePassword = mustChangePassword;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>What the operator types at the login prompt.</summary>
    public string UserName { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of <see cref="UserName"/>.
    /// </summary>
    /// <remarks>
    /// The unique index is built over this, so "Rahul" and "rahul" are the same account on SQLite
    /// as well as on SQL Server. Without it, SQLite's case-sensitive default would let a second
    /// account be created that differed only in case — two people, one apparent identity, and an
    /// audit trail that cannot tell them apart.
    /// </remarks>
    public string UserNameNormalised { get; private set; } = string.Empty;

    /// <summary>The name shown on screen and written to the audit trail.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Verifier produced by <c>IPasswordHasher</c>; never the password.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public Guid RoleId { get; private set; }

    /// <summary>
    /// Whether the account may sign in.
    /// </summary>
    /// <remarks>
    /// Deactivated rather than deleted, because the audit trail refers to this account by name for
    /// as long as the books exist and a dangling author is worse than a disabled row.
    /// </remarks>
    public bool IsActive { get; private set; } = true;

    /// <summary>Set when the owner creates or resets an account, cleared on the next change.</summary>
    public bool MustChangePassword { get; private set; }

    public DateTimeOffset? LastSignInAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    public void SetUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        UserName = userName.Trim();
        UserNameNormalised = NameNormaliser.Normalise(UserName);
    }

    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
    }

    public void AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("A user must be assigned a role.", nameof(roleId));
        }

        RoleId = roleId;
    }

    /// <summary>Stores a new verifier and clears <see cref="MustChangePassword"/>.</summary>
    public void SetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        MustChangePassword = false;
    }

    /// <summary>Stores a verifier the holder must replace at next sign-in.</summary>
    public void ResetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        MustChangePassword = true;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void RecordSignIn(DateTimeOffset atUtc) => LastSignInAtUtc = atUtc;
}
