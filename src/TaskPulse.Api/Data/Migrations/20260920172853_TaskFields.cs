using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskPulse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TaskFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeId",
                table: "tasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssigneeName",
                table: "tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAtUtc",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Labels",
                table: "tasks",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "tasks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_AssigneeId",
                table: "tasks",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_DueAtUtc",
                table: "tasks",
                column: "DueAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_Labels",
                table: "tasks",
                column: "Labels")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_AssigneeId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_DueAtUtc",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_Labels",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AssigneeId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AssigneeName",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "DueAtUtc",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Labels",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "tasks");
        }
    }
}
