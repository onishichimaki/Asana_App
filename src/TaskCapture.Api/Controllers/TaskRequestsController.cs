using Microsoft.AspNetCore.Mvc;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/task-requests")]
public sealed class TaskRequestsController(TaskWorkflowService workflow) : ControllerBase
{
    [HttpPost("organize")]
    [ProducesResponseType<OrganizeTaskResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizeTaskResponse>> Organize(
        [FromBody] OrganizeTaskRequest request,
        CancellationToken cancellationToken)
    {
        var clientKey = Request.Headers["X-TaskCapture-Client"].FirstOrDefault() ?? "local-device";
        var result = await workflow.OrganizeAsync(request, clientKey, HttpContext.TraceIdentifier, cancellationToken);
        return Ok(result);
    }

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<RecentTaskResponse>>> Recent(
        [FromQuery] int take = 5,
        CancellationToken cancellationToken = default) =>
        Ok(await workflow.GetRecentAsync(take, cancellationToken));
}
