using Microsoft.AspNetCore.Mvc;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/task-candidates")]
public sealed class TaskCandidatesController(TaskWorkflowService workflow) : ControllerBase
{
    [HttpPut("{candidateId:guid}")]
    public async Task<ActionResult<TaskCandidateResponse>> Update(
        Guid candidateId,
        [FromBody] CandidateUpdateRequest request,
        CancellationToken cancellationToken) =>
        Ok(await workflow.UpdateCandidateAsync(candidateId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("{candidateId:guid}/register")]
    public async Task<ActionResult<RegistrationResponse>> Register(
        Guid candidateId,
        [FromBody] CandidateUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.RegisterAsync(candidateId, request, HttpContext.TraceIdentifier, cancellationToken);
        return result.Succeeded ? Ok(result) : StatusCode(StatusCodes.Status502BadGateway, result);
    }
}
