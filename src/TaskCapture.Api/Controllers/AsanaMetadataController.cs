using Microsoft.AspNetCore.Mvc;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/asana")]
public sealed class AsanaMetadataController(IAsanaMetadataService metadataService) : ControllerBase
{
    [HttpGet("projects")]
    public async Task<ActionResult<AsanaProjectCatalog>> GetProjects(CancellationToken cancellationToken) =>
        Ok(await metadataService.GetProjectsAsync(cancellationToken));

    [HttpGet("projects/{projectGid}/sections")]
    public async Task<ActionResult<IReadOnlyList<AsanaSectionOption>>> GetSections(
        string projectGid,
        CancellationToken cancellationToken)
    {
        if (projectGid.Length is < 1 or > 64 || !projectGid.All(char.IsDigit))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(projectGid)] = ["Project GID must contain 1 to 64 digits."]
            }));
        }

        return Ok(await metadataService.GetSectionsAsync(projectGid, cancellationToken));
    }
}
