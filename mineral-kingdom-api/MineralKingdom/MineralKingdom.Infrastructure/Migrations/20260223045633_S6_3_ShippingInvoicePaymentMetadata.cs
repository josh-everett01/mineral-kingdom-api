using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S6_3_ShippingInvoicePaymentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOverride",
                table: "shipping_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "shipping_invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "shipping_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "shipping_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCheckoutId",
                table: "shipping_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderPaymentId",
                table: "shipping_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipping_invoices_Provider_ProviderCheckoutId",
                table: "shipping_invoices",
                columns: new[] { "Provider", "ProviderCheckoutId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shipping_invoices_Provider_ProviderCheckoutId",
                table: "shipping_invoices");

            migrationBuilder.DropColumn(
                name: "IsOverride",
                table: "shipping_invoices");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "shipping_invoices");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "shipping_invoices");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "shipping_invoices");

            migrationBuilder.DropColumn(
                name: "ProviderCheckoutId",
                table: "shipping_invoices");

            migrationBuilder.DropColumn(
                name: "ProviderPaymentId",
                table: "shipping_invoices");
        }
    }
}
