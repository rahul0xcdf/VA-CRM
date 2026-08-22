using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Godowns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Godowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Godowns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventorySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefaultValuationMethod = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventorySettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockGroups",
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
                    table.PrimaryKey("PK_StockGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockGroups_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockVouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VoucherDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CounterLedgerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PostedVoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockVouchers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockVouchers_Ledgers_CounterLedgerId",
                        column: x => x.CounterLedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockVouchers_Vouchers_PostedVoucherId",
                        column: x => x.PostedVoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitsOfMeasure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CodeNormalised = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitsOfMeasure_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StockGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaseUnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlternateUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConversionFactorMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    IsSerialTracked = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBatchTracked = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValuationOverride = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ReorderLevelMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    IsKit = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_StockGroups_StockGroupId",
                        column: x => x.StockGroupId,
                        principalTable: "StockGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_UnitsOfMeasure_AlternateUnitId",
                        column: x => x.AlternateUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_UnitsOfMeasure_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemKitComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KitItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemKitComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemKitComponents_Items_ComponentItemId",
                        column: x => x.ComponentItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemKitComponents_Items_KitItemId",
                        column: x => x.KitItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockVoucherLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GodownId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    IsSerialTracked = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBatchTracked = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnitCostMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockVoucherLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockVoucherLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockVoucherLines_Godowns_GodownId",
                        column: x => x.GodownId,
                        principalTable: "Godowns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockVoucherLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockVoucherLines_StockVouchers_StockVoucherId",
                        column: x => x.StockVoucherId,
                        principalTable: "StockVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SerialNumbers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ValueNormalised = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CurrentGodownId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReceiptLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SoldOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SerialNumbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_Godowns_CurrentGodownId",
                        column: x => x.CurrentGodownId,
                        principalTable: "Godowns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_StockVoucherLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "StockVoucherLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialNumbers_StockVoucherLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "StockVoucherLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GlobalSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GodownId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityInMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    QuantityOutMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitCostMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ValueMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    StockVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockVoucherLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockLedgerEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedgerEntries_Godowns_GodownId",
                        column: x => x.GodownId,
                        principalTable: "Godowns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedgerEntries_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedgerEntries_StockVoucherLines_StockVoucherLineId",
                        column: x => x.StockVoucherLineId,
                        principalTable: "StockVoucherLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedgerEntries_StockVouchers_StockVoucherId",
                        column: x => x.StockVoucherId,
                        principalTable: "StockVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Godowns_CompanyId_NameNormalised",
                table: "Godowns",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventorySettings_CompanyId",
                table: "InventorySettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemKitComponents_ComponentItemId",
                table: "ItemKitComponents",
                column: "ComponentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemKitComponents_KitItemId_ComponentItemId",
                table: "ItemKitComponents",
                columns: new[] { "KitItemId", "ComponentItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_AlternateUnitId",
                table: "Items",
                column: "AlternateUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BaseUnitId",
                table: "Items",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_Barcode",
                table: "Items",
                columns: new[] { "CompanyId", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_BaseUnitId",
                table: "Items",
                columns: new[] { "CompanyId", "BaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_Code",
                table: "Items",
                columns: new[] { "CompanyId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_NameNormalised",
                table: "Items",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_StockGroupId",
                table: "Items",
                columns: new[] { "CompanyId", "StockGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_StockGroupId",
                table: "Items",
                column: "StockGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_CompanyId_ItemId_Status",
                table: "SerialNumbers",
                columns: new[] { "CompanyId", "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_CompanyId_ItemId_ValueNormalised",
                table: "SerialNumbers",
                columns: new[] { "CompanyId", "ItemId", "ValueNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_CurrentGodownId",
                table: "SerialNumbers",
                column: "CurrentGodownId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_ItemId",
                table: "SerialNumbers",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_ReceiptLineId",
                table: "SerialNumbers",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialNumbers_SaleLineId",
                table: "SerialNumbers",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockGroups_CompanyId_NameNormalised",
                table: "StockGroups",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_CompanyId_EntryDate",
                table: "StockLedgerEntries",
                columns: new[] { "CompanyId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_CompanyId_GodownId_ItemId",
                table: "StockLedgerEntries",
                columns: new[] { "CompanyId", "GodownId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_CompanyId_ItemId_GlobalSequence",
                table: "StockLedgerEntries",
                columns: new[] { "CompanyId", "ItemId", "GlobalSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_GodownId",
                table: "StockLedgerEntries",
                column: "GodownId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_ItemId",
                table: "StockLedgerEntries",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_StockVoucherId",
                table: "StockLedgerEntries",
                column: "StockVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_StockVoucherLineId",
                table: "StockLedgerEntries",
                column: "StockVoucherLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockVoucherLines_CompanyId_ItemId",
                table: "StockVoucherLines",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockVoucherLines_GodownId",
                table: "StockVoucherLines",
                column: "GodownId");

            migrationBuilder.CreateIndex(
                name: "IX_StockVoucherLines_ItemId",
                table: "StockVoucherLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockVoucherLines_StockVoucherId_LineNumber",
                table: "StockVoucherLines",
                columns: new[] { "StockVoucherId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StockVouchers_CompanyId_PostedVoucherId",
                table: "StockVouchers",
                columns: new[] { "CompanyId", "PostedVoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockVouchers_CompanyId_VoucherDate",
                table: "StockVouchers",
                columns: new[] { "CompanyId", "VoucherDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockVouchers_CounterLedgerId",
                table: "StockVouchers",
                column: "CounterLedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_StockVouchers_PostedVoucherId",
                table: "StockVouchers",
                column: "PostedVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_CompanyId_CodeNormalised",
                table: "UnitsOfMeasure",
                columns: new[] { "CompanyId", "CodeNormalised" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventorySettings");

            migrationBuilder.DropTable(
                name: "ItemKitComponents");

            migrationBuilder.DropTable(
                name: "SerialNumbers");

            migrationBuilder.DropTable(
                name: "StockLedgerEntries");

            migrationBuilder.DropTable(
                name: "StockVoucherLines");

            migrationBuilder.DropTable(
                name: "Godowns");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "StockVouchers");

            migrationBuilder.DropTable(
                name: "StockGroups");

            migrationBuilder.DropTable(
                name: "UnitsOfMeasure");
        }
    }
}
