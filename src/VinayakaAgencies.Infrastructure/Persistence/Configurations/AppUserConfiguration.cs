using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Security;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AppUser"/> (phase-9-multiuser.md §4a D2).
/// </summary>
/// <remarks>
/// There is deliberately <b>no password column</b> — only
/// <see cref="AppUser.PasswordHash"/>, which holds a one-way verifier. Nothing in the schema can
/// yield the password, including to us, which is the difference between this and the Phase 6
/// credential store that has to be reversible to call the IRP.
/// </remarks>
internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AppUsers");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.CompanyId).IsRequired();

        builder.Property(user => user.UserName).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(user => user.UserNameNormalised)
            .IsRequired()
            .HasMaxLength(ColumnLengths.Name);
        builder.Property(user => user.DisplayName).IsRequired().HasMaxLength(ColumnLengths.Name);

        builder.Property(user => user.PasswordHash)
            .IsRequired()
            .HasMaxLength(ColumnLengths.PasswordVerifier);

        builder.Property(user => user.RoleId).IsRequired();
        builder.Property(user => user.IsActive).IsRequired();
        builder.Property(user => user.MustChangePassword).IsRequired();

        builder.Property(user => user.CreatedAtUtc).IsRequired();
        builder.Property(user => user.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(user => user.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // Company-wide rather than per-company-plus-something: a sign-in prompt has only a user
        // name to go on, so two accounts sharing one name would make the prompt ambiguous however
        // the companies differed. SignInService looks the account up by this column alone.
        builder.HasIndex(user => user.UserNameNormalised).IsUnique();

        builder.HasIndex(user => new { user.CompanyId, user.RoleId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(user => user.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade: deleting a role must never delete the people who held it. The audit
        // trail refers to these accounts by name for as long as the books exist, so a dangling
        // author is worse than a role that refuses to be deleted — which UserAdminService enforces
        // with a clearer message than a foreign-key violation would give.
        builder.HasOne<AppRole>()
            .WithMany()
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
