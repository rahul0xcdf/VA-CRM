using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DefaultSalePriceMinor",
                table: "Items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCategories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AllowPriceOverride = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreditLimitPolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CategoryPriceListLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerCategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FixedPriceMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    MarginPercentMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryPriceListLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryPriceListLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CategoryPriceListLines_CustomerCategories_CustomerCategoryId",
                        column: x => x.CustomerCategoryId,
                        principalTable: "CustomerCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CategoryPriceListLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerCategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactPerson = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Gstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    PlaceOfSupplyStateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    BillingAddressLines = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ShippingAddressLines = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreditLimitMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CreditPeriodDays = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_CustomerCategories_CustomerCategoryId",
                        column: x => x.CustomerCategoryId,
                        principalTable: "CustomerCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerSpecialPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FixedPriceMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    MarginPercentMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerSpecialPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerSpecialPrices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerSpecialPrices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerSpecialPrices_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryPriceListLines_CompanyId_ItemId",
                table: "CategoryPriceListLines",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryPriceListLines_CustomerCategoryId_ItemId",
                table: "CategoryPriceListLines",
                columns: new[] { "CustomerCategoryId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryPriceListLines_ItemId",
                table: "CategoryPriceListLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCategories_CompanyId_NameNormalised",
                table: "CustomerCategories",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_CustomerCategoryId",
                table: "Customers",
                columns: new[] { "CompanyId", "CustomerCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_LedgerId",
                table: "Customers",
                columns: new[] { "CompanyId", "LedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_NameNormalised",
                table: "Customers",
                columns: new[] { "CompanyId", "NameNormalised" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCategoryId",
                table: "Customers",
                column: "CustomerCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_LedgerId",
                table: "Customers",
                column: "LedgerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSpecialPrices_CompanyId_ItemId",
                table: "CustomerSpecialPrices",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSpecialPrices_CustomerId_ItemId",
                table: "CustomerSpecialPrices",
                columns: new[] { "CustomerId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSpecialPrices_ItemId",
                table: "CustomerSpecialPrices",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesSettings_CompanyId",
                table: "SalesSettings",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryPriceListLines");

            migrationBuilder.DropTable(
                name: "CustomerSpecialPrices");

            migrationBuilder.DropTable(
                name: "SalesSettings");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "CustomerCategories");

            migrationBuilder.DropColumn(
                name: "DefaultSalePriceMinor",
                table: "Items");
        }
    }
}
