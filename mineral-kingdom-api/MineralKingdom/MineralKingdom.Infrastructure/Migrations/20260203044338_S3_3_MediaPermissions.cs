using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S3_3_MediaPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auctions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auctions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auctions_ListingId",
                table: "auctions",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_ListingId_Status",
                table: "auctions",
                columns: new[] { "ListingId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auctions");
        }
    }
}
