using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentRequestStateToFulfillmentGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BoxStatus",
                table: "fulfillment_groups",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CLOSED",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "CLOSED");

            migrationBuilder.AddColumn<string>(
                name: "ShipmentRequestStatus",
                table: "fulfillment_groups",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NONE");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShipmentRequestedAt",
                table: "fulfillment_groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShipmentReviewedAt",
                table: "fulfillment_groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentReviewedByUserId",
                table: "fulfillment_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_groups_ShipmentRequestStatus",
                table: "fulfillment_groups",
                column: "ShipmentRequestStatus");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_groups_ShipmentRequestStatus_UpdatedAt",
                table: "fulfillment_groups",
                columns: new[] { "ShipmentRequestStatus", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fulfillment_groups_ShipmentRequestStatus",
                table: "fulfillment_groups");

            migrationBuilder.DropIndex(
                name: "IX_fulfillment_groups_ShipmentRequestStatus_UpdatedAt",
                table: "fulfillment_groups");

            migrationBuilder.DropColumn(
                name: "ShipmentRequestStatus",
                table: "fulfillment_groups");

            migrationBuilder.DropColumn(
                name: "ShipmentRequestedAt",
                table: "fulfillment_groups");

            migrationBuilder.DropColumn(
                name: "ShipmentReviewedAt",
                table: "fulfillment_groups");

            migrationBuilder.DropColumn(
                name: "ShipmentReviewedByUserId",
                table: "fulfillment_groups");

            migrationBuilder.AlterColumn<string>(
                name: "BoxStatus",
                table: "fulfillment_groups",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "CLOSED",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "CLOSED");
        }
    }
}
