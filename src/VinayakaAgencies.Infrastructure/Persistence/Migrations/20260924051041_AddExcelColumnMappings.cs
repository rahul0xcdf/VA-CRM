using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExcelColumnMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExcelColumnMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SheetName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HeaderRowNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcelColumnMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExcelColumnMappings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExcelColumnMappingColumns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MappingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceHeader = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceHeaderNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetField = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcelColumnMappingColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExcelColumnMappingColumns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExcelColumnMappingColumns_ExcelColumnMappings_MappingId",
                        column: x => x.MappingId,
                        principalTable: "ExcelColumnMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExcelColumnMappingColumns_CompanyId",
                table: "ExcelColumnMappingColumns",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExcelColumnMappingColumns_MappingId_SourceHeaderNormalised",
                table: "ExcelColumnMappingColumns",
                columns: new[] { "MappingId", "SourceHeaderNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExcelColumnMappingColumns_MappingId_TargetField",
                table: "ExcelColumnMappingColumns",
                columns: new[] { "MappingId", "TargetField" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExcelColumnMappings_CompanyId_NameNormalised",
                table: "ExcelColumnMappings",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ExcelColumnMappings_CompanyId_Target",
                table: "ExcelColumnMappings",
                columns: new[] { "CompanyId", "Target" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExcelColumnMappingColumns");

            migrationBuilder.DropTable(
                name: "ExcelColumnMappings");
        }
    }
}
