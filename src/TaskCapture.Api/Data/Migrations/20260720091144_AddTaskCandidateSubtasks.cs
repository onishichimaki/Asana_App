using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskCapture.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCandidateSubtasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskCandidateSubtasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskCandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCandidateSubtasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskCandidateSubtasks_TaskCandidates_TaskCandidateId",
                        column: x => x.TaskCandidateId,
                        principalTable: "TaskCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AsanaSubtaskRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskCandidateSubtaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalTaskGid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExternalTaskUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsanaSubtaskRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AsanaSubtaskRegistrations_TaskCandidateSubtasks_TaskCandidateSubtaskId",
                        column: x => x.TaskCandidateSubtaskId,
                        principalTable: "TaskCandidateSubtasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AsanaSubtaskRegistrations_ExternalTaskGid",
                table: "AsanaSubtaskRegistrations",
                column: "ExternalTaskGid");

            migrationBuilder.CreateIndex(
                name: "IX_AsanaSubtaskRegistrations_TaskCandidateSubtaskId",
                table: "AsanaSubtaskRegistrations",
                column: "TaskCandidateSubtaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCandidateSubtasks_TaskCandidateId_SortOrder",
                table: "TaskCandidateSubtasks",
                columns: new[] { "TaskCandidateId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsanaSubtaskRegistrations");

            migrationBuilder.DropTable(
                name: "TaskCandidateSubtasks");
        }
    }
}
