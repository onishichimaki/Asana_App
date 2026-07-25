using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Security;

public sealed class AccessClaimsTransformation(
    IOptions<AccessOptions> options,
    TaskCaptureDbContext db) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return principal;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue("email")
            ?? principal.FindFirstValue("upn");
        var configured = options.Value;
        var provider = principal.FindFirstValue("idp") ?? "Development";
        var subject = principal.FindFirstValue("oid")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        var clientKey = string.IsNullOrWhiteSpace(subject)
            ? null
            : UserIdentityKey.Create(provider, subject);
        var persisted = clientKey is null
            ? null
            : await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(user => user.ClientKey == clientKey);
        var allowedByDomain = configured.AllowedEmailDomains.Length == 0
            || (!string.IsNullOrWhiteSpace(email)
                && configured.AllowedEmailDomains.Any(domain =>
                    email.EndsWith($"@{domain.Trim().TrimStart('@')}", StringComparison.OrdinalIgnoreCase)));
        var allowed = allowedByDomain && (persisted?.IsActive ?? true);

        if (allowed && !principal.HasClaim(TaskCapturePolicies.AllowedClaim, "true"))
        {
            identity.AddClaim(new Claim(TaskCapturePolicies.AllowedClaim, "true"));
        }
        if (!allowed)
        {
            foreach (var claim in identity.FindAll(TaskCapturePolicies.AllowedClaim).ToArray())
            {
                identity.RemoveClaim(claim);
            }
        }

        var isAdmin = (provider.Equals("Development", StringComparison.OrdinalIgnoreCase)
                && principal.IsInRole(TaskCapturePolicies.Admin))
            || persisted?.IsAdmin == true
            || (!string.IsNullOrWhiteSpace(email)
            && configured.AdminEmails.Contains(email, StringComparer.OrdinalIgnoreCase)
            );
        foreach (var claim in identity.FindAll(ClaimTypes.Role)
                     .Where(claim => claim.Value == TaskCapturePolicies.Admin)
                     .ToArray())
        {
            identity.RemoveClaim(claim);
        }
        if (isAdmin)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, TaskCapturePolicies.Admin));
        }

        return principal;
    }
}
