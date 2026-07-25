using System.ComponentModel.DataAnnotations;

namespace TaskCapture.Api.Contracts;

public sealed class RequestLoginCodeRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;
}

public sealed class VerifyLoginCodeRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "確認コードは6桁の数字です。")]
    public string Code { get; set; } = string.Empty;
}

public sealed record RequestLoginCodeResponse(
    string MaskedEmail,
    int ExpiresInMinutes,
    string? DevelopmentCode);

public sealed record AuthenticatedUserResponse(
    Guid UserId,
    Guid SessionId,
    string Email,
    string DisplayName,
    bool IsAdmin,
    DateTimeOffset ExpiresAtUtc);
