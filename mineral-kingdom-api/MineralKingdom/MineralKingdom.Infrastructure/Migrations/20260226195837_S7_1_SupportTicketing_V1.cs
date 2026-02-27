using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S7_1_SupportTicketing_V1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure gen_random_uuid() is available for backfill inserts
            migrationBuilder.Sql(@"create extension if not exists pgcrypto;");

            // Drop old index (we'll recreate later)
            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CreatedAt",
                table: "support_tickets");

            // Preserve Email data by renaming to GuestEmail
            migrationBuilder.RenameColumn(
                name: "Email",
                table: "support_tickets",
                newName: "GuestEmail");

            // IMPORTANT: After rename, ensure GuestEmail is nullable (member tickets should not require it)
            // Also remove any legacy default like ''.
            migrationBuilder.AlterColumn<string>(
                name: "GuestEmail",
                table: "support_tickets",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            // Status default for v1
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "support_tickets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "OPEN",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "support_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "support_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "support_tickets",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "NORMAL");

            // IMPORTANT: Do NOT default to "" because we create a UNIQUE index.
            migrationBuilder.AddColumn<string>(
                name: "TicketNumber",
                table: "support_tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PENDING");

            // EF scaffold default is DateTimeOffset.MinValue; we'll backfill UpdatedAt = CreatedAt for existing rows.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "support_ticket_access_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_ticket_access_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_ticket_access_tokens_support_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BodyText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsInternalNote = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_ticket_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_ticket_messages_support_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_ticket_messages_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Backfill message thread from legacy Message column (if present/non-empty)
            migrationBuilder.Sql(@"
insert into support_ticket_messages
  (""Id"", ""TicketId"", ""AuthorType"", ""AuthorUserId"", ""BodyText"", ""IsInternalNote"", ""CreatedAt"")
select
  gen_random_uuid(),
  t.""Id"",
  'CUSTOMER',
  NULL,
  t.""Message"",
  false,
  t.""CreatedAt""
from support_tickets t
where t.""Message"" is not null and length(trim(t.""Message"")) > 0;
");

            // Now that messages are preserved, drop legacy Message column
            migrationBuilder.DropColumn(
                name: "Message",
                table: "support_tickets");

            // Backfill UpdatedAt for existing rows
            migrationBuilder.Sql(@"
update support_tickets
set ""UpdatedAt"" = ""CreatedAt""
where ""UpdatedAt"" = '0001-01-01T00:00:00+00'::timestamptz
   or ""UpdatedAt"" is null;
");

            // Backfill TicketNumber for existing rows with a unique deterministic value
            migrationBuilder.Sql(@"
update support_tickets
set ""TicketNumber"" = 'ST-' || upper(substr(replace(""Id""::text, '-', ''), 1, 10))
where ""TicketNumber"" is null
   or ""TicketNumber"" = ''
   or ""TicketNumber"" = 'PENDING';
");

            // Indexes
            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_AssignedToUserId",
                table: "support_tickets",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CreatedByUserId",
                table: "support_tickets",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_GuestEmail",
                table: "support_tickets",
                column: "GuestEmail");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_Status_Priority_UpdatedAt",
                table: "support_tickets",
                columns: new[] { "Status", "Priority", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_TicketNumber",
                table: "support_tickets",
                column: "TicketNumber",
                unique: true);

            // Recreate CreatedAt index (helps admin triage queries)
            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CreatedAt",
                table: "support_tickets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_access_tokens_TicketId",
                table: "support_ticket_access_tokens",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_access_tokens_TokenHash",
                table: "support_ticket_access_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_messages_AuthorUserId",
                table: "support_ticket_messages",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_messages_TicketId_CreatedAt",
                table: "support_ticket_messages",
                columns: new[] { "TicketId", "CreatedAt" });

            // Foreign keys (assignments + created-by)
            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_users_AssignedToUserId",
                table: "support_tickets",
                column: "AssignedToUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_users_CreatedByUserId",
                table: "support_tickets",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Design-required check constraints
            migrationBuilder.Sql(@"
alter table support_tickets
add constraint ""CK_support_tickets_creator_present""
check (""CreatedByUserId"" is not null or (""GuestEmail"" is not null and length(trim(""GuestEmail"")) > 0));
");

            migrationBuilder.Sql(@"
alter table support_tickets
add constraint ""CK_support_tickets_single_link""
check (
  (case when ""LinkedOrderId"" is null then 0 else 1 end) +
  (case when ""LinkedAuctionId"" is null then 0 else 1 end) +
  (case when ""LinkedShippingInvoiceId"" is null then 0 else 1 end) +
  (case when ""LinkedListingId"" is null then 0 else 1 end)
  <= 1
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop check constraints first (idempotent)
            migrationBuilder.Sql(@"alter table support_tickets drop constraint if exists ""CK_support_tickets_creator_present"";");
            migrationBuilder.Sql(@"alter table support_tickets drop constraint if exists ""CK_support_tickets_single_link"";");

            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_users_AssignedToUserId",
                table: "support_tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_users_CreatedByUserId",
                table: "support_tickets");

            migrationBuilder.DropTable(
                name: "support_ticket_access_tokens");

            migrationBuilder.DropTable(
                name: "support_ticket_messages");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_AssignedToUserId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CreatedByUserId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_GuestEmail",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_Status_Priority_UpdatedAt",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_TicketNumber",
                table: "support_tickets");

            // Up() recreated this, so Down() must drop it before recreating
            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CreatedAt",
                table: "support_tickets");

            // Recreate legacy Message column (data cannot be reliably reconstructed in Down)
            migrationBuilder.AddColumn<string>(
                name: "Message",
                table: "support_tickets",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "TicketNumber",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "support_tickets");

            // Rename GuestEmail back to Email
            migrationBuilder.RenameColumn(
                name: "GuestEmail",
                table: "support_tickets",
                newName: "Email");

            // Restore legacy Email to NOT NULL with default "" (best-effort to match original)
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "support_tickets",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "support_tickets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "OPEN");

            // Restore CreatedAt index as originally present
            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CreatedAt",
                table: "support_tickets",
                column: "CreatedAt");
        }
    }
}