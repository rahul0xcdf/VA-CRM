using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPurchasePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintTemplates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PartyLedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PartyReferenceNumber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PartyReferenceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ConvertedFromDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConvertedFromNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PostedVoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeDocuments_Ledgers_PartyLedgerId",
                        column: x => x.PartyLedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeDocuments_TradeDocuments_ConvertedFromDocumentId",
                        column: x => x.ConvertedFromDocumentId,
                        principalTable: "TradeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeDocuments_Vouchers_PostedVoucherId",
                        column: x => x.PostedVoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrintTemplateRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    LayoutJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintTemplateRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintTemplateRevisions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintTemplateRevisions_PrintTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "PrintTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeDocumentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TradeDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GodownId = table.Column<Guid>(type: "TEXT", nullable: true),
                    QuantityMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    RateMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountPercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SerialNumbersText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeDocumentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeDocumentLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeDocumentLines_Godowns_GodownId",
                        column: x => x.GodownId,
                        principalTable: "Godowns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeDocumentLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeDocumentLines_TradeDocuments_TradeDocumentId",
                        column: x => x.TradeDocumentId,
                        principalTable: "TradeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplateRevisions_CompanyId",
                table: "PrintTemplateRevisions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplateRevisions_TemplateId_Version",
                table: "PrintTemplateRevisions",
                columns: new[] { "TemplateId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplates_CompanyId_DocumentKind_NameNormalised",
                table: "PrintTemplates",
                columns: new[] { "CompanyId", "DocumentKind", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintTemplates_CompanyId_DocumentKind_Priority",
                table: "PrintTemplates",
                columns: new[] { "CompanyId", "DocumentKind", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocumentLines_CompanyId_TradeDocumentId",
                table: "TradeDocumentLines",
                columns: new[] { "CompanyId", "TradeDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocumentLines_GodownId",
                table: "TradeDocumentLines",
                column: "GodownId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocumentLines_ItemId",
                table: "TradeDocumentLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocumentLines_TradeDocumentId",
                table: "TradeDocumentLines",
                column: "TradeDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_CompanyId_ConvertedFromDocumentId",
                table: "TradeDocuments",
                columns: new[] { "CompanyId", "ConvertedFromDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_CompanyId_DocumentDate",
                table: "TradeDocuments",
                columns: new[] { "CompanyId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_CompanyId_Kind_Number",
                table: "TradeDocuments",
                columns: new[] { "CompanyId", "Kind", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_CompanyId_PostedVoucherId",
                table: "TradeDocuments",
                columns: new[] { "CompanyId", "PostedVoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_ConvertedFromDocumentId",
                table: "TradeDocuments",
                column: "ConvertedFromDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_PartyLedgerId",
                table: "TradeDocuments",
                column: "PartyLedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_PostedVoucherId",
                table: "TradeDocuments",
                column: "PostedVoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintTemplateRevisions");

            migrationBuilder.DropTable(
                name: "TradeDocumentLines");

            migrationBuilder.DropTable(
                name: "PrintTemplates");

            migrationBuilder.DropTable(
                name: "TradeDocuments");
        }
    }
}
