using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;
using TaskCapture.Api.Services;

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
        var accessOptions = scope.ServiceProvider.GetRequiredService<IOptions<AccessOptions>>().Value;
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        ValidateAccessConfiguration(accessOptions, environment);
        ValidateTaskOrganizationConfiguration(organizationOptions);
        var productionIssues = ProductionConfigurationValidator.GetBlockingIssues(
            environment,
            configuration,
            databaseOptions,
            organizationOptions,
            asanaOptions,
            accessOptions);
        if (productionIssues.Count > 0)
        {
            throw new InvalidOperationException(
                "本番設定が未完了です: " + string.Join(' ', productionIssues));
        }

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
        await UpsertSettingAsync(
            db,
            "Integration.Asana.CredentialMode",
            asanaOptions.CredentialMode,
            "The active Asana credential source. Tokens are never stored here.",
            cancellationToken);
        await UpsertSettingAsync(
            db,
            "Access.Mode",
            accessOptions.Mode,
            "The active sign-in method. Email codes and sessions are stored only as protected values.",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateTaskOrganizationConfiguration(TaskOrganizationOptions options)
    {
        if (options.Mode.Equals("RuleBased", StringComparison.OrdinalIgnoreCase)
            || options.Mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!options.Mode.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported task organizer mode: {options.Mode}");
        }

        if (string.IsNullOrWhiteSpace(options.AzureOpenAI.Endpoint)
            || string.IsNullOrWhiteSpace(options.AzureOpenAI.DeploymentName))
        {
            if (options.FallbackToRuleBased)
            {
                return;
            }
            throw new InvalidOperationException(
                "AzureOpenAI mode requires TaskOrganization:AzureOpenAI:Endpoint and DeploymentName.");
        }

        if (!Uri.TryCreate(options.AzureOpenAI.Endpoint, UriKind.Absolute, out var endpoint)
            || !endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "TaskOrganization:AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        }
    }

    private static void ValidateAccessConfiguration(
        AccessOptions options,
        IHostEnvironment environment)
    {
        if (options.Mode.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Access:Mode=Development is allowed only in Development or Testing. Configure AsanaOAuth before deployment.");
            }

            return;
        }

        var isEmailCode = options.Mode.Equals("EmailCode", StringComparison.OrdinalIgnoreCase);
        var isAsanaOAuth = options.Mode.Equals("AsanaOAuth", StringComparison.OrdinalIgnoreCase);
        if (!isEmailCode && !isAsanaOAuth)
        {
            throw new InvalidOperationException($"Unsupported access mode: {options.Mode}");
        }

        if (!options.AllowedEmailDomains.Any(domain =>
                !string.IsNullOrWhiteSpace(domain.Trim().TrimStart('@'))))
        {
            throw new InvalidOperationException(
                "Access:AllowedEmailDomains must contain at least one company email domain.");
        }

        if (isAsanaOAuth)
        {
            return;
        }

        var deliveryMode = options.EmailCode.Delivery.Mode;
        if (!deliveryMode.Equals("Mock", StringComparison.OrdinalIgnoreCase)
            && !deliveryMode.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported Access:EmailCode:Delivery:Mode: {deliveryMode}");
        }

        if (!environment.IsDevelopment()
            && !environment.IsEnvironment("Testing")
            && deliveryMode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Access:EmailCode:Delivery:Mode=Mock cannot be used outside Development or Testing.");
        }

        if (deliveryMode.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(options.EmailCode.Delivery.Host)
                || string.IsNullOrWhiteSpace(options.EmailCode.Delivery.FromAddress)))
        {
            throw new InvalidOperationException(
                "SMTP delivery requires Access:EmailCode:Delivery:Host and FromAddress.");
        }
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
