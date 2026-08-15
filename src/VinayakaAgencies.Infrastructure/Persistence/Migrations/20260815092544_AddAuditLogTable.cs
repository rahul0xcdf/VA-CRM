using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the edit-log table required by specs/phase-1-accounting-core.md §11 and MCA Rule 3(1).
    /// </summary>
    /// <remarks>
    /// The table plus the two triggers that make it append-only at the database level — the third of
    /// the five defences in §11.4. The triggers are what stop a raw connection obtained from
    /// IDbConnectionFactory, or any SQLite tool pointed at the file, from quietly rewriting history.
    /// </remarks>
    public partial class AddAuditLogTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    MachineName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SaveBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AggregateType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AggregateId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ChangedProperties = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    RowHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_AggregateType_AggregateId",
                table: "AuditLogEntries",
                columns: new[] { "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_CompanyId_OccurredAtUtc",
                table: "AuditLogEntries",
                columns: new[] { "CompanyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_EntityType_EntityId",
                table: "AuditLogEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_SaveBatchId",
                table: "AuditLogEntries",
                column: "SaveBatchId");

            // Defence 3 of §11.4. Named to match the HasTrigger annotations in
            // AuditLogEntryConfiguration: EF Core 8 must know the table carries triggers, or its
            // SQL Server save path will emit INSERT … OUTPUT INSERTED.Sequence at Phase 9, which
            // SQL Server rejects on a table with enabled triggers.
            if (migrationBuilder.IsSqlite())
            {
                migrationBuilder.Sql(
                    """
                    CREATE TRIGGER TR_AuditLogEntries_BlockUpdate
                    BEFORE UPDATE ON AuditLogEntries
                    BEGIN
                        SELECT RAISE(ABORT, 'The audit trail is append-only: audit log rows cannot be updated.');
                    END;
                    """);

                migrationBuilder.Sql(
                    """
                    CREATE TRIGGER TR_AuditLogEntries_BlockDelete
                    BEFORE DELETE ON AuditLogEntries
                    BEGIN
                        SELECT RAISE(ABORT, 'The audit trail is append-only: audit log rows cannot be deleted.');
                    END;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the log is inherent to how a migration rolls back and is acceptable as a
            // developer action. Phase 10's backup/restore work must not surface migration rollback
            // as a user-facing operation — that would be an audit-trail delete path wearing a
            // maintenance label (spec §13).
            if (migrationBuilder.IsSqlite())
            {
                migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_AuditLogEntries_BlockDelete;");
                migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_AuditLogEntries_BlockUpdate;");
            }

            migrationBuilder.DropTable(
                name: "AuditLogEntries");
        }
    }
}
