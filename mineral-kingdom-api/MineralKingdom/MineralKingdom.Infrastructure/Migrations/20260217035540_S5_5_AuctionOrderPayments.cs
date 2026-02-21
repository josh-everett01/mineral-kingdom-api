using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S5_5_AuctionOrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderPaymentId",
                table: "payment_webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderCheckoutId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AmountCents = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_payments_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_OrderPaymentId",
                table: "payment_webhook_events",
                column: "OrderPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_order_payments_OrderId",
                table: "order_payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_payments_Provider_ProviderCheckoutId",
                table: "order_payments",
                columns: new[] { "Provider", "ProviderCheckoutId" });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_webhook_events_order_payments_OrderPaymentId",
                table: "payment_webhook_events",
                column: "OrderPaymentId",
                principalTable: "order_payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_webhook_events_order_payments_OrderPaymentId",
                table: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "order_payments");

            migrationBuilder.DropIndex(
                name: "IX_payment_webhook_events_OrderPaymentId",
                table: "payment_webhook_events");

            migrationBuilder.DropColumn(
                name: "OrderPaymentId",
                table: "payment_webhook_events");
        }
    }
}
