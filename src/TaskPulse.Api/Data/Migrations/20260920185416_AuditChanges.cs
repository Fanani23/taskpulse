using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskPulse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Changes",
                table: "audit",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_Resource_Kind_TargetId_Id",
                table: "audit",
                columns: new[] { "Resource", "Kind", "TargetId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_Resource_Kind_TargetId_Id",
                table: "audit");

            migrationBuilder.DropColumn(
                name: "Changes",
                table: "audit");
        }
    }
}
