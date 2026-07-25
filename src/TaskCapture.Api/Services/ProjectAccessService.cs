using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Data;
using TaskCapture.Api.Security;

namespace TaskCapture.Api.Services;

public sealed class ProjectAccessService(
    TaskCaptureDbContext db,
    ICurrentUserContext currentUser)
{
    public async Task EnsureAllowedAsync(
        string? projectGid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectGid)) return;
        var user = await db.Users.SingleAsync(
            item => item.ClientKey == currentUser.ClientKey,
            cancellationToken);
        if (!user.RestrictProjects) return;
        var allowed = await db.UserProjectPreferences.AnyAsync(
            item => item.UserId == user.Id
                && item.ProjectGid == projectGid
                && item.IsAllowed,
            cancellationToken);
        if (!allowed)
        {
            throw new UnauthorizedAccessException(
                "このAsanaプロジェクトは利用を許可されていません。");
        }
    }
}
