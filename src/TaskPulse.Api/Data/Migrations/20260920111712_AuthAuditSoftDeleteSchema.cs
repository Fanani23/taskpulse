using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskPulse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuthAuditSoftDeleteSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "uploads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "tasks",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "tasks",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "catalog",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "catalog",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Resource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "catalog_schemas",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Schema = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_schemas", x => x.Kind);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_DeletedAtUtc",
                table: "tasks",
                column: "DeletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_AtUtc",
                table: "audit",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_Resource",
                table: "audit",
                column: "Resource");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit");

            migrationBuilder.DropTable(
                name: "catalog_schemas");

            migrationBuilder.DropIndex(
                name: "IX_tasks_DeletedAtUtc",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "uploads");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "catalog");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "catalog");
        }
    }
}
