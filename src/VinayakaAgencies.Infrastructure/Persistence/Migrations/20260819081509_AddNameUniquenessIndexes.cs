using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNameUniquenessIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoucherNumberSeries_CompanyId",
                table: "VoucherNumberSeries");

            migrationBuilder.DropIndex(
                name: "IX_CostCentres_CompanyId",
                table: "CostCentres");

            migrationBuilder.DropIndex(
                name: "IX_CostCategories_CompanyId",
                table: "CostCategories");

            migrationBuilder.DropIndex(
                name: "IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised",
                table: "AccountGroups");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherNumberSeries_CompanyId_VoucherTypeId_NameNormalised",
                table: "VoucherNumberSeries",
                columns: new[] { "CompanyId", "VoucherTypeId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_CompanyId_NameNormalised",
                table: "CostCentres",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCategories_CompanyId_NameNormalised",
                table: "CostCategories",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_CompanyId_NameNormalised",
                table: "AccountGroups",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true,
                filter: "[ParentGroupId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised",
                table: "AccountGroups",
                columns: new[] { "CompanyId", "ParentGroupId", "NameNormalised" },
                unique: true,
                filter: "[ParentGroupId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoucherNumberSeries_CompanyId_VoucherTypeId_NameNormalised",
                table: "VoucherNumberSeries");

            migrationBuilder.DropIndex(
                name: "IX_CostCentres_CompanyId_NameNormalised",
                table: "CostCentres");

            migrationBuilder.DropIndex(
                name: "IX_CostCategories_CompanyId_NameNormalised",
                table: "CostCategories");

            migrationBuilder.DropIndex(
                name: "IX_AccountGroups_CompanyId_NameNormalised",
                table: "AccountGroups");

            migrationBuilder.DropIndex(
                name: "IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised",
                table: "AccountGroups");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherNumberSeries_CompanyId",
                table: "VoucherNumberSeries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_CompanyId",
                table: "CostCentres",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCategories_CompanyId",
                table: "CostCategories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised",
                table: "AccountGroups",
                columns: new[] { "CompanyId", "ParentGroupId", "NameNormalised" },
                unique: true);
        }
    }
}
