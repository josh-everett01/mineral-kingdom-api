using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S6_2_OpenBox_BoxStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoxStatus",
                table: "fulfillment_groups",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "CLOSED");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "fulfillment_groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_groups_BoxStatus",
                table: "fulfillment_groups",
                column: "BoxStatus");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_groups_BoxStatus_UpdatedAt",
                table: "fulfillment_groups",
                columns: new[] { "BoxStatus", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fulfillment_groups_BoxStatus",
                table: "fulfillment_groups");

            migrationBuilder.DropIndex(
                name: "IX_fulfillment_groups_BoxStatus_UpdatedAt",
                table: "fulfillment_groups");

            migrationBuilder.DropColumn(
                name: "BoxStatus",
                table: "fulfillment_groups");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "fulfillment_groups");
        }
    }
}
