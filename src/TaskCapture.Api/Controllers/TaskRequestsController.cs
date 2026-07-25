using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Security;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/task-requests")]
public sealed class TaskRequestsController(
    TaskWorkflowService workflow,
    ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost("organize")]
    [ProducesResponseType<OrganizeTaskResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizeTaskResponse>> Organize(
        [FromBody] OrganizeTaskRequest request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.OrganizeAsync(
            request,
            currentUser.ClientKey,
            HttpContext.TraceIdentifier,
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<RecentTaskResponse>>> Recent(
        [FromQuery] int take = 20,
        [FromQuery, StringLength(200)] string? search = null,
        [FromQuery, RegularExpression("^(Received|Organized|Registered|Failed)?$")] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await workflow.GetRecentAsync(
            currentUser.ClientKey,
            take,
            search,
            status,
            cancellationToken));
}
