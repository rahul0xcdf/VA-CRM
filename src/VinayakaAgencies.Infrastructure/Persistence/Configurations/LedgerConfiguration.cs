using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Ledger"/> (spec §5).
/// </summary>
/// <remarks>
/// There is deliberately <b>no opening-balance column and no closing-balance column</b> (§8.3,
/// §8.5). An opening balance is a dated journal voucher; a closing balance is a <c>SUM</c> over
/// <c>JournalLine</c>. Whether a ledger is a bank ledger is likewise derived from its group's
/// position in the tree, not stored.
/// </remarks>
internal sealed class LedgerConfiguration : IEntityTypeConfiguration<Ledger>
{
    public void Configure(EntityTypeBuilder<Ledger> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Ledgers");

        builder.HasKey(ledger => ledger.Id);

        builder.Property(ledger => ledger.Id).ValueGeneratedNever();

        builder.Property(ledger => ledger.CompanyId).IsRequired();

        builder.Property(ledger => ledger.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(ledger => ledger.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(ledger => ledger.Alias).HasMaxLength(ColumnLengths.Name);

        builder.Property(ledger => ledger.AccountGroupId).IsRequired();
        builder.Property(ledger => ledger.IsSystem).IsRequired();
        builder.Property(ledger => ledger.AllowsManualPosting).IsRequired();
        builder.Property(ledger => ledger.IsActive).IsRequired();

        builder.Property(ledger => ledger.CreatedAtUtc).IsRequired();
        builder.Property(ledger => ledger.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(ledger => ledger.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(ledger => new { ledger.CompanyId, ledger.NameNormalised }).IsUnique();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(ledger => ledger.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade: deleting a group must never take its ledgers — and through them
        // their journal lines — with it (spec §4.6, §5).
        builder.HasOne<AccountGroup>()
            .WithMany()
            .HasForeignKey(ledger => ledger.AccountGroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
