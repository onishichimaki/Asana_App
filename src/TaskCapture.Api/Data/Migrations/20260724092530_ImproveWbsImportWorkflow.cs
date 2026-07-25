using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskCapture.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImproveWbsImportWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeType",
                table: "WbsImportRows",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "WbsImportRows",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedHours",
                table: "WbsImportRows",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousExternalTaskGid",
                table: "WbsImportRows",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousExternalTaskUrl",
                table: "WbsImportRows",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "WbsImportRows",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Progress",
                table: "WbsImportRows",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevertedAtUtc",
                table: "WbsImportRows",
                type: "datetimeoffset(0)",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasCreatedInBatch",
                table: "WbsImportRows",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomFieldTargetsJson",
                table: "WbsImportBatches",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<bool>(
                name: "PutUnmatchedExtraFieldsInDescription",
                table: "WbsImportBatches",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "WbsColumnAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ColumnName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedColumnName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WbsColumnAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WbsColumnAliases_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WbsColumnAliases_UserId_NormalizedColumnName",
                table: "WbsColumnAliases",
                columns: new[] { "UserId", "NormalizedColumnName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WbsColumnAliases");

            migrationBuilder.DropColumn(
                name: "ChangeType",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "EstimatedHours",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "PreviousExternalTaskGid",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "PreviousExternalTaskUrl",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "Progress",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "RevertedAtUtc",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "WasCreatedInBatch",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "CustomFieldTargetsJson",
                table: "WbsImportBatches");

            migrationBuilder.DropColumn(
                name: "PutUnmatchedExtraFieldsInDescription",
                table: "WbsImportBatches");
        }
    }
}
