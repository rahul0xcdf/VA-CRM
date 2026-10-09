namespace VinayakaAgencies.Domain.Security;

/// <summary>
/// Thrown when the signed-in user lacks the <see cref="PermissionKind"/> an operation requires.
/// </summary>
/// <remarks>
/// An exception rather than a false return, because C9.2 and C9.5 turn on refusal being
/// unmissable: a boolean can be ignored by a caller that forgets to read it, and the resulting
/// write would look authorised. The §2 rule — "a hidden button is not a permission" — only holds
/// if the service layer stops the work rather than reporting that it should have.
/// </remarks>
public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(PermissionKind permission, string userName)
        : base($"'{userName}' does not hold the '{permission}' permission.")
    {
        DeniedPermission = permission;
        UserName = userName;
    }

    public PermissionDeniedException(string message)
        : base(message)
    {
    }

    public PermissionDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PermissionDeniedException()
        : base("The signed-in user does not hold the permission this operation requires.")
    {
    }

    public PermissionKind? DeniedPermission { get; }

    public string? UserName { get; }
}
