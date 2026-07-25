using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;
using TaskCapture.Api.Security;

namespace TaskCapture.Api.Services;

public sealed record AsanaConnectionStatus(
    string CredentialMode,
    bool Connected,
    string? AsanaUserName,
    string? AsanaUserEmail,
    string? WorkspaceName,
    DateTimeOffset? ConnectedAtUtc,
    string? Message);

public sealed class AsanaEmailMismatchException : InvalidOperationException
{
    public AsanaEmailMismatchException()
        : base("会社メールと同じメールアドレスのAsanaアカウントで接続してください。")
    {
    }
}

public interface IAsanaAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public sealed class AsanaConnectionService(
    TaskCaptureDbContext db,
    ICurrentUserContext currentUser,
    IOptions<AsanaOptions> options,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider) : IAsanaAccessTokenProvider
{
    private readonly AsanaOptions _options = options.Value;
    private readonly IDataProtector _tokenProtector =
        dataProtection.CreateProtector("TaskCapture.AsanaOAuth.Tokens.v1");
    private readonly IDataProtector _stateProtector =
        dataProtection.CreateProtector("TaskCapture.AsanaOAuth.State.v1");

    public async Task<AsanaConnectionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            return new("Mock", true, "モック利用者", currentUser.Email, "モック環境", null, "Asanaへは送信しない確認モードです。");
        }

        if (!_options.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                "PersonalAccessToken",
                !string.IsNullOrWhiteSpace(_options.PersonalAccessToken),
                null,
                null,
                null,
                null,
                "管理者が設定したAsana接続を利用します。");
        }

        var connection = await FindConnectionAsync(cancellationToken);
        var policyMatches = connection is not null && ConnectionMatchesPolicy(connection);
        return connection is null
            ? new("PerUserOAuth", false, null, null, null, null, "Asanaへの接続が必要です。同じ会社メールのAsanaアカウントを使用してください。")
            : new(
                "PerUserOAuth",
                policyMatches,
                connection.AsanaUserName,
                connection.AsanaUserEmail,
                connection.WorkspaceName,
                connection.ConnectedAtUtc,
                policyMatches ? null : "会社メールと社内ワークスペースを確認するため、Asanaへ接続し直してください。");
    }

    public string BuildAuthorizationUri()
    {
        EnsureOAuthConfigured();
        var state = new OAuthState(
            currentUser.ClientKey,
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow().AddMinutes(10));
        var protectedState = _stateProtector.Protect(JsonSerializer.Serialize(state));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.OAuth.ClientId,
            ["redirect_uri"] = _options.OAuth.RedirectUri,
            ["response_type"] = "code",
            ["state"] = protectedState,
            ["scope"] = string.Join(' ', _options.OAuth.Scopes.Where(value => !string.IsNullOrWhiteSpace(value)))
        };
        return "https://app.asana.com/-/oauth_authorize?" + string.Join(
            '&',
            query.Select(item =>
                $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? string.Empty)}"));
    }

    public async Task ConnectAsync(
        string code,
        string state,
        string correlationId,
        CancellationToken cancellationToken)
    {
        EnsureOAuthConfigured();
        OAuthState parsedState;
        try
        {
            parsedState = JsonSerializer.Deserialize<OAuthState>(_stateProtector.Unprotect(state))
                ?? throw new InvalidOperationException();
        }
        catch
        {
            throw new InvalidOperationException("Asana接続の確認情報が正しくありません。最初からやり直してください。");
        }

        if (parsedState.ExpiresAtUtc < timeProvider.GetUtcNow()
            || !string.Equals(parsedState.ClientKey, currentUser.ClientKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Asana接続の有効時間が切れました。最初からやり直してください。");
        }

        var token = await ExchangeTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = _options.OAuth.ClientId!,
                ["client_secret"] = _options.OAuth.ClientSecret!,
                ["redirect_uri"] = _options.OAuth.RedirectUri!,
                ["code"] = code
            },
            cancellationToken);
        var profile = await GetProfileAsync(token.AccessToken, cancellationToken);
        var user = await GetCurrentUserAsync(cancellationToken);
        if (_options.OAuth.RequireMatchingEmail
            && !EmailsMatch(user.Email ?? currentUser.Email, profile.UserEmail))
        {
            db.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id,
                EventType = "AsanaConnectionRejected",
                EntityType = nameof(AsanaConnection),
                EntityId = profile.UserGid,
                Level = "Warning",
                Detail = "The Asana account email did not match the signed-in company email.",
                CorrelationId = correlationId
            });
            await db.SaveChangesAsync(cancellationToken);
            throw new AsanaEmailMismatchException();
        }

        var connection = await db.AsanaConnections
            .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (connection is null)
        {
            connection = new AsanaConnection { UserId = user.Id };
            db.AsanaConnections.Add(connection);
        }

        connection.AsanaUserGid = profile.UserGid;
        connection.AsanaUserName = profile.UserName;
        connection.AsanaUserEmail = NormalizeEmail(profile.UserEmail);
        connection.WorkspaceGid = profile.WorkspaceGid;
        connection.WorkspaceName = profile.WorkspaceName;
        connection.ProtectedAccessToken = _tokenProtector.Protect(token.AccessToken);
        connection.ProtectedRefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken)
            ? connection.ProtectedRefreshToken
            : _tokenProtector.Protect(token.RefreshToken);
        connection.GrantedScopes = string.Join(' ', _options.OAuth.Scopes);
        connection.TokenExpiresAtUtc = token.ExpiresInSeconds is > 0
            ? timeProvider.GetUtcNow().AddSeconds(token.ExpiresInSeconds.Value)
            : null;
        connection.ConnectedAtUtc = timeProvider.GetUtcNow();
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();
        connection.RevokedAtUtc = null;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            EventType = "AsanaConnected",
            EntityType = nameof(AsanaConnection),
            EntityId = connection.Id.ToString(),
            Detail = $"Asana user {profile.UserGid} connected.",
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DisconnectAsync(string correlationId, CancellationToken cancellationToken)
    {
        var connection = await FindConnectionAsync(cancellationToken);
        if (connection is null) return;
        var providerRevoked = await TryRevokeAtProviderAsync(connection, cancellationToken);
        ClearConnection(connection);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = connection.UserId,
            EventType = "AsanaDisconnected",
            EntityType = nameof(AsanaConnection),
            EntityId = connection.Id.ToString(),
            Level = providerRevoked ? "Information" : "Warning",
            Detail = providerRevoked
                ? "The Asana authorization was revoked and local tokens were cleared."
                : "Local Asana tokens were cleared, but provider revocation could not be confirmed.",
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeForUserAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var connection = await db.AsanaConnections.SingleOrDefaultAsync(
            item => item.UserId == userId && item.RevokedAtUtc == null,
            cancellationToken);
        if (connection is null) return;

        var providerRevoked = await TryRevokeAtProviderAsync(connection, cancellationToken);
        ClearConnection(connection);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            EventType = "AsanaDisconnectedByAdministrator",
            EntityType = nameof(AsanaConnection),
            EntityId = connection.Id.ToString(),
            Level = providerRevoked ? "Information" : "Warning",
            Detail = providerRevoked
                ? "The Asana authorization was revoked during user deactivation."
                : "Local Asana tokens were cleared during user deactivation; provider revocation could not be confirmed.",
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!_options.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(_options.PersonalAccessToken)
                ? _options.PersonalAccessToken
                : throw new InvalidOperationException(
                    "Asana APIキーがサーバーに設定されていません。");
        }

        var connection = await FindConnectionAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "Asanaへ接続してから登録してください。画面右上のアカウントから接続できます。");
        if (!ConnectionEmailMatches(connection))
        {
            throw new AsanaEmailMismatchException();
        }
        if (!ConnectionWorkspaceMatches(connection))
        {
            throw new InvalidOperationException(
                "同じ会社メールで社内ワークスペースを利用できるAsanaへ接続し直してください。");
        }
        if (connection.TokenExpiresAtUtc is null
            || connection.TokenExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(2))
        {
            return UnprotectToken(connection.ProtectedAccessToken);
        }

        if (string.IsNullOrWhiteSpace(connection.ProtectedRefreshToken))
        {
            throw new InvalidOperationException("Asana接続の期限が切れました。接続し直してください。");
        }

        EnsureOAuthConfigured();
        var refreshed = await ExchangeTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.OAuth.ClientId!,
                ["client_secret"] = _options.OAuth.ClientSecret!,
                ["refresh_token"] = UnprotectToken(connection.ProtectedRefreshToken)
            },
            cancellationToken);
        connection.ProtectedAccessToken = _tokenProtector.Protect(refreshed.AccessToken);
        if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
        {
            connection.ProtectedRefreshToken = _tokenProtector.Protect(refreshed.RefreshToken);
        }
        connection.TokenExpiresAtUtc = refreshed.ExpiresInSeconds is > 0
            ? timeProvider.GetUtcNow().AddSeconds(refreshed.ExpiresInSeconds.Value)
            : null;
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return refreshed.AccessToken;
    }

    private async Task<AsanaConnection?> FindConnectionAsync(CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(
            item => item.ClientKey == currentUser.ClientKey,
            cancellationToken);
        if (user is null) return null;
        return await db.AsanaConnections.SingleOrDefaultAsync(
            item => item.UserId == user.Id && item.RevokedAtUtc == null,
            cancellationToken);
    }

    private async Task<User> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        await db.Users.SingleAsync(item => item.ClientKey == currentUser.ClientKey, cancellationToken);

    private async Task<OAuthToken> ExchangeTokenAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AsanaOAuth");
        using var response = await client.PostAsync(
            "https://app.asana.com/-/oauth_token",
            new FormUrlEncodedContent(form),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Asanaの認証情報を取得できませんでした。接続をやり直してください。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access)
            ? access.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Asanaからアクセストークンが返りませんでした。");
        }
        return new OAuthToken(
            accessToken,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
                ? seconds
                : null);
    }

    private async Task<bool> TryRevokeAtProviderAsync(
        AsanaConnection connection,
        CancellationToken cancellationToken)
    {
        if (!_options.Mode.Equals("Api", StringComparison.OrdinalIgnoreCase)
            || !_options.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(connection.ProtectedRefreshToken))
        {
            return true;
        }

        try
        {
            EnsureOAuthConfigured();
            var client = httpClientFactory.CreateClient("AsanaOAuth");
            using var response = await client.PostAsync(
                "https://app.asana.com/-/oauth_revoke",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.OAuth.ClientId!,
                    ["client_secret"] = _options.OAuth.ClientSecret!,
                    ["token"] = UnprotectToken(connection.ProtectedRefreshToken)
                }),
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void ClearConnection(AsanaConnection connection)
    {
        connection.RevokedAtUtc = timeProvider.GetUtcNow();
        connection.ProtectedAccessToken = string.Empty;
        connection.ProtectedRefreshToken = null;
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();
    }

    private async Task<AsanaProfile> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AsanaOAuth");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://app.asana.com/api/1.0/users/me?opt_fields=gid,name,email,workspaces.gid,workspaces.name");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var data = document.RootElement.GetProperty("data");
        var workspaceItems = data.TryGetProperty("workspaces", out var workspaces)
            ? workspaces.EnumerateArray().ToArray()
            : [];
        var workspace = string.IsNullOrWhiteSpace(_options.DefaultWorkspaceGid)
            ? workspaceItems.FirstOrDefault()
            : workspaceItems.FirstOrDefault(item =>
                item.GetProperty("gid").GetString() == _options.DefaultWorkspaceGid);
        if (!string.IsNullOrWhiteSpace(_options.DefaultWorkspaceGid)
            && workspace.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "指定された社内Asanaワークスペースを利用できるアカウントで接続してください。");
        }
        return new AsanaProfile(
            data.GetProperty("gid").GetString() ?? string.Empty,
            data.GetProperty("name").GetString() ?? "Asana利用者",
            data.TryGetProperty("email", out var email) ? email.GetString() : null,
            workspace.ValueKind == JsonValueKind.Object ? workspace.GetProperty("gid").GetString() : null,
            workspace.ValueKind == JsonValueKind.Object ? workspace.GetProperty("name").GetString() : null);
    }

    private bool ConnectionMatchesPolicy(AsanaConnection connection) =>
        ConnectionEmailMatches(connection) && ConnectionWorkspaceMatches(connection);

    private bool ConnectionEmailMatches(AsanaConnection connection) =>
        !_options.OAuth.RequireMatchingEmail
        || EmailsMatch(currentUser.Email, connection.AsanaUserEmail);

    private bool ConnectionWorkspaceMatches(AsanaConnection connection) =>
        string.IsNullOrWhiteSpace(_options.DefaultWorkspaceGid)
            || string.Equals(
                connection.WorkspaceGid,
                _options.DefaultWorkspaceGid,
                StringComparison.Ordinal);

    private static bool EmailsMatch(string? companyEmail, string? asanaEmail)
    {
        var normalizedCompanyEmail = NormalizeEmail(companyEmail);
        var normalizedAsanaEmail = NormalizeEmail(asanaEmail);
        return normalizedCompanyEmail is not null
            && normalizedAsanaEmail is not null
            && string.Equals(normalizedCompanyEmail, normalizedAsanaEmail, StringComparison.Ordinal);
    }

    private static string? NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private void EnsureOAuthConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.OAuth.ClientId)
            || string.IsNullOrWhiteSpace(_options.OAuth.ClientSecret)
            || string.IsNullOrWhiteSpace(_options.OAuth.RedirectUri))
        {
            throw new InvalidOperationException(
                "Asana OAuthのClientId、ClientSecret、RedirectUriをサーバーへ設定してください。");
        }
    }

    private string UnprotectToken(string value)
    {
        try
        {
            return _tokenProtector.Unprotect(value);
        }
        catch
        {
            throw new InvalidOperationException(
                "保存済みのAsana接続を復号できません。Asanaへ接続し直してください。");
        }
    }

    private sealed record OAuthState(string ClientKey, string Nonce, DateTimeOffset ExpiresAtUtc);
    private sealed record OAuthToken(string AccessToken, string? RefreshToken, int? ExpiresInSeconds);
    private sealed record AsanaProfile(
        string UserGid,
        string UserName,
        string? UserEmail,
        string? WorkspaceGid,
        string? WorkspaceName);
}
