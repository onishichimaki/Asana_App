using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(
    IHostEnvironment environment,
    IOptions<DatabaseOptions> database,
    IOptions<TaskOrganizationOptions> organization,
    IOptions<AsanaOptions> asana,
    IOptions<AccessOptions> access,
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
        var ready = databaseReady && asanaReady && emailReady;
        return StatusCode(
            ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = ready ? "ready" : "not-ready",
                database = databaseReady,
                asana = asanaReady,
                email = emailReady,
                timestampUtc = DateTimeOffset.UtcNow
            });
    }
}
