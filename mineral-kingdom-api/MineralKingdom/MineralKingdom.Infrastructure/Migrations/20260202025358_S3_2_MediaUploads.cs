using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S3_2_MediaUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ContentLengthBytes",
                table: "listing_media",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "listing_media",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "listing_media",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "listing_media",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "listing_media",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "READY");

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "listing_media",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "listing_media",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentLengthBytes",
                table: "listing_media");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "listing_media");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "listing_media");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "listing_media");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "listing_media");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "listing_media");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "listing_media");
        }
    }
}
