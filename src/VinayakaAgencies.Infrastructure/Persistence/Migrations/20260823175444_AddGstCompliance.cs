using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGstCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReverseCharge",
                table: "TradeDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PartyGstin",
                table: "TradeDocuments",
                type: "TEXT",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceOfSupplyStateCode",
                table: "TradeDocuments",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CessMinor",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CgstMinor",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "GstRatePercentMinor",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "IgstMinor",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SgstMinor",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TaxableValueMinor",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "HsnSacId",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GstSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoundOffLedgerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GstSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GstSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HsnSac",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HsnSac", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HsnSac_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GstRateSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HsnSacId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RatePercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CompensationCessPercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GstRateSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GstRateSchedules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GstRateSchedules_HsnSac_HsnSacId",
                        column: x => x.HsnSacId,
                        principalTable: "HsnSac",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_HsnSacId",
                table: "Items",
                columns: new[] { "CompanyId", "HsnSacId" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_HsnSacId",
                table: "Items",
                column: "HsnSacId");

            migrationBuilder.CreateIndex(
                name: "IX_GstRateSchedules_CompanyId_EffectiveFrom",
                table: "GstRateSchedules",
                columns: new[] { "CompanyId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_GstRateSchedules_CompanyId_HsnSacId_EffectiveFrom",
                table: "GstRateSchedules",
                columns: new[] { "CompanyId", "HsnSacId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_GstRateSchedules_HsnSacId",
                table: "GstRateSchedules",
                column: "HsnSacId");

            migrationBuilder.CreateIndex(
                name: "IX_GstSettings_CompanyId",
                table: "GstSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HsnSac_CompanyId_Code",
                table: "HsnSac",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_HsnSac_CompanyId_IsActive",
                table: "HsnSac",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Items_HsnSac_HsnSacId",
                table: "Items",
                column: "HsnSacId",
                principalTable: "HsnSac",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_HsnSac_HsnSacId",
                table: "Items");

            migrationBuilder.DropTable(
                name: "GstRateSchedules");

            migrationBuilder.DropTable(
                name: "GstSettings");

            migrationBuilder.DropTable(
                name: "HsnSac");

            migrationBuilder.DropIndex(
                name: "IX_Items_CompanyId_HsnSacId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_HsnSacId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "IsReverseCharge",
                table: "TradeDocuments");

            migrationBuilder.DropColumn(
                name: "PartyGstin",
                table: "TradeDocuments");

            migrationBuilder.DropColumn(
                name: "PlaceOfSupplyStateCode",
                table: "TradeDocuments");

            migrationBuilder.DropColumn(
                name: "CessMinor",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "CgstMinor",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "GstRatePercentMinor",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "IgstMinor",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "SgstMinor",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "TaxableValueMinor",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "HsnSacId",
                table: "Items");
        }
    }
}
