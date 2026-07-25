using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Security;

public sealed class AsanaConnectionRequirement : IAuthorizationRequirement;

public sealed class AsanaConnectionAuthorizationHandler(
    TaskCaptureDbContext db,
    ICurrentUserContext currentUser,
    IOptions<AsanaOptions> options) : AuthorizationHandler<AsanaConnectionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AsanaConnectionRequirement requirement)
    {
        var configured = options.Value;
        if (configured.Mode.Equals("Mock", StringComparison.OrdinalIgnoreCase)
            || !configured.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return;
        }

        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.Email))
        {
            return;
        }

        var normalizedEmail = currentUser.Email.Trim().ToLowerInvariant();
        var connected = await db.Users
            .AsNoTracking()
            .Where(user => user.ClientKey == currentUser.ClientKey && user.IsActive)
            .AnyAsync(user => user.AsanaConnection != null
                && user.AsanaConnection.RevokedAtUtc == null
                && user.AsanaConnection.ProtectedAccessToken != string.Empty
                && (!configured.OAuth.RequireMatchingEmail
                    || user.AsanaConnection.AsanaUserEmail == normalizedEmail)
                && (string.IsNullOrWhiteSpace(configured.DefaultWorkspaceGid)
                    || user.AsanaConnection.WorkspaceGid == configured.DefaultWorkspaceGid));
        if (connected)
        {
            context.Succeed(requirement);
        }
    }
}
