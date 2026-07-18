using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Data;

namespace TaskCapture.Api.Tests;

public sealed class TaskCaptureDbContextFactoryTests
{
    [Fact]
    public void CreateDbContext_PrefersEnvironmentConnectionString()
    {
        const string variableName = "ConnectionStrings__TaskCapture";
        const string expected =
            "Server=sql-test-host;Database=TaskCaptureFactoryTest;Integrated Security=True;TrustServerCertificate=True";
        var original = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, expected);
            using var db = new TaskCaptureDbContextFactory().CreateDbContext([]);

            Assert.Equal(expected, db.Database.GetConnectionString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, original);
        }
    }
}
