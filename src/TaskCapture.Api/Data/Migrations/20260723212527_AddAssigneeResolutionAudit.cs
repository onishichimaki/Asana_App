using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskCapture.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssigneeResolutionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeResolutionStatus",
                table: "AsanaRegistrations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedAssigneeGid",
                table: "AsanaRegistrations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedAssigneeName",
                table: "AsanaRegistrations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarningMessage",
                table: "AsanaRegistrations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssigneeResolutionStatus",
                table: "AsanaRegistrations");

            migrationBuilder.DropColumn(
                name: "ResolvedAssigneeGid",
                table: "AsanaRegistrations");

            migrationBuilder.DropColumn(
                name: "ResolvedAssigneeName",
                table: "AsanaRegistrations");

            migrationBuilder.DropColumn(
                name: "WarningMessage",
                table: "AsanaRegistrations");
        }
    }
}
