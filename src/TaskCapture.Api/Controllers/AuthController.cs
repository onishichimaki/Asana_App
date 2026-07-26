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
    IOptions<AsanaOptions> asana,
    IHostEnvironment environment) : ControllerBase
{
    private const string AsanaCorrelationCookie = "TaskCapture.AsanaLogin";
    private const string AsanaLauncherCookie = "TaskCapture.AsanaLoginLauncher";

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
        await SignInAsync(result);

        return Ok(new
        {
            authenticated = true,
            email = result.Email,
            displayName = result.DisplayName,
            isAdmin = result.IsAdmin
        });
    }

    [AllowAnonymous]
    [HttpGet("asana/start")]
    public IActionResult StartAsanaLogin([FromQuery] bool launcher = false)
    {
        EnsureAsanaLoginMode();
        var login = asanaConnectionService.BuildLoginAuthorizationUri();
        Response.Cookies.Append(
            AsanaCorrelationCookie,
            login.Correlation,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = UseSecureLoginCookie(),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/api/auth/asana",
                Expires = login.ExpiresAtUtc
            });
        if (launcher)
        {
            Response.Cookies.Append(
                AsanaLauncherCookie,
                "1",
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = UseSecureLoginCookie(),
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Path = "/api/auth/asana",
                    Expires = login.ExpiresAtUtc
                });
        }
        return Redirect(login.AuthorizationUrl);
    }

    [AllowAnonymous]
    [HttpGet("asana/callback")]
    public async Task<IActionResult> CompleteAsanaLogin(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        EnsureAsanaLoginMode();
        var correlation = Request.Cookies[AsanaCorrelationCookie];
        var launcher = Request.Cookies[AsanaLauncherCookie] == "1";
        Response.Cookies.Delete(
            AsanaCorrelationCookie,
            new CookieOptions
            {
                Secure = UseSecureLoginCookie(),
                SameSite = SameSiteMode.Lax,
                Path = "/api/auth/asana"
            });
        Response.Cookies.Delete(
            AsanaLauncherCookie,
            new CookieOptions
            {
                Secure = UseSecureLoginCookie(),
                SameSite = SameSiteMode.Lax,
                Path = "/api/auth/asana"
            });
        if (!string.IsNullOrWhiteSpace(error))
        {
            return LoginRedirect("cancelled", launcher);
        }
        if (string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(correlation)
            || code.Length > 2_048
            || state.Length > 8_000
            || correlation.Length > 256)
        {
            return LoginRedirect("expired", launcher);
        }

        try
        {
            var result = await asanaConnectionService.LoginAsync(
                code,
                state,
                correlation,
                Request.Headers.UserAgent.ToString(),
                HttpContext.TraceIdentifier,
                cancellationToken);
            await SignInAsync(result);
            return LoginRedirect("connected", launcher);
        }
        catch (AsanaWorkspaceDeniedException)
        {
            return LoginRedirect("wrong-workspace", launcher);
        }
        catch (AsanaLoginException exception)
        {
            return LoginRedirect(exception.Reason, launcher);
        }
        catch
        {
            return LoginRedirect("error", launcher);
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (access.Value.Mode.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new { message = "開発用ログインはアプリの再起動まで有効です。" });
        }

        var hasAnotherActiveSession = false;
        var sessionText = User.FindFirstValue(EmailAuthenticationDefaults.SessionClaim);
        if (Guid.TryParse(sessionText, out var sessionId))
        {
            hasAnotherActiveSession = await emailAuthentication.RevokeSessionAsync(
                sessionId,
                HttpContext.TraceIdentifier,
                cancellationToken);
        }

        if (access.Value.Mode.Equals("AsanaOAuth", StringComparison.OrdinalIgnoreCase)
            && !hasAnotherActiveSession)
        {
            await asanaConnectionService.DisconnectAsync(
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

    private void EnsureAsanaLoginMode()
    {
        if (!access.Value.Mode.Equals("AsanaOAuth", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Asanaログインは現在使用していません。");
        }
    }

    private async Task SignInAsync(AuthenticatedUserResponse result)
    {
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
    }

    private RedirectResult LoginRedirect(string result, bool launcher) =>
        Redirect(launcher
            ? $"/?launcher=1&login={Uri.EscapeDataString(result)}"
            : $"/?login={Uri.EscapeDataString(result)}");

    private bool UseSecureLoginCookie() =>
        Request.IsHttps
        || (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"));
}
