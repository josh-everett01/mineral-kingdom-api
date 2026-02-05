using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S4_2_CartsAndCheckoutHolds_2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_checkout_holds_CartId_Completed",
                table: "checkout_holds");

            migrationBuilder.CreateIndex(
                name: "IX_checkout_holds_CartId_Completed",
                table: "checkout_holds",
                column: "CartId",
                unique: true,
                filter: "\"Status\" = 'COMPLETED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_checkout_holds_CartId_Completed",
                table: "checkout_holds");

            migrationBuilder.CreateIndex(
                name: "UX_checkout_holds_CartId_Completed",
                table: "checkout_holds",
                column: "CartId",
                unique: true,
                filter: "\"CompletedAt\" IS NOT NULL");
        }
    }
}
