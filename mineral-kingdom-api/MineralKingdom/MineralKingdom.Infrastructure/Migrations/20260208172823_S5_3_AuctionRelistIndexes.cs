using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S5_3_AuctionRelistIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_auctions_RelistOfAuctionId",
                table: "auctions");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_Status_UpdatedAt",
                table: "auctions",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_auctions_RelistOfAuctionId",
                table: "auctions",
                column: "RelistOfAuctionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_auctions_Status_UpdatedAt",
                table: "auctions");

            migrationBuilder.DropIndex(
                name: "UX_auctions_RelistOfAuctionId",
                table: "auctions");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_RelistOfAuctionId",
                table: "auctions",
                column: "RelistOfAuctionId");
        }
    }
}
