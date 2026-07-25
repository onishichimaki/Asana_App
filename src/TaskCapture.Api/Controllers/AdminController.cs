using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;
using TaskCapture.Api.Security;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Authorize(Policy = TaskCapturePolicies.Admin)]
[Route("api/admin")]
public sealed class AdminController(
    TaskCaptureDbContext db,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<object>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await db.Users
            .AsNoTracking()
            .OrderByDescending(user => user.LastLoginAtUtc)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsActive,
                user.IsAdmin,
                user.RestrictProjects,
                user.LastLoginAtUtc,
                TaskCount = user.TaskRequests.Count,
                AsanaConnected = user.AsanaConnection != null
                    && user.AsanaConnection.RevokedAtUtc == null,
                AllowedProjects = user.ProjectPreferences
                    .Where(item => item.IsAllowed)
                    .Select(item => new { item.ProjectGid, item.ProjectName })
            })
            .ToArrayAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPut("users/{userId:guid}/access")]
    public async Task<IActionResult> UpdateUserAccess(
        Guid userId,
        [FromBody] UpdateUserAccessRequest request,
        CancellationToken cancellationToken)
    {
        var target = await db.Users
            .Include(user => user.ProjectPreferences)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (target is null) return NotFound();

        if (string.Equals(target.ClientKey, currentUser.ClientKey, StringComparison.Ordinal)
            && (!request.IsActive || !request.IsAdmin))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "自分自身の管理権限や利用許可は解除できません。",
                Status = StatusCodes.Status400BadRequest
            });
        }

        target.IsActive = request.IsActive;
        target.IsAdmin = request.IsAdmin;
        target.RestrictProjects = request.RestrictProjects;
        foreach (var preference in target.ProjectPreferences)
        {
            preference.IsAllowed = false;
            preference.UpdatedAtUtc = timeProvider.GetUtcNow();
        }
        foreach (var input in request.AllowedProjects
                     .DistinctBy(item => item.ProjectGid))
        {
            var preference = target.ProjectPreferences
                .SingleOrDefault(item => item.ProjectGid == input.ProjectGid);
            if (preference is null)
            {
                preference = new UserProjectPreference
                {
                    UserId = target.Id,
                    ProjectGid = input.ProjectGid
                };
                target.ProjectPreferences.Add(preference);
            }
            preference.ProjectName = input.ProjectName.Trim();
            preference.IsAllowed = true;
            preference.UpdatedAtUtc = timeProvider.GetUtcNow();
        }

        var actor = await db.Users.SingleAsync(
            user => user.ClientKey == currentUser.ClientKey,
            cancellationToken);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.Id,
            EventType = "UserAccessUpdated",
            EntityType = nameof(User),
            EntityId = target.Id.ToString(),
            Detail =
                $"Active={target.IsActive}; Admin={target.IsAdmin}; RestrictProjects={target.RestrictProjects}; AllowedProjects={request.AllowedProjects.Count}",
            CorrelationId = HttpContext.TraceIdentifier
        });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("audit")]
    public async Task<ActionResult<object>> GetAudit(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var events = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(take)
            .Select(item => new
            {
                item.Id,
                item.EventType,
                item.EntityType,
                item.EntityId,
                item.Level,
                item.Detail,
                item.CorrelationId,
                item.CreatedAtUtc,
                UserEmail = item.User != null ? item.User.Email : null
            })
            .ToArrayAsync(cancellationToken);
        return Ok(events);
    }
}
