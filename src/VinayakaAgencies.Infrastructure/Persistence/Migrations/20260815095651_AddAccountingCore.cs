using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaterialisedPath = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Nature = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AffectsGrossProfit = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountGroups_AccountGroups_ParentGroupId",
                        column: x => x.ParentGroupId,
                        principalTable: "AccountGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountGroups_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CostCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostCategories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialYears_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PeriodLocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LockedThroughDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodLocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodLocks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultSeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherTypes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ledgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AccountGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowsManualPosting = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ledgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ledgers_AccountGroups_AccountGroupId",
                        column: x => x.AccountGroupId,
                        principalTable: "AccountGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ledgers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CostCentres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CostCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentCostCentreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaterialisedPath = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCentres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostCentres_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostCentres_CostCategories_CostCategoryId",
                        column: x => x.CostCategoryId,
                        principalTable: "CostCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostCentres_CostCentres_ParentCostCentreId",
                        column: x => x.ParentCostCentreId,
                        principalTable: "CostCentres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherNumberSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameNormalised = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Suffix = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    StartNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    ResetPolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AllowManualOverride = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherNumberSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherNumberSeries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherNumberSeries_VoucherTypes_VoucherTypeId",
                        column: x => x.VoucherTypeId,
                        principalTable: "VoucherTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherNumberSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinancialYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastAllocatedNumber = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherNumberSequences_FinancialYears_FinancialYearId",
                        column: x => x.FinancialYearId,
                        principalTable: "FinancialYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherNumberSequences_VoucherNumberSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "VoucherNumberSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinancialYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vouchers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_FinancialYears_FinancialYearId",
                        column: x => x.FinancialYearId,
                        principalTable: "FinancialYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_VoucherNumberSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "VoucherNumberSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_VoucherTypes_VoucherTypeId",
                        column: x => x.VoucherTypeId,
                        principalTable: "VoucherTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinancialYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VoucherDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NumberNormalised = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsManualNumber = table.Column<bool>(type: "INTEGER", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReferenceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ChangeReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SupersededByRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherRevisions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherRevisions_FinancialYears_FinancialYearId",
                        column: x => x.FinancialYearId,
                        principalTable: "FinancialYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherRevisions_VoucherNumberSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "VoucherNumberSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherRevisions_VoucherTypes_VoucherTypeId",
                        column: x => x.VoucherTypeId,
                        principalTable: "VoucherTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherRevisions_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LedgerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CostCentreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_CostCentres_CostCentreId",
                        column: x => x.CostCentreId,
                        principalTable: "CostCentres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_VoucherRevisions_VoucherRevisionId",
                        column: x => x.VoucherRevisionId,
                        principalTable: "VoucherRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationMarks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationMarks_JournalLines_JournalLineId",
                        column: x => x.JournalLineId,
                        principalTable: "JournalLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_CompanyId_MaterialisedPath",
                table: "AccountGroups",
                columns: new[] { "CompanyId", "MaterialisedPath" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised",
                table: "AccountGroups",
                columns: new[] { "CompanyId", "ParentGroupId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_ParentGroupId",
                table: "AccountGroups",
                column: "ParentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationMarks_CompanyId",
                table: "BankReconciliationMarks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationMarks_JournalLineId",
                table: "BankReconciliationMarks",
                column: "JournalLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCategories_CompanyId",
                table: "CostCategories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_CompanyId",
                table: "CostCentres",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_CostCategoryId",
                table: "CostCentres",
                column: "CostCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_ParentCostCentreId",
                table: "CostCentres",
                column: "ParentCostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialYears_CompanyId_StartDate",
                table: "FinancialYears",
                columns: new[] { "CompanyId", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_CompanyId_LedgerId",
                table: "JournalLines",
                columns: new[] { "CompanyId", "LedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_CostCentreId",
                table: "JournalLines",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_LedgerId",
                table: "JournalLines",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_VoucherRevisionId_LineNumber",
                table: "JournalLines",
                columns: new[] { "VoucherRevisionId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_AccountGroupId",
                table: "Ledgers",
                column: "AccountGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_CompanyId_NameNormalised",
                table: "Ledgers",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodLocks_CompanyId",
                table: "PeriodLocks",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherNumberSequences_FinancialYearId",
                table: "VoucherNumberSequences",
                column: "FinancialYearId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherNumberSequences_SeriesId_FinancialYearId",
                table: "VoucherNumberSequences",
                columns: new[] { "SeriesId", "FinancialYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherNumberSeries_CompanyId",
                table: "VoucherNumberSeries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherNumberSeries_VoucherTypeId",
                table: "VoucherNumberSeries",
                column: "VoucherTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRevisions_CompanyId_VoucherTypeId_SeriesId_FinancialYearId_NumberNormalised",
                table: "VoucherRevisions",
                columns: new[] { "CompanyId", "VoucherTypeId", "SeriesId", "FinancialYearId", "NumberNormalised" },
                unique: true,
                filter: "[SupersededByRevisionId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRevisions_FinancialYearId",
                table: "VoucherRevisions",
                column: "FinancialYearId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRevisions_SeriesId",
                table: "VoucherRevisions",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRevisions_VoucherId_RevisionNumber",
                table: "VoucherRevisions",
                columns: new[] { "VoucherId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRevisions_VoucherTypeId",
                table: "VoucherRevisions",
                column: "VoucherTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CompanyId",
                table: "Vouchers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_FinancialYearId",
                table: "Vouchers",
                column: "FinancialYearId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_SeriesId",
                table: "Vouchers",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_VoucherTypeId",
                table: "Vouchers",
                column: "VoucherTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherTypes_CompanyId_NameNormalised",
                table: "VoucherTypes",
                columns: new[] { "CompanyId", "NameNormalised" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankReconciliationMarks");

            migrationBuilder.DropTable(
                name: "PeriodLocks");

            migrationBuilder.DropTable(
                name: "VoucherNumberSequences");

            migrationBuilder.DropTable(
                name: "JournalLines");

            migrationBuilder.DropTable(
                name: "CostCentres");

            migrationBuilder.DropTable(
                name: "Ledgers");

            migrationBuilder.DropTable(
                name: "VoucherRevisions");

            migrationBuilder.DropTable(
                name: "CostCategories");

            migrationBuilder.DropTable(
                name: "AccountGroups");

            migrationBuilder.DropTable(
                name: "Vouchers");

            migrationBuilder.DropTable(
                name: "FinancialYears");

            migrationBuilder.DropTable(
                name: "VoucherNumberSeries");

            migrationBuilder.DropTable(
                name: "VoucherTypes");
        }
    }
}
