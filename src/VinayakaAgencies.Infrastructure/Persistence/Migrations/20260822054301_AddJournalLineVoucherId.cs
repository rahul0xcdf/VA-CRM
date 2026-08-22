using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalLineVoucherId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VoucherId",
                table: "JournalLines",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_VoucherId",
                table: "JournalLines",
                column: "VoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalLines_Vouchers_VoucherId",
                table: "JournalLines",
                column: "VoucherId",
                principalTable: "Vouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalLines_Vouchers_VoucherId",
                table: "JournalLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalLines_VoucherId",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "VoucherId",
                table: "JournalLines");
        }
    }
}
