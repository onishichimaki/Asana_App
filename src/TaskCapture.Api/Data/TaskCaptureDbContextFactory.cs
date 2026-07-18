using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskCapture.Api.Data;

public sealed class TaskCaptureDbContextFactory : IDesignTimeDbContextFactory<TaskCaptureDbContext>
{
    public TaskCaptureDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=TaskCaptureDesign;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new TaskCaptureDbContext(options);
    }
}
