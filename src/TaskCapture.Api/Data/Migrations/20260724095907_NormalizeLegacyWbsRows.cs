using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskCapture.Api.Data.Migrations;

/// <summary>
/// Preserves the migration-history boundary used by existing installations.
/// New installations have no legacy WBS rows to normalize.
/// </summary>
[DbContext(typeof(TaskCaptureDbContext))]
[Migration("20260724095907_NormalizeLegacyWbsRows")]
public partial class NormalizeLegacyWbsRows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The original release normalized rows created before the improved WBS schema.
        // Existing databases already ran that one-time operation; fresh databases have
        // no legacy rows at this point in the migration chain.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
