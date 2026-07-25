using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;
using TaskCapture.Api.Security;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Authorize(Policy = TaskCapturePolicies.Admin)]
[Route("api/admin")]
public sealed class AdminController(
    TaskCaptureDbContext db,
    ICurrentUserContext currentUser,
    IOptions<AsanaOptions> asanaOptions,
    IOptions<AccessOptions> accessOptions,
    AsanaConnectionService asanaConnectionService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<object>> GetUsers(CancellationToken cancellationToken)
    {
        var requireMatchingEmail = asanaOptions.Value.OAuth.RequireMatchingEmail;
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
                    && user.AsanaConnection.RevokedAtUtc == null
                    && (!requireMatchingEmail
                        || (user.Email != null
                            && user.AsanaConnection.AsanaUserEmail != null
                            && user.Email == user.AsanaConnection.AsanaUserEmail)),
                AllowedProjects = user.ProjectPreferences
                    .Where(item => item.IsAllowed)
                    .Select(item => new { item.ProjectGid, item.ProjectName })
            })
            .ToArrayAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<ActionResult<object>> PreRegisterUser(
        [FromBody] PreRegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeCompanyEmail(request.Email);
        var clientKey = UserIdentityKey.Create("Email", email);
        if (await db.Users.AnyAsync(
                user => user.ClientKey == clientKey || user.Email == email,
                cancellationToken))
        {
            return Conflict(new ProblemDetails
            {
                Title = "このメールはすでに利用登録されています。",
                Status = StatusCodes.Status409Conflict
            });
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? email.Split('@')[0]
            : request.DisplayName.Trim();
        var user = new User
        {
            ClientKey = clientKey,
            IdentityProvider = "Email",
            SubjectId = email,
            Email = email,
            DisplayName = displayName,
            IsActive = true,
            IsAdmin = request.IsAdmin,
            RestrictProjects = false,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        db.Users.Add(user);
        var actor = await db.Users.SingleAsync(
            item => item.ClientKey == currentUser.ClientKey,
            cancellationToken);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.Id,
            EventType = "UserPreRegistered",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            Detail = $"Registered user {user.Id}; Admin={user.IsAdmin}.",
            CorrelationId = HttpContext.TraceIdentifier,
            CreatedAtUtc = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/admin/users/{user.Id}", new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.IsActive,
            user.IsAdmin
        });
    }

    [HttpPut("users/{userId:guid}/access")]
    public async Task<IActionResult> UpdateUserAccess(
        Guid userId,
        [FromBody] UpdateUserAccessRequest request,
        CancellationToken cancellationToken)
    {
        var target = await db.Users
            .Include(user => user.ProjectPreferences)
            .Include(user => user.Sessions)
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

        var isBeingDeactivated = target.IsActive && !request.IsActive;
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

        if (isBeingDeactivated)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var session in target.Sessions.Where(session => session.RevokedAtUtc is null))
            {
                session.RevokedAtUtc = now;
            }
            await asanaConnectionService.RevokeForUserAsync(
                target.Id,
                HttpContext.TraceIdentifier,
                cancellationToken);
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

    private string NormalizeCompanyEmail(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(normalized))
        {
            throw new ValidationException("会社メールの形式を確認してください。");
        }

        var domain = normalized[(normalized.LastIndexOf('@') + 1)..];
        var allowedDomains = accessOptions.Value.AllowedEmailDomains
            .Select(item => item.Trim().TrimStart('@').ToLowerInvariant())
            .Where(item => item.Length > 0)
            .ToArray();
        if (allowedDomains.Length == 0
            || !allowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException("登録できる会社メールではありません。");
        }
        return normalized;
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
