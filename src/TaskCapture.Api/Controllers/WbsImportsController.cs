using Microsoft.AspNetCore.Mvc;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/wbs-imports")]
public sealed class WbsImportsController(WbsImportService service) : ControllerBase
{
    [HttpGet("profiles")]
    public async Task<ActionResult<IReadOnlyList<WbsImportProfileResponse>>> GetProfiles(
        CancellationToken cancellationToken) =>
        Ok(await service.GetProfilesAsync(ClientKey(), cancellationToken));

    [HttpPost("profiles")]
    public async Task<ActionResult<WbsImportProfileResponse>> CreateProfile(
        [FromBody] WbsImportProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.SaveProfileAsync(
            null,
            request,
            ClientKey(),
            HttpContext.TraceIdentifier,
            cancellationToken);
        return CreatedAtAction(nameof(GetProfiles), result);
    }

    [HttpPut("profiles/{profileId:guid}")]
    public async Task<ActionResult<WbsImportProfileResponse>> UpdateProfile(
        Guid profileId,
        [FromBody] WbsImportProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SaveProfileAsync(
            profileId,
            request,
            ClientKey(),
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpDelete("profiles/{profileId:guid}")]
    public async Task<IActionResult> DeleteProfile(Guid profileId, CancellationToken cancellationToken)
    {
        await service.DeleteProfileAsync(
            profileId,
            ClientKey(),
            HttpContext.TraceIdentifier,
            cancellationToken);
        return NoContent();
    }

    [HttpPost("batches")]
    public async Task<ActionResult<WbsImportBatchResponse>> CreateBatch(
        [FromBody] WbsImportBatchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateBatchAsync(
            request,
            ClientKey(),
            HttpContext.TraceIdentifier,
            cancellationToken);
        return CreatedAtAction(nameof(GetBatch), new { batchId = result.Id }, result);
    }

    [HttpGet("batches/{batchId:guid}")]
    public async Task<ActionResult<WbsImportBatchResponse>> GetBatch(
        Guid batchId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetBatchAsync(batchId, ClientKey(), cancellationToken));

    [HttpPost("batches/{batchId:guid}/register")]
    public async Task<ActionResult<WbsImportBatchResponse>> RegisterBatch(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var result = await service.RegisterBatchAsync(
            batchId,
            ClientKey(),
            HttpContext.TraceIdentifier,
            cancellationToken);
        return result.Status == "Registered"
            ? Ok(result)
            : StatusCode(StatusCodes.Status207MultiStatus, result);
    }

    [HttpGet("batches/{batchId:guid}/errors.csv")]
    public async Task<IActionResult> DownloadErrors(Guid batchId, CancellationToken cancellationToken)
    {
        var content = await service.GetErrorCsvAsync(batchId, ClientKey(), cancellationToken);
        return File(content, "text/csv; charset=utf-8", $"wbs-import-{batchId:N}-errors.csv");
    }

    private string ClientKey() =>
        Request.Headers["X-TaskCapture-Client"].FirstOrDefault() ?? "local-device";
}
