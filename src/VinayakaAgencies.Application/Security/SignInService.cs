using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Application.Security;

/// <summary>
/// Verifies credentials, establishes the session, and changes the holder's own password.
/// </summary>
/// <remarks>
/// <b>An unknown account costs the same as a wrong password.</b> When no account matches, a hash is
/// still verified against a throwaway verifier before failing. Skipping that work would make an
/// unknown user name measurably faster to reject than a known one, which at a PBKDF2 cost of
/// hundreds of thousands of iterations is not a subtle difference — it is microseconds against
/// hundreds of milliseconds, and it would let anyone enumerate who has an account here.
/// </remarks>
public sealed class SignInService : ISignInService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserSession _session;
    private readonly IClock _clock;

    /// <summary>
    /// Verified against when no account matches, purely to spend the same time as a real check.
    /// </summary>
    /// <remarks>
    /// Generated once per process from a password nobody holds, rather than being a constant in the
    /// source: a fixed verifier in a public repository is a known-answer target, and there is no
    /// reason to publish one when producing it costs a single hash.
    /// </remarks>
    private readonly Lazy<string> _decoyVerifier;

    public SignInService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IUserSession session,
        IClock clock)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        _decoyVerifier = new Lazy<string>(() => _passwordHasher.Hash(Guid.NewGuid().ToString("N")));
    }

    public Task<bool> AnyAccountsExistAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.Repository<AppUser>().AnyAsync(cancellationToken: cancellationToken);

    public async Task<SignInOutcome> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return SignInOutcome.Failed(
                SignInFailure.InvalidCredentials,
                "Enter a user name and password.");
        }

        var normalised = NameNormaliser.Normalise(userName);
        var user = await _unitOfWork.Repository<AppUser>()
            .SingleOrDefaultAsync(new UserByName(normalised), cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // Deliberate: spend a verification so an unknown name is not faster to reject.
            _passwordHasher.Verify(password, _decoyVerifier.Value);

            return SignInOutcome.Failed(
                SignInFailure.InvalidCredentials,
                "That user name and password do not match an account.");
        }

        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            return SignInOutcome.Failed(
                SignInFailure.InvalidCredentials,
                "That user name and password do not match an account.");
        }

        // Checked after the password, so a wrong password against a disabled account still reports
        // the generic failure rather than confirming that the account exists.
        if (!user.IsActive)
        {
            return SignInOutcome.Failed(
                SignInFailure.AccountInactive,
                "That account has been deactivated. Ask the owner to re-enable it.");
        }

        user.RecordSignIn(_clock.UtcNow);
        _unitOfWork.Repository<AppUser>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var signedIn = new SignedInUser(
            user.Id,
            user.CompanyId,
            user.UserName,
            user.DisplayName,
            user.RoleId,
            user.MustChangePassword);

        _session.SignIn(signedIn);

        return SignInOutcome.Success(signedIn);
    }

    public void SignOut() => _session.SignOut();

    public async Task<ChangePasswordOutcome> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var current = _session.Current;

        if (current is null)
        {
            return ChangePasswordOutcome.Failed("Nobody is signed in.");
        }

        var user = await _unitOfWork.Repository<AppUser>()
            .GetByIdAsync(current.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return ChangePasswordOutcome.Failed("The signed-in account no longer exists.");
        }

        var errors = new List<string>();

        if (!_passwordHasher.Verify(currentPassword ?? string.Empty, user.PasswordHash))
        {
            errors.Add("The current password is not correct.");
        }

        errors.AddRange(PasswordPolicy.Validate(newPassword));

        if (errors.Count > 0)
        {
            return ChangePasswordOutcome.Failed([.. errors]);
        }

        user.SetPassword(_passwordHasher.Hash(newPassword));
        _unitOfWork.Repository<AppUser>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The session was carrying MustChangePassword and it is now satisfied, so re-establish it
        // rather than leaving the shell believing a change is still outstanding.
        _session.SignIn(current with { MustChangePassword = false });

        return ChangePasswordOutcome.Success();
    }

    public async Task<SignInOutcome> CreateFirstAccountAsync(
        Guid companyId,
        string userName,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (await AnyAccountsExistAsync(cancellationToken).ConfigureAwait(false))
        {
            return SignInOutcome.Failed(
                SignInFailure.InvalidCredentials,
                "This install already has accounts. Ask the owner to create one for you.");
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(displayName))
        {
            return SignInOutcome.Failed(
                SignInFailure.InvalidCredentials,
                "Enter a user name and a display name.");
        }

        var passwordErrors = PasswordPolicy.Validate(password);

        if (passwordErrors.Count > 0)
        {
            return SignInOutcome.Failed(
                SignInFailure.InvalidCredentials,
                string.Join(" ", passwordErrors));
        }

        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var ownerRole = await SecuritySeed
            .EnsureRolesAsync(_unitOfWork, companyId, cancellationToken)
            .ConfigureAwait(false);

        var user = new AppUser(
            companyId,
            userName,
            displayName,
            _passwordHasher.Hash(password),
            ownerRole.Id);

        await _unitOfWork.Repository<AppUser>()
            .AddAsync(user, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var signedIn = new SignedInUser(
            user.Id,
            user.CompanyId,
            user.UserName,
            user.DisplayName,
            user.RoleId,
            MustChangePassword: false);

        _session.SignIn(signedIn);

        return SignInOutcome.Success(signedIn);
    }

    private sealed class UserByName : Specification<AppUser>
    {
        public UserByName(string userNameNormalised)
        {
            Where(user => user.UserNameNormalised == userNameNormalised);
        }
    }
}
