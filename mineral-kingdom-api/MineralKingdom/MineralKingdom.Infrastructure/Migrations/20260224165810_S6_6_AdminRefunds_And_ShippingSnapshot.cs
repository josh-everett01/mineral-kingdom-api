using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S6_6_AdminRefunds_And_ShippingSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CalculatedAmountCents",
                table: "shipping_invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.Sql(@"
UPDATE shipping_invoices
SET ""CalculatedAmountCents"" = ""AmountCents""
WHERE ""CalculatedAmountCents"" = 0 AND ""AmountCents"" <> 0;
");
            migrationBuilder.CreateTable(
                name: "order_refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_refunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_refunds_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_refunds_OrderId_CreatedAt",
                table: "order_refunds",
                columns: new[] { "OrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_refunds_Provider_ProviderRefundId",
                table: "order_refunds",
                columns: new[] { "Provider", "ProviderRefundId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_refunds");

            migrationBuilder.DropColumn(
                name: "CalculatedAmountCents",
                table: "shipping_invoices");
        }
    }
}
