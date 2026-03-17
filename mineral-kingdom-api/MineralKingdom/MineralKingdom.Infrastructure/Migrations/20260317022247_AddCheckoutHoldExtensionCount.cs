using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutHoldExtensionCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExtensionCount",
                table: "checkout_holds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutHoldId",
                table: "checkout_hold_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_checkout_hold_items_CheckoutHoldId",
                table: "checkout_hold_items",
                column: "CheckoutHoldId");

            migrationBuilder.AddForeignKey(
                name: "FK_checkout_hold_items_checkout_holds_CheckoutHoldId",
                table: "checkout_hold_items",
                column: "CheckoutHoldId",
                principalTable: "checkout_holds",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_checkout_hold_items_checkout_holds_CheckoutHoldId",
                table: "checkout_hold_items");

            migrationBuilder.DropIndex(
                name: "IX_checkout_hold_items_CheckoutHoldId",
                table: "checkout_hold_items");

            migrationBuilder.DropColumn(
                name: "ExtensionCount",
                table: "checkout_holds");

            migrationBuilder.DropColumn(
                name: "CheckoutHoldId",
                table: "checkout_hold_items");
        }
    }
}
