using Microsoft.EntityFrameworkCore;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Auditing;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Infrastructure.Persistence.Auditing;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// The EF Core context for a company database, and the single point every write passes through.
/// </summary>
/// <remarks>
/// Deliberately <c>internal</c>. ADR-0001 requires that no <c>DbContext</c> type leaves this
/// assembly — callers reach the database through <see cref="IUnitOfWork"/> and
/// <see cref="IRepository{TEntity}"/> only. That is what keeps the Phase 9 SQLite → SQL Server
/// Express migration from becoming a rewrite, and it is why no <c>DbSet&lt;T&gt;</c> properties are
/// exposed here: entity access is via <see cref="DbContext.Set{TEntity}"/> from the repository.
/// <para>
/// <b>The audit trail is captured here, in the <c>SaveChangesAsync</c> override, and not in an
/// <c>IInterceptor</c>.</b> <c>specs/phase-1-accounting-core.md</c> §11.2 rejects the interceptor by
/// name: an interceptor can be left unregistered by a future edit of the composition root, and
/// nothing would fail — the application would simply stop keeping an edit log, which is the one
/// outcome MCA Rule 3(1) does not permit. <see cref="IClock"/> and <see cref="ICurrentUser"/> are
/// therefore required constructor parameters with no default and no null fallback, so a context
/// that cannot audit cannot be constructed at all.
/// </para>
/// </remarks>
internal sealed class AppDbContext : DbContext
{
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IClock clock,
        ICurrentUser currentUser)
        : base(options)
    {
        // No `?? NullClock.Instance` and no optional parameter. A defaulted dependency is the
        // optional-interceptor problem wearing a different hat (§11.2).
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Money and quantity are decimal(18,4) everywhere (CLAUDE.md). Setting it as a convention
        // rather than per-property means a Phase 1 voucher amount cannot silently land on the
        // provider default and lose paise, and SQL Server will not fall back to decimal(18,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);

        // Non-key strings default to a bounded width so nothing maps to nvarchar(max) by accident
        // in Phase 9. Configurations override this where a column genuinely needs more.
        configurationBuilder.Properties<string>().HaveMaxLength(512);
    }

    /// <summary>
    /// Stamps, audits and saves. Every write in the product ends up here.
    /// </summary>
    /// <remarks>
    /// The order is load-bearing. Stamping runs first so that the after-image records the real
    /// author and timestamp rather than a blank <c>CreatedBy</c> and a default
    /// <c>CreatedAtUtc</c> — this is exactly what went wrong when the stamp lived in an interceptor
    /// and the capture lived here, because <c>SavingChangesAsync</c> only fires from inside
    /// <c>base.SaveChangesAsync</c>, after the images have already been taken. Capture then runs
    /// against the stamped state, and the log rows are added to the same change tracker, so they
    /// commit in the same transaction as the change they describe: either both land or neither does
    /// (§11.2).
    /// </remarks>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        GuardAgainstAuditLogTampering();
        StampCreationAndModification();
        await CaptureAuditTrailAsync(cancellationToken).ConfigureAwait(false);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Not supported. Use <see cref="SaveChangesAsync(CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// §11.2 and §11.4 describe the audit capture at <c>SaveChangesAsync</c>, and the capture reads
    /// the tail of the hash chain from the database, which cannot be done without blocking on a
    /// synchronous path. Rather than leave a second, unaudited save path open for anyone inside
    /// this assembly to reach for, the synchronous overloads are closed off. CLAUDE.md's "async all
    /// the way for I/O" makes that a rule the codebase already follows.
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException(
            "Synchronous SaveChanges is not supported: it would bypass the audit-trail capture and "
            + "the audit-log tamper guard in SaveChangesAsync (spec §11.2, §11.4). "
            + "Call SaveChangesAsync instead.");

    /// <inheritdoc cref="SaveChanges(bool)"/>
    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    /// <summary>
    /// Defence 2 of §11.4 — rejects any attempt to update or delete an audit-log row.
    /// </summary>
    /// <remarks>
    /// Defence 1 is the type system: <c>AuditLogEntry</c> does not derive from <c>Entity</c>, so
    /// <c>IUnitOfWork.Repository&lt;AuditLogEntry&gt;()</c> does not compile and application code
    /// has no <c>Update</c> or <c>Remove</c> to call. That defence cannot see code inside this
    /// assembly, which holds the context directly; this one can. Defence 3 — the database triggers —
    /// catches a raw connection that never reaches EF at all.
    /// </remarks>
    private void GuardAgainstAuditLogTampering()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLogEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new AuditTrailTamperException(
                    $"An audit log row (Sequence {entry.Entity.Sequence}) was marked "
                    + $"{entry.State}. The edit log is append-only and cannot be altered or removed "
                    + "by any user, including an administrator (MCA Rule 3(1), spec §11.4).");
            }
        }
    }

    /// <summary>
    /// Records who made this change and when, on the entities that carry those stamps.
    /// </summary>
    /// <remarks>
    /// Stamped on <see cref="ICreationStamped"/>, the narrower of the two interfaces, so that
    /// append-only types such as <c>VoucherRevision</c> — which have no <c>Modified*</c> pair and
    /// therefore cannot implement <see cref="IAuditStamped"/> — still get an author. An
    /// unattributed voucher revision is an audit hole, not a cosmetic gap.
    /// </remarks>
    private void StampCreationAndModification()
    {
        var now = _clock.UtcNow.ToUniversalTime();
        var user = _currentUser.UserName;

        // An unattributable change is not an audit trail (see ICurrentUser). Fail loudly rather
        // than writing a blank author into the books.
        if (string.IsNullOrWhiteSpace(user))
        {
            throw new InvalidOperationException(
                "ICurrentUser returned a blank user name. Every persisted change must be attributable.");
        }

        foreach (var entry in ChangeTracker.Entries<ICreationStamped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy = user;
                    break;

                case EntityState.Modified:
                    if (entry.Entity is IAuditStamped modifiable)
                    {
                        modifiable.ModifiedAtUtc = now;
                        modifiable.ModifiedBy = user;
                    }

                    // Creation facts are immutable. Re-attaching a detached entity would otherwise
                    // let a stale or forged value overwrite the original author.
                    entry.Property(nameof(ICreationStamped.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(ICreationStamped.CreatedBy)).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Writes one edit-log row per changed entity, sealed into the hash chain (§11.1, §11.5).
    /// </summary>
    private async Task CaptureAuditTrailAsync(CancellationToken cancellationToken)
    {
        var entries = AuditTrailCapture.Auditable(ChangeTracker);

        if (entries.Count == 0)
        {
            return;
        }

        var rows = AuditTrailCapture.Seal(
            entries,
            occurredAtUtc: _clock.UtcNow.ToUniversalTime(),
            userName: _currentUser.UserName,
            machineName: Environment.MachineName,

            // One identifier per SaveChanges, so a voucher and its lines can be read back as the
            // single action they were rather than as a scatter of unrelated rows.
            saveBatchId: Guid.NewGuid(),
            previousHash: await ReadChainTipAsync(cancellationToken).ConfigureAwait(false));

        // AddRange preserves the order the rows were sealed in, which is the order they must be
        // inserted in for PreviousHash to line up with the row before it.
        Set<AuditLogEntry>().AddRange(rows);
    }

    /// <summary>The last sealed row's hash, or the genesis value if the log is empty.</summary>
    private async Task<string> ReadChainTipAsync(CancellationToken cancellationToken)
    {
        var tip = await Set<AuditLogEntry>()
            .AsNoTracking()
            .OrderByDescending(row => row.Sequence)
            .Select(row => row.RowHash)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrEmpty(tip) ? AuditLogEntry.GenesisHash : tip;
    }
}
