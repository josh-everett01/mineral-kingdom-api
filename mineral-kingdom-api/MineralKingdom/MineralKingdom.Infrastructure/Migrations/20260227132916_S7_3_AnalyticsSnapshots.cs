using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S7_3_AnalyticsSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_auction_summary",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    AuctionsClosed = table.Column<int>(type: "integer", nullable: false),
                    AuctionsSold = table.Column<int>(type: "integer", nullable: false),
                    AuctionsUnsold = table.Column<int>(type: "integer", nullable: false),
                    AvgFinalPriceCents = table.Column<int>(type: "integer", nullable: true),
                    AvgBidsPerAuction = table.Column<double>(type: "double precision", nullable: true),
                    ReserveMetRate = table.Column<double>(type: "double precision", nullable: true),
                    PaymentCompletionRate = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_auction_summary", x => x.Date);
                });

            migrationBuilder.CreateTable(
                name: "daily_sales_summary",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    GrossSalesCents = table.Column<long>(type: "bigint", nullable: false),
                    NetSalesCents = table.Column<long>(type: "bigint", nullable: false),
                    OrderCount = table.Column<int>(type: "integer", nullable: false),
                    AovCents = table.Column<long>(type: "bigint", nullable: false),
                    StoreSalesCents = table.Column<long>(type: "bigint", nullable: false),
                    AuctionSalesCents = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_sales_summary", x => x.Date);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_auction_summary_Date",
                table: "daily_auction_summary",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_auction_summary");

            migrationBuilder.DropTable(
                name: "daily_sales_summary");
        }
    }
}
