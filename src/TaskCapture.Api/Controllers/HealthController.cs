using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(
    IHostEnvironment environment,
    IOptions<DatabaseOptions> database,
    IOptions<TaskOrganizationOptions> organization,
    IOptions<AsanaOptions> asana) : ControllerBase
{
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
        timestampUtc = DateTimeOffset.UtcNow
    });
}
