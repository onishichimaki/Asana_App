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

        await UpsertSettingAsync(
            db,
            "TaskOrganization.Mode",
            organizationOptions.Mode,
            "The active task organizer implementation. Secrets are never stored here.",
            cancellationToken);
        await UpsertSettingAsync(
            db,
            "Integration.Asana.Mode",
            asanaOptions.Mode,
            "The active Asana integration mode. PAT is read from server configuration.",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpsertSettingAsync(
        TaskCaptureDbContext db,
        string key,
        string value,
        string description,
        CancellationToken cancellationToken)
    {
        var setting = await db.ApplicationSettings.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (setting is null)
        {
            db.ApplicationSettings.Add(new ApplicationSetting
            {
                Key = key,
                Value = value,
                Description = description
            });
            return;
        }

        setting.Value = value;
        setting.Description = description;
    }
}
