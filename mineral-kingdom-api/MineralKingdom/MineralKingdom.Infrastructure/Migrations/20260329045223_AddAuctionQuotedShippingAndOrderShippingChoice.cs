using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionQuotedShippingAndOrderShippingChoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShippingAmountCents",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMode",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "UNSELECTED");

            migrationBuilder.AddColumn<int>(
                name: "QuotedShippingCents",
                table: "auctions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingAmountCents",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingMode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "QuotedShippingCents",
                table: "auctions");
        }
    }
}
