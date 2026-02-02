using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S3_1_ListingsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "minerals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_minerals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "listings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "DRAFT"),
                    PrimaryMineralId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocalityDisplay = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    AdminArea1 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AdminArea2 = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MineName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LengthCm = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    WidthCm = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    WeightGrams = table.Column<int>(type: "integer", nullable: true),
                    SizeClass = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsFluorescent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    FluorescenceNotes = table.Column<string>(type: "text", nullable: true),
                    ConditionNotes = table.Column<string>(type: "text", nullable: true),
                    IsLot = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    QuantityTotal = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    QuantityAvailable = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_listings_minerals_PrimaryMineralId",
                        column: x => x.PrimaryMineralId,
                        principalTable: "minerals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "listing_media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listing_media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_listing_media_listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_listing_media_ListingId",
                table: "listing_media",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_listing_media_ListingId_SortOrder",
                table: "listing_media",
                columns: new[] { "ListingId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_listings_Country_AdminArea1",
                table: "listings",
                columns: new[] { "CountryCode", "AdminArea1" });

            migrationBuilder.CreateIndex(
                name: "IX_listings_PrimaryMineralId",
                table: "listings",
                column: "PrimaryMineralId");

            migrationBuilder.CreateIndex(
                name: "IX_listings_SizeClass",
                table: "listings",
                column: "SizeClass");

            migrationBuilder.CreateIndex(
                name: "IX_listings_Status_PublishedAt",
                table: "listings",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_minerals_Name",
                table: "minerals",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "listing_media");

            migrationBuilder.DropTable(
                name: "listings");

            migrationBuilder.DropTable(
                name: "minerals");
        }
    }
}
