using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(
    IHostEnvironment environment,
    IOptions<DatabaseOptions> database,
    IOptions<TaskOrganizationOptions> organization,
    IOptions<AsanaOptions> asana,
    IOptions<AccessOptions> access,
    IConfiguration configuration,
    TaskCaptureDbContext db) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        environment = environment.EnvironmentName,
        database = database.Value.Provider,
        organizer = organization.Value.Mode,
        organizerModel = organization.Value.Mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
            ? organization.Value.Gemini.Model
            : organization.Value.Mode.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase)
                ? organization.Value.AzureOpenAI.DeploymentName
                : null,
        organizerFallback = organization.Value.FallbackToRuleBased,
        asana = asana.Value.Mode,
        asanaCredentialMode = asana.Value.CredentialMode,
        access = access.Value.Mode,
        timestampUtc = DateTimeOffset.UtcNow
    });

    [AllowAnonymous]
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var databaseReady = !db.Database.IsRelational()
            || await db.Database.CanConnectAsync(cancellationToken);
        var asanaReady = asana.Value.Mode.Equals("Mock", StringComparison.OrdinalIgnoreCase)
            || (asana.Value.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase)
                ? !string.IsNullOrWhiteSpace(asana.Value.OAuth.ClientId)
                    && !string.IsNullOrWhiteSpace(asana.Value.OAuth.ClientSecret)
                    && !string.IsNullOrWhiteSpace(asana.Value.OAuth.RedirectUri)
                : !string.IsNullOrWhiteSpace(asana.Value.PersonalAccessToken));
        var emailReady = access.Value.Mode.Equals("Development", StringComparison.OrdinalIgnoreCase)
            || (access.Value.AllowedEmailDomains.Length > 0
                && (access.Value.EmailCode.Delivery.Mode.Equals("Mock", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(access.Value.EmailCode.Delivery.Host)));
        var organizerPrimaryReady = organization.Value.Mode.Equals("RuleBased", StringComparison.OrdinalIgnoreCase)
            || (organization.Value.Mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(organization.Value.Gemini.ApiKey)
                    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))))
            || (organization.Value.Mode.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(organization.Value.AzureOpenAI.Endpoint)
                && !string.IsNullOrWhiteSpace(organization.Value.AzureOpenAI.DeploymentName)
                && (!string.IsNullOrWhiteSpace(organization.Value.AzureOpenAI.ApiKey)
                    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY"))));
        var organizerReady = organizerPrimaryReady || organization.Value.FallbackToRuleBased;
        var productionIssues = ProductionConfigurationValidator.GetBlockingIssues(
            environment,
            configuration,
            database.Value,
            organization.Value,
            asana.Value,
            access.Value);
        var productionConfigurationReady = productionIssues.Count == 0;
        var ready = databaseReady && asanaReady && emailReady && organizerReady
            && productionConfigurationReady;
        return StatusCode(
            ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = ready ? "ready" : "not-ready",
                database = databaseReady,
                asana = asanaReady,
                email = emailReady,
                organizer = organizerReady,
                organizerPrimary = organizerPrimaryReady,
                productionConfiguration = productionConfigurationReady,
                configurationIssues = productionIssues,
                timestampUtc = DateTimeOffset.UtcNow
            });
    }
}
