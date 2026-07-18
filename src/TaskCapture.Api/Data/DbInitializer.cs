using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider rootServices,
        CancellationToken cancellationToken)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var organizationOptions = scope.ServiceProvider.GetRequiredService<IOptions<TaskOrganizationOptions>>().Value;
        var asanaOptions = scope.ServiceProvider.GetRequiredService<IOptions<AsanaOptions>>().Value;

        if (db.Database.IsRelational())
        {
            if (databaseOptions.ApplyMigrations)
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        if (!await db.ApplicationSettings.AnyAsync(cancellationToken))
        {
            db.ApplicationSettings.AddRange(
                new ApplicationSetting
                {
                    Key = "TaskOrganization.Mode",
                    Value = organizationOptions.Mode,
                    Description = "The active task organizer implementation. Secrets are never stored here."
                },
                new ApplicationSetting
                {
                    Key = "Integration.Asana.Mode",
                    Value = asanaOptions.Mode,
                    Description = "The active Asana integration mode. PAT is read from server configuration."
                });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
