using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VinayakaAgencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeLinePricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AppliedRateMinor",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "IsPriceOverridden",
                table: "TradeDocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PriceSource",
                table: "TradeDocumentLines",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Legacy lines were keyed by hand before the pricing engine existed, so what the
            // operator typed *was* the applied price: backfill it as manual rather than leaving
            // zero/empty evidence behind (the empty-string column default marks exactly those
            // rows — every later insert writes an explicit value).
            migrationBuilder.Sql(
                "UPDATE TradeDocumentLines SET AppliedRateMinor = RateMinor, PriceSource = 'Manual' " +
                "WHERE PriceSource = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliedRateMinor",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "IsPriceOverridden",
                table: "TradeDocumentLines");

            migrationBuilder.DropColumn(
                name: "PriceSource",
                table: "TradeDocumentLines");
        }
    }
}
