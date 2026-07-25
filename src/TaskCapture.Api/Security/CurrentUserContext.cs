using System.Security.Claims;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Security;

public static class TaskCapturePolicies
{
    public const string User = "TaskCapture.User";
    public const string Admin = "TaskCapture.Admin";
    public const string AllowedClaim = "taskcapture:allowed";
}

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    string ClientKey { get; }
    string IdentityProvider { get; }
    string SubjectId { get; }
    string? Email { get; }
    string DisplayName { get; }
    bool IsAdmin { get; }
}

public sealed class CurrentUserContext(
    IHttpContextAccessor accessor,
    IOptions<AccessOptions> accessOptions) : ICurrentUserContext
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public string IdentityProvider =>
        Principal.FindFirstValue("idp")
        ?? (Principal.HasClaim(claim => claim.Type == "tid") ? "MicrosoftEntra" : "Development");

    public string SubjectId =>
        Principal.FindFirstValue("oid")
        ?? Principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal.FindFirstValue("sub")
        ?? throw new InvalidOperationException("ログイン利用者の識別情報を取得できません。");

    public string? Email =>
        Principal.FindFirstValue(ClaimTypes.Email)
        ?? Principal.FindFirstValue("preferred_username")
        ?? Principal.FindFirstValue("email")
        ?? Principal.FindFirstValue("upn");

    public string DisplayName =>
        Principal.FindFirstValue(ClaimTypes.Name)
        ?? Principal.FindFirstValue("name")
        ?? Email
        ?? "利用者";

    public bool IsAdmin =>
        Principal.IsInRole(TaskCapturePolicies.Admin)
        || (!string.IsNullOrWhiteSpace(Email)
            && accessOptions.Value.AdminEmails.Contains(Email, StringComparer.OrdinalIgnoreCase));

    public string ClientKey
    {
        get
        {
            if (!IsAuthenticated)
            {
                throw new InvalidOperationException("ログインが必要です。");
            }

            return UserIdentityKey.Create(IdentityProvider, SubjectId);
        }
    }
}
