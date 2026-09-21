using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Employee"/> (phase-7-tds-payroll-banking.md §4a D3).
/// </summary>
/// <remarks>
/// The employee code is uniquely indexed among active rows only, matching every other master in
/// the product: a row created in error can be deactivated and the code reused, while two live
/// employees sharing a code stays a database impossibility.
/// <para>
/// <c>LeftOn</c> carries no filter and no uniqueness. A leaver is still an employee of record —
/// their final month still computes, their PF is still remitted, and their past pay slips must stay
/// reproducible — so leaving is a date, never a soft delete.
/// </para>
/// </remarks>
internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Employees");

        builder.HasKey(employee => employee.Id);
        builder.Property(employee => employee.Id).ValueGeneratedNever();

        builder.Property(employee => employee.CompanyId).IsRequired();
        builder.Property(employee => employee.Code).IsRequired().HasMaxLength(ColumnLengths.ItemCode);
        builder.Property(employee => employee.Name).IsRequired().HasMaxLength(ColumnLengths.Name);
        builder.Property(employee => employee.NameNormalised)
            .IsRequired()
            .HasMaxLength(ColumnLengths.Name);
        builder.Property(employee => employee.Designation).HasMaxLength(ColumnLengths.Name);

        builder.Property(employee => employee.JoinedOn).IsRequired();
        builder.Property(employee => employee.LeftOn);

        builder.Property(employee => employee.Pan)
            .HasMaxLength(VinayakaAgencies.Domain.Tds.TaxPayeeProfile.PanLength);
        builder.Property(employee => employee.Uan).HasMaxLength(Employee.UanLength);
        builder.Property(employee => employee.EsiNumber).HasMaxLength(ColumnLengths.ItemCode);
        builder.Property(employee => employee.IsActive).IsRequired();

        builder.Property(employee => employee.CreatedAtUtc).IsRequired();
        builder.Property(employee => employee.CreatedBy)
            .IsRequired()
            .HasMaxLength(ColumnLengths.UserName);
        builder.Property(employee => employee.ModifiedBy).HasMaxLength(ColumnLengths.UserName);

        builder.HasIndex(employee => new { employee.CompanyId, employee.Code })
            .IsUnique()
            .HasFilter(IndexFilters.IsTrue(nameof(Employee.IsActive)));

        builder.HasIndex(employee => new { employee.CompanyId, employee.NameNormalised });

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(employee => employee.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


