using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Data;

namespace TaskCapture.Api.Security;

public sealed class CurrentUserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserContext currentUser,
        TaskCaptureDbContext db,
        TimeProvider timeProvider)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && currentUser.IsAuthenticated)
        {
            var now = timeProvider.GetUtcNow();
            var user = await db.Users.SingleOrDefaultAsync(
                item => item.ClientKey == currentUser.ClientKey,
                context.RequestAborted);

            if (user is null)
            {
                user = new User
                {
                    ClientKey = currentUser.ClientKey,
                    IdentityProvider = currentUser.IdentityProvider,
                    SubjectId = currentUser.SubjectId,
                    Email = currentUser.Email,
                    DisplayName = currentUser.DisplayName,
                    IsAdmin = currentUser.IsAdmin,
                    IsActive = true,
                    LastLoginAtUtc = now,
                    CreatedAtUtc = now
                };
                db.Users.Add(user);
                db.AuditLogs.Add(new AuditLog
                {
                    User = user,
                    EventType = "UserProvisioned",
                    EntityType = "User",
                    EntityId = user.Id.ToString(),
                    Detail = "ログイン利用者を登録しました。",
                    CorrelationId = context.TraceIdentifier,
                    CreatedAtUtc = now
                });
                await db.SaveChangesAsync(context.RequestAborted);
            }
            else
            {
                if (!user.IsActive)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(
                        new { detail = "この利用者はアプリの使用を停止されています。" },
                        context.RequestAborted);
                    return;
                }

                user.IdentityProvider = currentUser.IdentityProvider;
                user.SubjectId = currentUser.SubjectId;
                user.Email = currentUser.Email;
                user.DisplayName = currentUser.DisplayName;
                if (user.LastLoginAtUtc is null || now - user.LastLoginAtUtc > TimeSpan.FromMinutes(10))
                {
                    user.LastLoginAtUtc = now;
                    await db.SaveChangesAsync(context.RequestAborted);
                }
            }
        }

        await next(context);
    }
}
