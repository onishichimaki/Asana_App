using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Options;
using TaskCapture.Api.Security;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    ICurrentUserContext currentUser,
    EmailAuthenticationService emailAuthentication,
    AsanaConnectionService asanaConnectionService,
    IOptions<AccessOptions> access,
    IOptions<AsanaOptions> asana) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var connectionRequired = asana.Value.CredentialMode.Equals(
            "PerUserOAuth",
            StringComparison.OrdinalIgnoreCase);
        var connected = !connectionRequired;
        if (currentUser.IsAuthenticated && connectionRequired)
        {
            connected = (await asanaConnectionService.GetStatusAsync(cancellationToken)).Connected;
        }

        return Ok(new
        {
            authenticated = currentUser.IsAuthenticated,
            mode = access.Value.Mode,
            email = currentUser.IsAuthenticated ? currentUser.Email : null,
            displayName = currentUser.IsAuthenticated ? currentUser.DisplayName : null,
            isAdmin = currentUser.IsAuthenticated && currentUser.IsAdmin,
            allowedEmailDomains = access.Value.AllowedEmailDomains,
            asanaCredentialMode = asana.Value.CredentialMode,
            asanaConnectionRequired = connectionRequired,
            asanaConnected = connected
        });
    }

    [AllowAnonymous]
    [HttpPost("request-code")]
    public async Task<ActionResult<RequestLoginCodeResponse>> RequestCode(
        [FromBody] RequestLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEmailMode();
        var fingerprint = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return Ok(await emailAuthentication.RequestCodeAsync(
            request.Email,
            fingerprint,
            HttpContext.TraceIdentifier,
            cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("verify-code")]
    public async Task<ActionResult<object>> VerifyCode(
        [FromBody] VerifyLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEmailMode();
        var result = await emailAuthentication.VerifyCodeAsync(
            request.Email,
            request.Code,
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier,
            cancellationToken);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.Email),
            new(ClaimTypes.Email, result.Email),
            new(ClaimTypes.Name, result.DisplayName),
            new("idp", "Email"),
            new(TaskCapturePolicies.AllowedClaim, "true"),
            new(EmailAuthenticationDefaults.SessionClaim, result.SessionId.ToString())
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, EmailAuthenticationDefaults.Scheme));
        await HttpContext.SignInAsync(
            EmailAuthenticationDefaults.Scheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = result.ExpiresAtUtc
            });

        return Ok(new
        {
            authenticated = true,
            email = result.Email,
            displayName = result.DisplayName,
            isAdmin = result.IsAdmin
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (access.Value.Mode.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new { message = "開発用ログインはアプリの再起動まで有効です。" });
        }

        var sessionText = User.FindFirstValue(EmailAuthenticationDefaults.SessionClaim);
        if (Guid.TryParse(sessionText, out var sessionId))
        {
            await emailAuthentication.RevokeSessionAsync(
                sessionId,
                HttpContext.TraceIdentifier,
                cancellationToken);
        }
        await HttpContext.SignOutAsync(EmailAuthenticationDefaults.Scheme);
        return Ok(new { message = "ログアウトしました。" });
    }

    private void EnsureEmailMode()
    {
        if (!access.Value.Mode.Equals("EmailCode", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("メール確認コードによるログインは現在使用していません。");
        }
    }
}
