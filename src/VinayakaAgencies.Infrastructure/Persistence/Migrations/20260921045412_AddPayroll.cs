using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    JoinedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LeftOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Pan = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Uan = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    EsiNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayHeads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Statute = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayHeads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayHeads_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayHeads_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalaryPayableLedgerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsProvidentFundEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEmployeeStateInsuranceEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsProfessionalTaxEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProfessionalTaxSlabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WageFromMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    WageToMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    TaxAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfessionalTaxSlabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfessionalTaxSlabs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryVouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PostedVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedVoucherNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryVouchers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVouchers_Vouchers_PostedVoucherId",
                        column: x => x.PostedVoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryRateSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Statute = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EmployeeRatePercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    EmployerRatePercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    WageCeilingMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    EligibilityCeilingMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryRateSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryRateSchedules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryStructureLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayHeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    PercentMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryStructureLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryStructureLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureLines_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryStructureLines_PayHeads_PayHeadId",
                        column: x => x.PayHeadId,
                        principalTable: "PayHeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryVoucherLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalaryVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayHeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayHeadName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Statute = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryVoucherLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryVoucherLines_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVoucherLines_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVoucherLines_PayHeads_PayHeadId",
                        column: x => x.PayHeadId,
                        principalTable: "PayHeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryVoucherLines_SalaryVouchers_SalaryVoucherId",
                        column: x => x.SalaryVoucherId,
                        principalTable: "SalaryVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_Code",
                table: "Employees",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_NameNormalised",
                table: "Employees",
                columns: new[] { "CompanyId", "NameNormalised" });

            migrationBuilder.CreateIndex(
                name: "IX_PayHeads_CompanyId_NameNormalised",
                table: "PayHeads",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PayHeads_CompanyId_Statute",
                table: "PayHeads",
                columns: new[] { "CompanyId", "Statute" });

            migrationBuilder.CreateIndex(
                name: "IX_PayHeads_LedgerId",
                table: "PayHeads",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettings_CompanyId",
                table: "PayrollSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfessionalTaxSlabs_CompanyId_EffectiveFrom_WageFromMinor",
                table: "ProfessionalTaxSlabs",
                columns: new[] { "CompanyId", "EffectiveFrom", "WageFromMinor" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureLines_CompanyId_EmployeeId_EffectiveFrom",
                table: "SalaryStructureLines",
                columns: new[] { "CompanyId", "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureLines_EmployeeId",
                table: "SalaryStructureLines",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructureLines_PayHeadId",
                table: "SalaryStructureLines",
                column: "PayHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVoucherLines_CompanyId_Statute",
                table: "SalaryVoucherLines",
                columns: new[] { "CompanyId", "Statute" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVoucherLines_EmployeeId",
                table: "SalaryVoucherLines",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVoucherLines_LedgerId",
                table: "SalaryVoucherLines",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVoucherLines_PayHeadId",
                table: "SalaryVoucherLines",
                column: "PayHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVoucherLines_SalaryVoucherId_EmployeeId_SortOrder",
                table: "SalaryVoucherLines",
                columns: new[] { "SalaryVoucherId", "EmployeeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVouchers_CompanyId_PeriodEnd",
                table: "SalaryVouchers",
                columns: new[] { "CompanyId", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryVouchers_PostedVoucherId",
                table: "SalaryVouchers",
                column: "PostedVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryRateSchedules_CompanyId_Statute_EffectiveFrom",
                table: "StatutoryRateSchedules",
                columns: new[] { "CompanyId", "Statute", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollSettings");

            migrationBuilder.DropTable(
                name: "ProfessionalTaxSlabs");

            migrationBuilder.DropTable(
                name: "SalaryStructureLines");

            migrationBuilder.DropTable(
                name: "SalaryVoucherLines");

            migrationBuilder.DropTable(
                name: "StatutoryRateSchedules");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "PayHeads");

            migrationBuilder.DropTable(
                name: "SalaryVouchers");
        }
    }
}
