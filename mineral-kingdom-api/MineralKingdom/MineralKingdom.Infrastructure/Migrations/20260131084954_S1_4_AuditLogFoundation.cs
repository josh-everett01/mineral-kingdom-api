using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S1_4_AuditLogFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add new generalized columns (nullable first so we can backfill safely)
            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "admin_audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "admin_audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid?>(
            name: "EntityId",
            table: "admin_audit_logs",
            type: "uuid",
            nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorRole",
                table: "admin_audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "admin_audit_logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "admin_audit_logs",
                type: "text",
                nullable: true);

            // JSONB (nullable)
            migrationBuilder.AddColumn<string>(
                name: "BeforeJson",
                table: "admin_audit_logs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterJson",
                table: "admin_audit_logs",
                type: "jsonb",
                nullable: true);

            // 2) Backfill new columns from S1-3 legacy columns
            // ActionType <- Action
            // EntityType <- 'USER'
            // EntityId <- TargetUserId
            // BeforeJson <- {"role": "<BeforeRole>"}
            // AfterJson <- {"role": "<AfterRole>"}
            migrationBuilder.Sql("""
            UPDATE admin_audit_logs
            SET
              "ActionType" = "Action",
              "EntityType" = 'USER',
              "EntityId" = "TargetUserId",
              "BeforeJson" = jsonb_build_object('role', "BeforeRole"),
              "AfterJson"  = jsonb_build_object('role', "AfterRole")
            WHERE "ActionType" IS NULL;
        """);

            // 3) Make new columns NOT NULL now that they are backfilled
            migrationBuilder.AlterColumn<string>(
                name: "ActionType",
                table: "admin_audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EntityType",
                table: "admin_audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "EntityId",
                table: "admin_audit_logs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // Update indexes: drop TargetUserId index, add EntityType+EntityId
            migrationBuilder.DropIndex(
                name: "IX_admin_audit_logs_TargetUserId",
                table: "admin_audit_logs");

            // 4) Drop legacy columns (Option B: clean schema)
            migrationBuilder.DropColumn(name: "Action", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "BeforeRole", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "AfterRole", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "TargetUserId", table: "admin_audit_logs");

            // 5) Update indexes: drop TargetUserId index, add EntityType+EntityId
            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_logs_EntityType_EntityId",
                table: "admin_audit_logs",
                columns: new[] { "EntityType", "EntityId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-create legacy columns
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "admin_audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                table: "admin_audit_logs",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "BeforeRole",
                table: "admin_audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "USER");

            migrationBuilder.AddColumn<string>(
                name: "AfterRole",
                table: "admin_audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "USER");

            // Backfill legacy columns from generalized columns where possible
            migrationBuilder.Sql("""
            UPDATE admin_audit_logs
            SET
              "Action" = "ActionType",
              "TargetUserId" = "EntityId",
              "BeforeRole" = COALESCE(("BeforeJson"->>'role'), 'USER'),
              "AfterRole"  = COALESCE(("AfterJson"->>'role'),  'USER')
            WHERE "EntityType" = 'USER';
        """);

            // Drop new index, restore old index
            migrationBuilder.DropIndex(
                name: "IX_admin_audit_logs_EntityType_EntityId",
                table: "admin_audit_logs");

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_logs_TargetUserId",
                table: "admin_audit_logs",
                column: "TargetUserId");

            // Drop new generalized columns
            migrationBuilder.DropColumn(name: "ActionType", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "EntityType", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "EntityId", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "ActorRole", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "IpAddress", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "UserAgent", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "BeforeJson", table: "admin_audit_logs");
            migrationBuilder.DropColumn(name: "AfterJson", table: "admin_audit_logs");
        }
    }
}
