using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingModeToCheckoutPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShippingMode",
                table: "checkout_payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SHIP_NOW");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingMode",
                table: "checkout_payments");
        }
    }
}
