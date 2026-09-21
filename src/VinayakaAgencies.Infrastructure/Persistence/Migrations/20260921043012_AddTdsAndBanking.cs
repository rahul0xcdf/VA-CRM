using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTdsAndBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentInstruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankLedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InstrumentNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    InstrumentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BankReference = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PayeeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsPrinted = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrintedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentInstruments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentInstruments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentInstruments_Ledgers_BankLedgerId",
                        column: x => x.BankLedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentInstruments_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxPayeeProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayeeCategory = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Pan = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    IsDeductionApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeclaresDeductingOnPurchases = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxPayeeProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxPayeeProfiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxPayeeProfiles_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StatutePaymentCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxSections_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LowerDeductionCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxSectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CertificateNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RatePercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CeilingAmountMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LowerDeductionCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LowerDeductionCertificates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LowerDeductionCertificates_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LowerDeductionCertificates_TaxSections_TaxSectionId",
                        column: x => x.TaxSectionId,
                        principalTable: "TaxSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxDeductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxSectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinancialYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SectionCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PayeeCategory = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DeductionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BaseAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxableBaseMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DeductedAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    AppliedRatePercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CumulativeBaseAfterMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    IsCertificateApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsThresholdCrossingPayment = table.Column<bool>(type: "INTEGER", nullable: false),
                    PayeeHadPan = table.Column<bool>(type: "INTEGER", nullable: false),
                    CertificateNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxDeductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxDeductions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxDeductions_FinancialYears_FinancialYearId",
                        column: x => x.FinancialYearId,
                        principalTable: "FinancialYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxDeductions_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxDeductions_TaxSections_TaxSectionId",
                        column: x => x.TaxSectionId,
                        principalTable: "TaxSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxDeductions_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRateSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxSectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayeeCategory = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RatePercentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    RateWithoutPanPercentMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    SingleTransactionThresholdMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    AnnualThresholdMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRateSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxRateSchedules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxRateSchedules_TaxSections_TaxSectionId",
                        column: x => x.TaxSectionId,
                        principalTable: "TaxSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TdsSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TdsPayableLedgerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TcsPayableLedgerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsPurchaseOfGoodsDeductionEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PurchaseOfGoodsSectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsSaleOfGoodsCollectionEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SaleOfGoodsSectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TdsSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TdsSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TdsSettings_TaxSections_PurchaseOfGoodsSectionId",
                        column: x => x.PurchaseOfGoodsSectionId,
                        principalTable: "TaxSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TdsSettings_TaxSections_SaleOfGoodsSectionId",
                        column: x => x.SaleOfGoodsSectionId,
                        principalTable: "TaxSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LowerDeductionCertificates_CompanyId_LedgerId_TaxSectionId_ValidFrom",
                table: "LowerDeductionCertificates",
                columns: new[] { "CompanyId", "LedgerId", "TaxSectionId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_LowerDeductionCertificates_LedgerId",
                table: "LowerDeductionCertificates",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_LowerDeductionCertificates_TaxSectionId",
                table: "LowerDeductionCertificates",
                column: "TaxSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstruments_BankLedgerId",
                table: "PaymentInstruments",
                column: "BankLedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstruments_CompanyId_BankLedgerId_InstrumentDate",
                table: "PaymentInstruments",
                columns: new[] { "CompanyId", "BankLedgerId", "InstrumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstruments_CompanyId_InstrumentNumber",
                table: "PaymentInstruments",
                columns: new[] { "CompanyId", "InstrumentNumber" },
                filter: "[InstrumentNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstruments_VoucherId",
                table: "PaymentInstruments",
                column: "VoucherId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeductions_CompanyId_LedgerId_TaxSectionId_FinancialYearId_DeductionDate",
                table: "TaxDeductions",
                columns: new[] { "CompanyId", "LedgerId", "TaxSectionId", "FinancialYearId", "DeductionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeductions_CompanyId_TaxSectionId_DeductionDate",
                table: "TaxDeductions",
                columns: new[] { "CompanyId", "TaxSectionId", "DeductionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeductions_FinancialYearId",
                table: "TaxDeductions",
                column: "FinancialYearId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeductions_LedgerId",
                table: "TaxDeductions",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeductions_TaxSectionId",
                table: "TaxDeductions",
                column: "TaxSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDeductions_VoucherId",
                table: "TaxDeductions",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxPayeeProfiles_CompanyId_LedgerId",
                table: "TaxPayeeProfiles",
                columns: new[] { "CompanyId", "LedgerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxPayeeProfiles_LedgerId",
                table: "TaxPayeeProfiles",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateSchedules_CompanyId_EffectiveFrom",
                table: "TaxRateSchedules",
                columns: new[] { "CompanyId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateSchedules_CompanyId_TaxSectionId_EffectiveFrom",
                table: "TaxRateSchedules",
                columns: new[] { "CompanyId", "TaxSectionId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateSchedules_TaxSectionId",
                table: "TaxRateSchedules",
                column: "TaxSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxSections_CompanyId_IsActive",
                table: "TaxSections",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxSections_CompanyId_Kind_Code",
                table: "TaxSections",
                columns: new[] { "CompanyId", "Kind", "Code" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TdsSettings_CompanyId",
                table: "TdsSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TdsSettings_PurchaseOfGoodsSectionId",
                table: "TdsSettings",
                column: "PurchaseOfGoodsSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TdsSettings_SaleOfGoodsSectionId",
                table: "TdsSettings",
                column: "SaleOfGoodsSectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LowerDeductionCertificates");

            migrationBuilder.DropTable(
                name: "PaymentInstruments");

            migrationBuilder.DropTable(
                name: "TaxDeductions");

            migrationBuilder.DropTable(
                name: "TaxPayeeProfiles");

            migrationBuilder.DropTable(
                name: "TaxRateSchedules");

            migrationBuilder.DropTable(
                name: "TdsSettings");

            migrationBuilder.DropTable(
                name: "TaxSections");
        }
    }
}
