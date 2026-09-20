using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskPulse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferencesAndUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Nickname = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preferences", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "uploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uploads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_uploads_CreatedAtUtc",
                table: "uploads",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_uploads_Source",
                table: "uploads",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preferences");

            migrationBuilder.DropTable(
                name: "uploads");
        }
    }
}
