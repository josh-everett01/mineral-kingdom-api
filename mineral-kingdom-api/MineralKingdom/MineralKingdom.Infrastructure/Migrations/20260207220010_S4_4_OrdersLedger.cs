using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S4_4_OrdersLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutHoldId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestEmail",
                table: "orders",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestEmail",
                table: "checkout_holds",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_ledger_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_ledger_entries_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_GuestEmail",
                table: "orders",
                column: "GuestEmail");

            migrationBuilder.CreateIndex(
                name: "UX_orders_CheckoutHoldId",
                table: "orders",
                column: "CheckoutHoldId",
                unique: true,
                filter: "\"CheckoutHoldId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_orders_OrderNumber",
                table: "orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_checkout_holds_GuestEmail",
                table: "checkout_holds",
                column: "GuestEmail");

            migrationBuilder.CreateIndex(
                name: "IX_order_ledger_entries_OrderId",
                table: "order_ledger_entries",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_orders_GuestEmail",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "UX_orders_CheckoutHoldId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "UX_orders_OrderNumber",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_checkout_holds_GuestEmail",
                table: "checkout_holds");

            migrationBuilder.DropColumn(
                name: "CheckoutHoldId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "GuestEmail",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "GuestEmail",
                table: "checkout_holds");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
