using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S4_3_PaymentsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checkout_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_checkout_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_checkout_payments_carts_CartId",
                        column: x => x.CartId,
                        principalTable: "carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_checkout_payments_checkout_holds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "checkout_holds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EventId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CheckoutPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_webhook_events_checkout_payments_CheckoutPaymentId",
                        column: x => x.CheckoutPaymentId,
                        principalTable: "checkout_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_checkout_payments_CartId",
                table: "checkout_payments",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_checkout_payments_HoldId",
                table: "checkout_payments",
                column: "HoldId");

            migrationBuilder.CreateIndex(
                name: "IX_checkout_payments_Provider_ProviderCheckoutId",
                table: "checkout_payments",
                columns: new[] { "Provider", "ProviderCheckoutId" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_CheckoutPaymentId",
                table: "payment_webhook_events",
                column: "CheckoutPaymentId");

            migrationBuilder.CreateIndex(
                name: "UX_payment_webhook_events_provider_event",
                table: "payment_webhook_events",
                columns: new[] { "Provider", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "checkout_payments");
        }
    }
}
