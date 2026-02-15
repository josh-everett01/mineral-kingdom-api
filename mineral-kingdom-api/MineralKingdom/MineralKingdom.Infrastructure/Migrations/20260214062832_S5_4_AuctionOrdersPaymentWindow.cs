using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S5_4_AuctionOrdersPaymentWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AuctionId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaymentDueAt",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "STORE");

            migrationBuilder.AlterColumn<Guid>(
                name: "OfferId",
                table: "order_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "UX_orders_AuctionId",
                table: "orders",
                column: "AuctionId",
                unique: true,
                filter: "\"AuctionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_orders_AuctionId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "AuctionId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentDueAt",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "OfferId",
                table: "order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
