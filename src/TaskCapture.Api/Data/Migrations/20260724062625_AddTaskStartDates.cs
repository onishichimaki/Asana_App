using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskCapture.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskStartDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "WbsImportRows",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "TaskCandidates",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "WbsImportRows");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "TaskCandidates");
        }
    }
}
