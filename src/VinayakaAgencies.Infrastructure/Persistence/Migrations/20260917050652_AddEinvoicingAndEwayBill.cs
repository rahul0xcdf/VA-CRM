using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEinvoicingAndEwayBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Customers",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Pincode",
                table: "Customers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Companies",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Companies",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Companies",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Pincode",
                table: "Companies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EinvoiceRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TradeDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierGstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Irn = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AcknowledgementNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PortalAckDateText = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SignedInvoiceJws = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SignedQrCodeJws = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancellationReasonCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    CancellationRemarks = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastErrorText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EinvoiceRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EinvoiceRegistrations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EinvoiceRegistrations_TradeDocuments_TradeDocumentId",
                        column: x => x.TradeDocumentId,
                        principalTable: "TradeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EinvoiceSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEinvoicingApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EwayBillThresholdMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ProtectedClientId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ProtectedClientSecret = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ProtectedUsername = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ProtectedPassword = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EinvoiceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EinvoiceSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EwayBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TradeDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Irn = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConsignmentValueMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DistanceKilometres = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EwayBillNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TransportMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    VehicleType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TransporterGstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    TransporterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TransportDocumentNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TransportDocumentDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    VehicleNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancellationReasonCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    CancellationRemarks = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastErrorText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EwayBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EwayBills_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EwayBills_TradeDocuments_TradeDocumentId",
                        column: x => x.TradeDocumentId,
                        principalTable: "TradeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EinvoiceApiCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EinvoiceRegistrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    RedactedRequestBody = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    RedactedResponseBody = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    PortalErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TimedOut = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EinvoiceApiCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EinvoiceApiCalls_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EinvoiceApiCalls_EinvoiceRegistrations_EinvoiceRegistrationId",
                        column: x => x.EinvoiceRegistrationId,
                        principalTable: "EinvoiceRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EinvoiceApiCalls_CompanyId_EinvoiceRegistrationId_StartedAtUtc",
                table: "EinvoiceApiCalls",
                columns: new[] { "CompanyId", "EinvoiceRegistrationId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EinvoiceApiCalls_EinvoiceRegistrationId",
                table: "EinvoiceApiCalls",
                column: "EinvoiceRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_EinvoiceRegistrations_CompanyId_DocumentDate",
                table: "EinvoiceRegistrations",
                columns: new[] { "CompanyId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EinvoiceRegistrations_CompanyId_Irn",
                table: "EinvoiceRegistrations",
                columns: new[] { "CompanyId", "Irn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EinvoiceRegistrations_CompanyId_TradeDocumentId",
                table: "EinvoiceRegistrations",
                columns: new[] { "CompanyId", "TradeDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EinvoiceRegistrations_TradeDocumentId",
                table: "EinvoiceRegistrations",
                column: "TradeDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EinvoiceSettings_CompanyId",
                table: "EinvoiceSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EwayBills_CompanyId_EwayBillNumber",
                table: "EwayBills",
                columns: new[] { "CompanyId", "EwayBillNumber" },
                unique: true,
                filter: "\"EwayBillNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EwayBills_CompanyId_TradeDocumentId",
                table: "EwayBills",
                columns: new[] { "CompanyId", "TradeDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EwayBills_TradeDocumentId",
                table: "EwayBills",
                column: "TradeDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EinvoiceApiCalls");

            migrationBuilder.DropTable(
                name: "EinvoiceSettings");

            migrationBuilder.DropTable(
                name: "EwayBills");

            migrationBuilder.DropTable(
                name: "EinvoiceRegistrations");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Pincode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Pincode",
                table: "Companies");
        }
    }
}
