using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S5_1_AuctionsModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_auctions_ListingId_Status",
                table: "auctions");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "auctions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "DRAFT",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<int>(
                name: "BidCount",
                table: "auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CloseTime",
                table: "auctions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosingWindowEnd",
                table: "auctions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentLeaderMaxCents",
                table: "auctions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentLeaderUserId",
                table: "auctions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentPriceCents",
                table: "auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RelistOfAuctionId",
                table: "auctions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReserveMet",
                table: "auctions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReservePriceCents",
                table: "auctions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartTime",
                table: "auctions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartingPriceCents",
                table: "auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "auction_bid_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SubmittedAmountCents = table.Column<int>(type: "integer", nullable: true),
                    Accepted = table.Column<bool>(type: "boolean", nullable: true),
                    ResultingCurrentPriceCents = table.Column<int>(type: "integer", nullable: true),
                    ResultingLeaderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DataJson = table.Column<string>(type: "text", nullable: true),
                    ServerReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auction_bid_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auction_bid_events_auctions_AuctionId",
                        column: x => x.AuctionId,
                        principalTable: "auctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auction_max_bids",
                columns: table => new
                {
                    AuctionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxBidCents = table.Column<int>(type: "integer", nullable: false),
                    BidType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auction_max_bids", x => new { x.AuctionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_auction_max_bids_auctions_AuctionId",
                        column: x => x.AuctionId,
                        principalTable: "auctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auctions_RelistOfAuctionId",
                table: "auctions",
                column: "RelistOfAuctionId");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_Status_CloseTime",
                table: "auctions",
                columns: new[] { "Status", "CloseTime" });

            migrationBuilder.CreateIndex(
                name: "IX_auction_bid_events_Auction_Time",
                table: "auction_bid_events",
                columns: new[] { "AuctionId", "ServerReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_auction_max_bids_Auction_Max_Received",
                table: "auction_max_bids",
                columns: new[] { "AuctionId", "MaxBidCents", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auction_bid_events");

            migrationBuilder.DropTable(
                name: "auction_max_bids");

            migrationBuilder.DropIndex(
                name: "IX_auctions_RelistOfAuctionId",
                table: "auctions");

            migrationBuilder.DropIndex(
                name: "IX_auctions_Status_CloseTime",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "BidCount",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "CloseTime",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "ClosingWindowEnd",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "CurrentLeaderMaxCents",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "CurrentLeaderUserId",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "CurrentPriceCents",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "RelistOfAuctionId",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "ReserveMet",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "ReservePriceCents",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "StartingPriceCents",
                table: "auctions");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "auctions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "DRAFT");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_ListingId_Status",
                table: "auctions",
                columns: new[] { "ListingId", "Status" });
        }
    }
}
