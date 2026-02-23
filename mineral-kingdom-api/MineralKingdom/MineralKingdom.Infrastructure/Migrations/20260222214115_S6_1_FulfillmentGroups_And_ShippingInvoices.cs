using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S6_1_FulfillmentGroups_And_ShippingInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FulfillmentGroupId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fulfillment_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PackedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShippingCarrier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fulfillment_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shipping_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FulfillmentGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipping_invoices_fulfillment_groups_FulfillmentGroupId",
                        column: x => x.FulfillmentGroupId,
                        principalTable: "fulfillment_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_FulfillmentGroupId",
                table: "orders",
                column: "FulfillmentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_groups_Status",
                table: "fulfillment_groups",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_groups_UpdatedAt",
                table: "fulfillment_groups",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_invoices_FulfillmentGroupId_Status",
                table: "shipping_invoices",
                columns: new[] { "FulfillmentGroupId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_orders_fulfillment_groups_FulfillmentGroupId",
                table: "orders",
                column: "FulfillmentGroupId",
                principalTable: "fulfillment_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_fulfillment_groups_FulfillmentGroupId",
                table: "orders");

            migrationBuilder.DropTable(
                name: "shipping_invoices");

            migrationBuilder.DropTable(
                name: "fulfillment_groups");

            migrationBuilder.DropIndex(
                name: "IX_orders_FulfillmentGroupId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentGroupId",
                table: "orders");
        }
    }
}
