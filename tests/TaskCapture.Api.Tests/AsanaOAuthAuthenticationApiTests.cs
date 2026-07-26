using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskCapture.Api.Data;
using TaskCapture.Api.Security;

namespace TaskCapture.Api.Tests;

public sealed class AsanaOAuthAuthenticationApiTests
{
    [Fact]
    public async Task PreRegisteredAsanaUser_LogsInOnceUsesBusinessApiAndLogsOut()
    {
        await using var factory = new AsanaOAuthWebApplicationFactory();
        await PreRegisterAsync(factory, "member@example.co.jp", "Member");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var initial = await client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");
        Assert.NotNull(initial);
        Assert.Equal("AsanaOAuth", initial.Mode);
        Assert.False(initial.Authenticated);

        var start = await client.GetAsync("/api/auth/asana/start");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var authorization = ParseQuery(start.Headers.Location!.ToString());
        Assert.Equal("S256", authorization["code_challenge_method"]);

        var callback = await client.GetAsync(
            $"/api/auth/asana/callback?code=valid-code&state={Uri.EscapeDataString(authorization["state"])}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/?login=connected", callback.Headers.Location!.ToString());

        var me = await client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me.Authenticated);
        Assert.Equal("member@example.co.jp", me.Email);
        Assert.True(me.AsanaConnected);
        (await client.GetAsync("/api/task-requests/recent")).EnsureSuccessStatusCode();

        var logout = await client.PostAsync("/api/auth/logout", null);
        logout.EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/task-requests/recent")).StatusCode);
        Assert.True(factory.Handler.RevokeRequested);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.NotNull(await db.UserSessions.Select(item => item.RevokedAtUtc).SingleAsync());
        Assert.NotNull(await db.AsanaConnections.Select(item => item.RevokedAtUtc).SingleAsync());
        Assert.True(await db.AuditLogs.AnyAsync(item => item.EventType == "LoginSucceeded"));
    }

    [Fact]
    public async Task CallbackWithoutCorrelationCookie_IsRejectedBeforeTokenExchange()
    {
        await using var factory = new AsanaOAuthWebApplicationFactory();
        using var startClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var start = await startClient.GetAsync("/api/auth/asana/start");
        var state = ParseQuery(start.Headers.Location!.ToString())["state"];

        using var callbackClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var callback = await callbackClient.GetAsync(
            $"/api/auth/asana/callback?code=valid-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal("/?login=expired", callback.Headers.Location!.ToString());
        Assert.Equal(0, factory.Handler.TokenRequestCount);
    }

    [Fact]
    public async Task LogoutWithAnotherActiveDevice_KeepsSharedAsanaConnection()
    {
        await using var factory = new AsanaOAuthWebApplicationFactory();
        await PreRegisterAsync(factory, "member@example.co.jp", "Member");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var start = await client.GetAsync("/api/auth/asana/start");
        var authorization = ParseQuery(start.Headers.Location!.ToString());
        await client.GetAsync(
            $"/api/auth/asana/callback?code=valid-code&state={Uri.EscapeDataString(authorization["state"])}");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
            var userId = await db.Users.Select(item => item.Id).SingleAsync();
            db.UserSessions.Add(new UserSession
            {
                UserId = userId,
                UserAgentHash = "another-device",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastSeenAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(14)
            });
            await db.SaveChangesAsync();
        }

        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();

        Assert.False(factory.Handler.RevokeRequested);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Equal(1, await verificationDb.UserSessions.CountAsync(item => item.RevokedAtUtc != null));
        Assert.Equal(1, await verificationDb.UserSessions.CountAsync(item => item.RevokedAtUtc == null));
        Assert.Null(await verificationDb.AsanaConnections.Select(item => item.RevokedAtUtc).SingleAsync());
    }

    private static async Task PreRegisterAsync(
        AsanaOAuthWebApplicationFactory factory,
        string email,
        string displayName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        db.Users.Add(new User
        {
            ClientKey = UserIdentityKey.Create("Email", email),
            IdentityProvider = "Email",
            SubjectId = email,
            Email = email,
            DisplayName = displayName,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static Dictionary<string, string> ParseQuery(string uri) =>
        new Uri(uri).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1]));

    private sealed record AuthMeResponse(
        bool Authenticated,
        string Mode,
        string? Email,
        bool AsanaConnected);
}

public sealed class AsanaOAuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"TaskCaptureAsanaLogin-{Guid.NewGuid():N}";
    public AsanaLoginHttpHandler Handler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "InMemory",
                ["Database:InMemoryDatabaseName"] = _databaseName,
                ["Database:ApplyMigrations"] = "false",
                ["TaskOrganization:Mode"] = "RuleBased",
                ["Integration:Asana:Mode"] = "Api",
                ["Integration:Asana:CredentialMode"] = "PerUserOAuth",
                ["Integration:Asana:DefaultWorkspaceGid"] = "workspace-1",
                ["Integration:Asana:OAuth:ClientId"] = "client-id",
                ["Integration:Asana:OAuth:ClientSecret"] = "client-secret-for-test",
                ["Integration:Asana:OAuth:RedirectUri"] = "https://taskcapture.example/api/auth/asana/callback",
                ["Access:Mode"] = "AsanaOAuth",
                ["Access:AllowedEmailDomains:0"] = "example.co.jp",
                ["Access:AdminEmails:0"] = "admin@example.co.jp"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new AsanaLoginHttpClientFactory(Handler));
        });
    }
}

public sealed class AsanaLoginHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public sealed class AsanaLoginHttpHandler : HttpMessageHandler
{
    public int TokenRequestCount { get; private set; }
    public bool RevokeRequested { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.EndsWith("/oauth_revoke", StringComparison.Ordinal) == true)
        {
            RevokeRequested = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        if (request.RequestUri?.AbsolutePath.EndsWith("/oauth_token", StringComparison.Ordinal) == true)
        {
            TokenRequestCount++;
            return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600}""");
        }

        return Json("""
            {"data":{"gid":"asana-user","name":"Asana Member","email":"member@example.co.jp","workspaces":[{"gid":"workspace-1","name":"Company"}]}}
            """);
    }

    private static Task<HttpResponseMessage> Json(string content) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        });
}
