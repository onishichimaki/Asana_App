using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;
using TaskCapture.Api.Security;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class AsanaConnectionServiceTests
{
    [Fact]
    public async Task ConnectAsync_AcceptsAndStoresMatchingCompanyEmail()
    {
        await using var fixture = CreateFixture("employee@example.co.jp", "Employee@Example.co.jp");

        var state = GetState(fixture.Service.BuildAuthorizationUri());
        await fixture.Service.ConnectAsync("authorization-code", state, "correlation", CancellationToken.None);

        var connection = await fixture.Db.AsanaConnections.SingleAsync();
        Assert.Equal("employee@example.co.jp", connection.AsanaUserEmail);
        Assert.NotEqual("access-token", connection.ProtectedAccessToken);
        var status = await fixture.Service.GetStatusAsync(CancellationToken.None);
        Assert.True(status.Connected);
        Assert.Equal("employee@example.co.jp", status.AsanaUserEmail);
        Assert.Null(status.Message);
    }

    [Fact]
    public async Task ConnectAsync_RejectsDifferentAsanaEmailWithoutSavingToken()
    {
        await using var fixture = CreateFixture("employee@example.co.jp", "other@example.co.jp");

        var state = GetState(fixture.Service.BuildAuthorizationUri());
        await Assert.ThrowsAsync<AsanaEmailMismatchException>(() =>
            fixture.Service.ConnectAsync("authorization-code", state, "correlation", CancellationToken.None));

        Assert.Empty(await fixture.Db.AsanaConnections.ToListAsync());
        var audit = await fixture.Db.AuditLogs.SingleAsync();
        Assert.Equal("AsanaConnectionRejected", audit.EventType);
        Assert.DoesNotContain("employee@example.co.jp", audit.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("other@example.co.jp", audit.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectAsync_RejectsAccountOutsideConfiguredCompanyWorkspace()
    {
        await using var fixture = CreateFixture(
            "employee@example.co.jp",
            "employee@example.co.jp",
            "different-workspace");

        var state = GetState(fixture.Service.BuildAuthorizationUri());
        var error = await Assert.ThrowsAsync<AsanaWorkspaceDeniedException>(() =>
            fixture.Service.ConnectAsync("authorization-code", state, "correlation", CancellationToken.None));

        Assert.Contains("社内Asanaワークスペース", error.Message);
        Assert.Empty(await fixture.Db.AsanaConnections.ToListAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_RequiresReconnectForLegacyConnectionWithoutEmail()
    {
        await using var fixture = CreateFixture("employee@example.co.jp", "employee@example.co.jp");
        fixture.Db.AsanaConnections.Add(new AsanaConnection
        {
            UserId = fixture.User.Id,
            AsanaUserGid = "asana-user",
            AsanaUserName = "Employee",
            ProtectedAccessToken = "legacy-protected-token",
            ConnectedAtUtc = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<AsanaEmailMismatchException>(() =>
            fixture.Service.GetAccessTokenAsync(CancellationToken.None));
        var status = await fixture.Service.GetStatusAsync(CancellationToken.None);
        Assert.False(status.Connected);
        Assert.Contains("接続し直して", status.Message);
    }

    [Fact]
    public async Task DisconnectAsync_RevokesProviderAuthorizationAndErasesLocalTokens()
    {
        await using var fixture = CreateFixture("employee@example.co.jp", "employee@example.co.jp");
        var state = GetState(fixture.Service.BuildAuthorizationUri());
        await fixture.Service.ConnectAsync("authorization-code", state, "connect", CancellationToken.None);

        await fixture.Service.DisconnectAsync("disconnect", CancellationToken.None);

        var connection = await fixture.Db.AsanaConnections.SingleAsync();
        Assert.NotNull(connection.RevokedAtUtc);
        Assert.Empty(connection.ProtectedAccessToken);
        Assert.Null(connection.ProtectedRefreshToken);
        Assert.True(fixture.Handler.RevokeRequested);
        Assert.Equal("refresh-token", fixture.Handler.RevokedToken);
        Assert.False((await fixture.Service.GetStatusAsync(CancellationToken.None)).Connected);
    }

    [Fact]
    public async Task LoginAsync_UsesPkceAndCreatesSessionForPreRegisteredUser()
    {
        await using var fixture = CreateFixture("employee@example.co.jp", "Employee@Example.co.jp");
        var start = fixture.Service.BuildLoginAuthorizationUri();
        var authorization = ParseQuery(start.AuthorizationUrl);

        var result = await fixture.Service.LoginAsync(
            "authorization-code",
            authorization["state"],
            start.Correlation,
            "test-browser",
            "login-correlation",
            CancellationToken.None);

        Assert.Equal("employee@example.co.jp", result.Email);
        Assert.Single(await fixture.Db.UserSessions.ToListAsync());
        Assert.Single(await fixture.Db.AsanaConnections.ToListAsync());
        Assert.Equal("S256", authorization["code_challenge_method"]);
        Assert.True(fixture.Handler.LastTokenForm.TryGetValue("code_verifier", out var verifier));
        Assert.Equal(CreateCodeChallenge(verifier!), authorization["code_challenge"]);
        Assert.Equal(
            "Asana OAuthでログインしました。",
            (await fixture.Db.AuditLogs.SingleAsync(item => item.EventType == "LoginSucceeded")).Detail);
    }

    [Fact]
    public async Task LoginAsync_RejectsUserWhoWasNotPreRegisteredAndRevokesToken()
    {
        await using var fixture = CreateFixture("employee@example.co.jp", "employee@example.co.jp");
        fixture.Db.Users.Remove(fixture.User);
        await fixture.Db.SaveChangesAsync();
        var start = fixture.Service.BuildLoginAuthorizationUri();

        var error = await Assert.ThrowsAsync<AsanaLoginException>(() =>
            fixture.Service.LoginAsync(
                "authorization-code",
                ParseQuery(start.AuthorizationUrl)["state"],
                start.Correlation,
                "test-browser",
                "login-correlation",
                CancellationToken.None));

        Assert.Equal("not-registered", error.Reason);
        Assert.Empty(await fixture.Db.UserSessions.ToListAsync());
        Assert.Empty(await fixture.Db.AsanaConnections.ToListAsync());
        Assert.True(fixture.Handler.RevokeRequested);
        Assert.DoesNotContain(
            "employee@example.co.jp",
            (await fixture.Db.AuditLogs.SingleAsync()).Detail ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_RejectsWrongWorkspaceAndInvalidCorrelation()
    {
        await using var wrongWorkspace = CreateFixture(
            "employee@example.co.jp",
            "employee@example.co.jp",
            "different-workspace");
        var workspaceStart = wrongWorkspace.Service.BuildLoginAuthorizationUri();
        await Assert.ThrowsAsync<AsanaWorkspaceDeniedException>(() =>
            wrongWorkspace.Service.LoginAsync(
                "authorization-code",
                ParseQuery(workspaceStart.AuthorizationUrl)["state"],
                workspaceStart.Correlation,
                "test-browser",
                "login-correlation",
                CancellationToken.None));
        Assert.True(wrongWorkspace.Handler.RevokeRequested);

        await using var badCorrelation = CreateFixture(
            "employee@example.co.jp",
            "employee@example.co.jp");
        var correlationStart = badCorrelation.Service.BuildLoginAuthorizationUri();
        var error = await Assert.ThrowsAsync<AsanaLoginException>(() =>
            badCorrelation.Service.LoginAsync(
                "authorization-code",
                ParseQuery(correlationStart.AuthorizationUrl)["state"],
                "wrong-correlation",
                "test-browser",
                "login-correlation",
                CancellationToken.None));
        Assert.Equal("expired", error.Reason);
        Assert.Equal(0, badCorrelation.Handler.TokenRequestCount);
    }

    private static Fixture CreateFixture(
        string companyEmail,
        string asanaEmail,
        string asanaWorkspaceGid = "workspace-1")
    {
        var dbOptions = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseInMemoryDatabase($"AsanaConnection-{Guid.NewGuid():N}")
            .Options;
        var db = new TaskCaptureDbContext(dbOptions);
        var currentUser = new TestCurrentUser(companyEmail);
        var user = new User
        {
            ClientKey = currentUser.ClientKey,
            IdentityProvider = currentUser.IdentityProvider,
            SubjectId = currentUser.SubjectId,
            Email = companyEmail,
            DisplayName = "Employee"
        };
        db.Users.Add(user);
        db.SaveChanges();

        var asanaOptions = Microsoft.Extensions.Options.Options.Create(new AsanaOptions
        {
            Mode = "Api",
            CredentialMode = "PerUserOAuth",
            DefaultWorkspaceGid = "workspace-1",
            OAuth = new AsanaOAuthOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret-for-test",
                RedirectUri = "https://taskcapture.example/api/asana/connection/callback",
                RequireMatchingEmail = true
            }
        });
        var handler = new AsanaOAuthHandler(asanaEmail, asanaWorkspaceGid);
        var service = new AsanaConnectionService(
            db,
            currentUser,
            asanaOptions,
            Microsoft.Extensions.Options.Options.Create(new AccessOptions
            {
                Mode = "AsanaOAuth",
                AllowedEmailDomains = ["example.co.jp"]
            }),
            new EphemeralDataProtectionProvider(),
            new TestHttpClientFactory(handler),
            TimeProvider.System);
        return new Fixture(db, user, service, handler);
    }

    private static string GetState(string authorizationUri)
    {
        var query = new Uri(authorizationUri).Query.TrimStart('?');
        var item = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Single(pair => Uri.UnescapeDataString(pair[0]) == "state");
        return Uri.UnescapeDataString(item[1]);
    }

    private static Dictionary<string, string> ParseQuery(string uri) =>
        new Uri(uri).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1]));

    private static string CreateCodeChallenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class TestCurrentUser(string email) : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public string ClientKey => UserIdentityKey.Create(IdentityProvider, SubjectId);
        public string IdentityProvider => "Email";
        public string SubjectId => email.ToLowerInvariant();
        public string? Email => email;
        public string DisplayName => "Employee";
        public bool IsAdmin => false;
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class AsanaOAuthHandler(string asanaEmail, string asanaWorkspaceGid) : HttpMessageHandler
    {
        public bool RevokeRequested { get; private set; }
        public string? RevokedToken { get; private set; }
        public int TokenRequestCount { get; private set; }
        public Dictionary<string, string> LastTokenForm { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/oauth_revoke", StringComparison.Ordinal) == true)
            {
                RevokeRequested = true;
                var form = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                RevokedToken = form.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(pair => pair.Length == 2)
                    .Where(pair => Uri.UnescapeDataString(pair[0]) == "token")
                    .Select(pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')))
                    .SingleOrDefault();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            if (request.Method == HttpMethod.Post)
            {
                TokenRequestCount++;
                var form = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                LastTokenForm = form.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(pair => pair.Length == 2)
                    .ToDictionary(
                        pair => Uri.UnescapeDataString(pair[0]),
                        pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            }

            var json = request.Method == HttpMethod.Post
                ? """{"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600}"""
                : System.Text.Json.JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        gid = "asana-user",
                        name = "Employee",
                        email = asanaEmail,
                        workspaces = new[] { new { gid = asanaWorkspaceGid, name = "Company" } }
                    }
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record Fixture(
        TaskCaptureDbContext Db,
        User User,
        AsanaConnectionService Service,
        AsanaOAuthHandler Handler) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
