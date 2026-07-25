using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Security;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AccessOptions> accessOptions,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "TaskCapture.Development";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "Development authentication is allowed only in Development or Testing."));
        }

        var configured = accessOptions.Value;
        var email = configured.LocalEmail.Trim().ToLowerInvariant();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, email),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, configured.LocalDisplayName),
            new("idp", "Development"),
            new(TaskCapturePolicies.AllowedClaim, "true")
        };

        if (configured.AdminEmails.Length == 0
            || configured.AdminEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, TaskCapturePolicies.Admin));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
