using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Customer"/> (phase-4-customer-pricing.md §2).
/// </summary>
/// <remarks>
/// The customer is a face over the books, not a second set of them: every balance stays derived
/// from the customer's ledger, and invoices keep posting against that ledger exactly as Phase 3
/// did. The unique index over <c>LedgerId</c> makes the one-to-one link a database fact.
/// </remarks>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Customers");

        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();

        builder.Property(customer => customer.CompanyId).IsRequired();
        builder.Property(customer => customer.LedgerId).IsRequired();
        builder.Property(customer => customer.CustomerCategoryId).IsRequired();

        builder.Property(customer => customer.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(customer => customer.NameNormalised).IsRequired().HasMaxLength(ColumnLengths.Name);

        builder.Property(customer => customer.ContactPerson).HasMaxLength(ColumnLengths.Name);
        builder.Property(customer => customer.PhoneNumber).HasMaxLength(32);
        builder.Property(customer => customer.Email).HasMaxLength(ColumnLengths.Name);

        // 15 characters, fixed by the GST registration format — same as Company.Gstin.
        builder.Property(customer => customer.Gstin).HasMaxLength(15);

        builder.Property(customer => customer.PlaceOfSupplyStateCode).HasMaxLength(2);

        builder.Property(customer => customer.BillingAddressLines).HasMaxLength(ColumnLengths.Address);

        // Structured city and pincode for the IRP buyer block (phase-6-einvoice-eway.md §4b).
        // The free-text address above cannot yield either, and the portal needs both separately.
        builder.Property(customer => customer.City).HasMaxLength(ColumnLengths.Name);
        builder.Property(customer => customer.Pincode);
        builder.Property(customer => customer.ShippingAddressLines).HasMaxLength(ColumnLengths.Address);

        builder.Property(customer => customer.CreditLimitMinor).IsRequired();
        builder.Ignore(customer => customer.CreditLimit);

        builder.Property(customer => customer.IsActive).IsRequired();

        builder.Property(customer => customer.CreatedAtUtc).IsRequired();
        builder.Property(customer => customer.CreatedBy).IsRequired().HasMaxLength(ColumnLengths.UserName);
        builder.Property(customer => customer.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        // One ledger per customer, both directions: two customers sharing an account would pool
        // their receivables into one statement, which is precisely the error this prevents.
        builder.HasIndex(customer => customer.LedgerId).IsUnique();

        // Lookup paths for entry screens; non-unique because two "Anand Traders" branches are an
        // operator warning, not a database impossibility.
        builder.HasIndex(customer => new { customer.CompanyId, customer.NameNormalised });
        builder.HasIndex(customer => new { customer.CompanyId, customer.CustomerCategoryId });

        builder.HasIndex(customer => new { customer.CompanyId, customer.LedgerId });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(customer => customer.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: deleting a customer's ledger while the customer stands would orphan their
        // entire receivable history.
        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(customer => customer.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: a category with customers cannot be deleted out from under its price list or
        // them; deactivate it instead.
        builder.HasOne<CustomerCategory>()
            .WithMany()
            .HasForeignKey(customer => customer.CustomerCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
