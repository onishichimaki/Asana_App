using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/asana/connection")]
public sealed class AsanaConnectionController(AsanaConnectionService connectionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AsanaConnectionStatus>> GetStatus(
        CancellationToken cancellationToken) =>
        Ok(await connectionService.GetStatusAsync(cancellationToken));

    [HttpPost("start")]
    public ActionResult<object> Start() =>
        Ok(new { authorizationUrl = connectionService.BuildAuthorizationUri() });

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return Redirect("/?asana=cancelled");
        }
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Redirect("/?asana=error");
        }

        try
        {
            await connectionService.ConnectAsync(
                code,
                state,
                HttpContext.TraceIdentifier,
                cancellationToken);
            return Redirect("/?asana=connected");
        }
        catch (AsanaEmailMismatchException)
        {
            return Redirect("/?asana=email-mismatch");
        }
        catch
        {
            return Redirect("/?asana=error");
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await connectionService.DisconnectAsync(
            HttpContext.TraceIdentifier,
            cancellationToken);
        return NoContent();
    }
}
