using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskCapture.Api.Data;

public sealed class TaskCaptureDbContextFactory : IDesignTimeDbContextFactory<TaskCaptureDbContext>
{
    public TaskCaptureDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TaskCapture");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Server=(localdb)\\MSSQLLocalDB;Database=TaskCaptureDesign;Trusted_Connection=True;TrustServerCertificate=True";
        }

        var options = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new TaskCaptureDbContext(options);
    }
}
