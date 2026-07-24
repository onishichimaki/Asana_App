using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskCapture.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWbsImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WbsImportProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LayoutSignature = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SheetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HeaderRow = table.Column<int>(type: "int", nullable: false),
                    DataStartRow = table.Column<int>(type: "int", nullable: false),
                    MappingJson = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    ProjectGid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SectionGid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WbsImportProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WbsImportProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WbsImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WbsImportProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SheetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LayoutSignature = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProjectGid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SectionGid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ValidRows = table.Column<int>(type: "int", nullable: false),
                    SucceededRows = table.Column<int>(type: "int", nullable: false),
                    FailedRows = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WbsImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WbsImportBatches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WbsImportBatches_WbsImportProfiles_WbsImportProfileId",
                        column: x => x.WbsImportProfileId,
                        principalTable: "WbsImportProfiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WbsImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WbsImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentRowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsGeneratedKey = table.Column<bool>(type: "bit", nullable: false),
                    RowHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Included = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    Assignee = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ValidationErrorsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ExternalTaskGid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExternalTaskUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AssigneeResolutionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ResolvedAssigneeGid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResolvedAssigneeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WarningMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WbsImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WbsImportRows_WbsImportBatches_WbsImportBatchId",
                        column: x => x.WbsImportBatchId,
                        principalTable: "WbsImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WbsImportRows_WbsImportRows_ParentRowId",
                        column: x => x.ParentRowId,
                        principalTable: "WbsImportRows",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportBatches_FileHash",
                table: "WbsImportBatches",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportBatches_UserId_CreatedAtUtc",
                table: "WbsImportBatches",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportBatches_WbsImportProfileId",
                table: "WbsImportBatches",
                column: "WbsImportProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportProfiles_UserId_LayoutSignature",
                table: "WbsImportProfiles",
                columns: new[] { "UserId", "LayoutSignature" });

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportProfiles_UserId_Name",
                table: "WbsImportProfiles",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportRows_ExternalTaskGid",
                table: "WbsImportRows",
                column: "ExternalTaskGid");

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportRows_ParentRowId",
                table: "WbsImportRows",
                column: "ParentRowId");

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportRows_RowHash",
                table: "WbsImportRows",
                column: "RowHash");

            migrationBuilder.CreateIndex(
                name: "IX_WbsImportRows_WbsImportBatchId_SourceKey",
                table: "WbsImportRows",
                columns: new[] { "WbsImportBatchId", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WbsImportRows");

            migrationBuilder.DropTable(
                name: "WbsImportBatches");

            migrationBuilder.DropTable(
                name: "WbsImportProfiles");
        }
    }
}
