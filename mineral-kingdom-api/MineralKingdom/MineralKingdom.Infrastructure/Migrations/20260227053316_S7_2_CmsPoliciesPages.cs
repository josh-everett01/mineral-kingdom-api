using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S7_2_CmsPoliciesPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"create extension if not exists pgcrypto;");

            migrationBuilder.CreateTable(
                name: "cms_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cms_pages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cms_page_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentMarkdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    ContentHtml = table.Column<string>(type: "character varying(40000)", maxLength: 40000, nullable: true),
                    EditorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cms_page_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cms_page_revisions_cms_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "cms_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cms_page_revisions_PageId",
                table: "cms_page_revisions",
                column: "PageId",
                unique: true,
                filter: "\"Status\" = 'PUBLISHED'");

            migrationBuilder.CreateIndex(
                name: "IX_cms_page_revisions_PageId_Status",
                table: "cms_page_revisions",
                columns: new[] { "PageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_pages_Category_IsActive",
                table: "cms_pages",
                columns: new[] { "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_pages_Slug",
                table: "cms_pages",
                column: "Slug",
                unique: true);

            migrationBuilder.Sql(@"
                    insert into cms_pages (""Id"", ""Slug"", ""Title"", ""Category"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                    values
                    (gen_random_uuid(), 'about', 'About', 'MARKETING', true, now(), now()),
                    (gen_random_uuid(), 'faq', 'FAQ', 'MARKETING', true, now(), now()),
                    (gen_random_uuid(), 'terms', 'Terms & Conditions', 'POLICY', true, now(), now()),
                    (gen_random_uuid(), 'privacy', 'Privacy Policy', 'POLICY', true, now(), now()),
                    (gen_random_uuid(), 'auction-rules', 'Auction Rules', 'POLICY', true, now(), now()),
                    (gen_random_uuid(), 'buying-rules', 'Buying Rules', 'POLICY', true, now(), now())
                    on conflict (""Slug"") do nothing;
                    ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cms_page_revisions");

            migrationBuilder.DropTable(
                name: "cms_pages");
        }
    }
}
