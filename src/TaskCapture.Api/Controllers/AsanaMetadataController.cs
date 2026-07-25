using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Data;
using TaskCapture.Api.Security;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/asana")]
public sealed class AsanaMetadataController(
    IAsanaMetadataService metadataService,
    TaskCaptureDbContext db,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("projects")]
    public async Task<ActionResult<AsanaProjectCatalog>> GetProjects(CancellationToken cancellationToken)
    {
        var catalog = await metadataService.GetProjectsAsync(cancellationToken);
        var user = await GetUserAsync(cancellationToken);
        var preferences = await db.UserProjectPreferences
            .Where(item => item.UserId == user.Id)
            .ToDictionaryAsync(item => item.ProjectGid, cancellationToken);
        var projects = catalog.Projects
            .Where(project =>
                !user.RestrictProjects
                || preferences.TryGetValue(project.Gid, out var preference) && preference.IsAllowed)
            .Select(project => project with
            {
                IsFavorite = preferences.TryGetValue(project.Gid, out var preference)
                    && preference.IsFavorite
            })
            .OrderByDescending(project => project.IsFavorite)
            .ThenBy(project => project.Name, StringComparer.Create(
                System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                ignoreCase: true))
            .ToArray();
        var defaultProject = projects.Any(project => project.Gid == catalog.DefaultProjectGid)
            ? catalog.DefaultProjectGid
            : projects.FirstOrDefault()?.Gid;
        return Ok(new AsanaProjectCatalog(defaultProject, projects));
    }

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

        await EnsureProjectAllowedAsync(projectGid, cancellationToken);
        return Ok(await metadataService.GetSectionsAsync(projectGid, cancellationToken));
    }

    [HttpGet("projects/{projectGid}/fields")]
    public async Task<ActionResult<IReadOnlyList<AsanaCustomFieldDefinition>>> GetFields(
        string projectGid,
        CancellationToken cancellationToken)
    {
        if (!IsValidNumber(projectGid))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(projectGid)] = ["プロジェクトの番号が正しくありません。"]
            }));
        }

        await EnsureProjectAllowedAsync(projectGid, cancellationToken);
        return Ok(await metadataService.GetCustomFieldsAsync(projectGid, cancellationToken));
    }

    [HttpPut("projects/{projectGid}/favorite")]
    public async Task<IActionResult> SetFavorite(
        string projectGid,
        [FromBody] SetProjectFavoriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidNumber(projectGid))
        {
            return BadRequest();
        }

        await EnsureProjectAllowedAsync(projectGid, cancellationToken);
        var user = await GetUserAsync(cancellationToken);
        var preference = await db.UserProjectPreferences.SingleOrDefaultAsync(
            item => item.UserId == user.Id && item.ProjectGid == projectGid,
            cancellationToken);
        if (preference is null)
        {
            preference = new UserProjectPreference
            {
                UserId = user.Id,
                ProjectGid = projectGid,
                ProjectName = request.ProjectName.Trim()
            };
            db.UserProjectPreferences.Add(preference);
        }
        preference.ProjectName = request.ProjectName.Trim();
        preference.IsFavorite = request.IsFavorite;
        preference.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<User> GetUserAsync(CancellationToken cancellationToken) =>
        await db.Users.SingleAsync(
            item => item.ClientKey == currentUser.ClientKey,
            cancellationToken);

    private async Task EnsureProjectAllowedAsync(
        string projectGid,
        CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(cancellationToken);
        if (!user.RestrictProjects) return;
        var allowed = await db.UserProjectPreferences.AnyAsync(
            item => item.UserId == user.Id
                && item.ProjectGid == projectGid
                && item.IsAllowed,
            cancellationToken);
        if (!allowed)
        {
            throw new UnauthorizedAccessException("このAsanaプロジェクトは利用を許可されていません。");
        }
    }

    private static bool IsValidNumber(string value) =>
        value.Length is >= 1 and <= 64 && value.All(char.IsDigit);
}

public sealed class SetProjectFavoriteRequest
{
    public bool IsFavorite { get; set; }
    [Required, MaxLength(200)] public string ProjectName { get; set; } = string.Empty;
}
