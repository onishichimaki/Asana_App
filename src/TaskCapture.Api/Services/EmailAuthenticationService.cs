using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;
using TaskCapture.Api.Infrastructure;
using TaskCapture.Api.Options;
using TaskCapture.Api.Security;

namespace TaskCapture.Api.Services;

public sealed class EmailAuthenticationService(
    TaskCaptureDbContext db,
    IAccountEmailSender emailSender,
    IOptions<AccessOptions> access,
    TimeProvider timeProvider,
    IHostEnvironment environment)
{
    private const int HashIterations = 120_000;

    public async Task<RequestLoginCodeResponse> RequestCodeAsync(
        string email,
        string requestFingerprint,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeAndValidateEmail(email);
        await EnsurePreRegisteredAsync(normalizedEmail, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var latest = await db.EmailLoginCodes
            .Where(item => item.Email == normalizedEmail)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null && now - latest.CreatedAtUtc < TimeSpan.FromMinutes(1))
        {
            throw new TooManyRequestsException("確認コードは1分待ってから再送できます。");
        }

        var hourlyCount = await db.EmailLoginCodes.CountAsync(
            item => item.Email == normalizedEmail
                && item.CreatedAtUtc >= now.AddHours(-1),
            cancellationToken);
        if (hourlyCount >= 5)
        {
            throw new TooManyRequestsException("確認コードの送信回数が上限に達しました。1時間後にお試しください。");
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
            .ToString("D6", CultureInfo.InvariantCulture);
        var lifetimeMinutes = Math.Clamp(access.Value.EmailCode.CodeLifetimeMinutes, 5, 30);
        await emailSender.SendLoginCodeAsync(
            normalizedEmail,
            code,
            lifetimeMinutes,
            cancellationToken);

        var loginCode = new EmailLoginCode
        {
            Email = normalizedEmail,
            CodeHash = HashCode(normalizedEmail, code),
            RequestFingerprint = SafeFingerprint(requestFingerprint),
            ExpiresAtUtc = now.AddMinutes(lifetimeMinutes),
            CreatedAtUtc = now
        };
        db.EmailLoginCodes.Add(loginCode);
        db.AuditLogs.Add(new AuditLog
        {
            EventType = "LoginCodeRequested",
            EntityType = "EmailLoginCode",
            EntityId = loginCode.Id.ToString(),
            Detail = $"送信先={MaskEmail(normalizedEmail)}",
            CorrelationId = correlationId,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);

        var revealDevelopmentCode =
            (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            && access.Value.EmailCode.Delivery.Mode.Equals("Mock", StringComparison.OrdinalIgnoreCase);
        return new RequestLoginCodeResponse(
            MaskEmail(normalizedEmail),
            lifetimeMinutes,
            revealDevelopmentCode ? code : null);
    }

    public async Task<AuthenticatedUserResponse> VerifyCodeAsync(
        string email,
        string code,
        string userAgent,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeAndValidateEmail(email);
        var now = timeProvider.GetUtcNow();
        var loginCode = await db.EmailLoginCodes
            .Where(item => item.Email == normalizedEmail && item.UsedAtUtc == null)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException("確認コードが見つかりません。新しいコードを送信してください。");

        var maximumAttempts = Math.Clamp(access.Value.EmailCode.MaximumAttempts, 3, 10);
        if (loginCode.ExpiresAtUtc < now)
        {
            throw new ValidationException("確認コードの期限が切れました。新しいコードを送信してください。");
        }
        if (loginCode.FailedAttempts >= maximumAttempts)
        {
            throw new ValidationException("確認回数の上限に達しました。新しいコードを送信してください。");
        }
        if (!VerifyCodeHash(loginCode.CodeHash, normalizedEmail, code))
        {
            loginCode.FailedAttempts++;
            await db.SaveChangesAsync(cancellationToken);
            throw new ValidationException("確認コードが正しくありません。");
        }

        loginCode.UsedAtUtc = now;
        var clientKey = UserIdentityKey.Create("Email", normalizedEmail);
        var user = await db.Users.SingleOrDefaultAsync(
            item => item.ClientKey == clientKey,
            cancellationToken);
        if (user is null)
        {
            if (!IsAdmin(normalizedEmail))
            {
                throw new UnauthorizedAccessException(
                    "このメールは利用登録されていません。管理者に連絡してください。");
            }
            user = new User
            {
                ClientKey = clientKey,
                IdentityProvider = "Email",
                SubjectId = normalizedEmail,
                Email = normalizedEmail,
                DisplayName = normalizedEmail.Split('@')[0],
                IsAdmin = IsAdmin(normalizedEmail),
                IsActive = true,
                LastLoginAtUtc = now,
                CreatedAtUtc = now
            };
            db.Users.Add(user);
        }
        else if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("この利用者はアプリの使用を停止されています。");
        }
        else
        {
            user.Email = normalizedEmail;
            user.SubjectId = normalizedEmail;
            user.IdentityProvider = "Email";
            // UIで付与した管理者権限を再ログイン時にも維持する。
            // 設定ファイルのAdminEmailsは初期管理者の昇格だけに使用する。
            user.IsAdmin = user.IsAdmin || IsAdmin(normalizedEmail);
            user.LastLoginAtUtc = now;
        }

        var sessionDays = Math.Clamp(access.Value.EmailCode.SessionDays, 1, 30);
        var session = new UserSession
        {
            User = user,
            UserAgentHash = SafeFingerprint(userAgent),
            ExpiresAtUtc = now.AddDays(sessionDays),
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        };
        db.UserSessions.Add(session);
        db.AuditLogs.Add(new AuditLog
        {
            User = user,
            EventType = "LoginSucceeded",
            EntityType = "UserSession",
            EntityId = session.Id.ToString(),
            Detail = "メール確認コードでログインしました。",
            CorrelationId = correlationId,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return new AuthenticatedUserResponse(
            user.Id,
            session.Id,
            normalizedEmail,
            user.DisplayName,
            user.IsAdmin,
            session.ExpiresAtUtc);
    }

    public async Task<bool> RevokeSessionAsync(
        Guid sessionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var session = await db.UserSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId,
            cancellationToken);
        if (session is null) return false;

        if (session.RevokedAtUtc is not null)
        {
            return await HasAnotherActiveSessionAsync(
                session.UserId,
                session.Id,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        session.RevokedAtUtc = now;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = session.UserId,
            EventType = "Logout",
            EntityType = "UserSession",
            EntityId = session.Id.ToString(),
            Detail = "ログアウトしました。",
            CorrelationId = correlationId,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return await HasAnotherActiveSessionAsync(
            session.UserId,
            session.Id,
            now,
            cancellationToken);
    }

    private Task<bool> HasAnotherActiveSessionAsync(
        Guid userId,
        Guid currentSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.UserSessions.AnyAsync(
            item => item.UserId == userId
                && item.Id != currentSessionId
                && item.RevokedAtUtc == null
                && item.ExpiresAtUtc > now,
            cancellationToken);

    private string NormalizeAndValidateEmail(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(normalized))
        {
            throw new ValidationException("会社メールの形式を確認してください。");
        }

        var domain = normalized[(normalized.LastIndexOf('@') + 1)..];
        var allowedDomains = access.Value.AllowedEmailDomains
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

    private bool IsAdmin(string email) =>
        access.Value.AdminEmails.Contains(email, StringComparer.OrdinalIgnoreCase);

    private async Task EnsurePreRegisteredAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        if (IsAdmin(normalizedEmail)) return;

        var clientKey = UserIdentityKey.Create("Email", normalizedEmail);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.ClientKey == clientKey,
            cancellationToken);
        if (user is null)
        {
            throw new UnauthorizedAccessException(
                "このメールは利用登録されていません。管理者に連絡してください。");
        }
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException(
                "この利用者はアプリの使用を停止されています。");
        }
    }

    private static string HashCode(string email, string code)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes($"{email}:{code}"),
            salt,
            HashIterations,
            HashAlgorithmName.SHA256,
            32);
        return $"{HashIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyCodeHash(string encoded, string email, string code)
    {
        var parts = encoded.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes($"{email}:{code}"),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string SafeFingerprint(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string MaskEmail(string email)
    {
        var separator = email.IndexOf('@');
        if (separator <= 1) return $"***{email[separator..]}";
        return $"{email[0]}***{email[(separator - 1)..]}";
    }
}
