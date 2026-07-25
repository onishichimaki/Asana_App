using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;
using TaskCapture.Api.Security;

namespace TaskCapture.Api.Tests;

public sealed class EmailAuthenticationApiTests
{
    [Fact]
    public async Task CompanyEmailCode_UsesPreRegisteredAccountProtectsApiAndLogsOut()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory();
        await PreRegisterAsync(factory, "member@example.co.jp", "Member");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var initialMe = await client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");
        Assert.NotNull(initialMe);
        Assert.Equal("EmailCode", initialMe.Mode);
        Assert.False(initialMe.Authenticated);

        var unauthenticated = await client.GetAsync("/api/task-requests/recent");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var requestCode = await client.PostAsJsonAsync(
            "/api/auth/request-code",
            new { email = "member@example.co.jp" });
        requestCode.EnsureSuccessStatusCode();
        var codeResult = await requestCode.Content.ReadFromJsonAsync<RequestLoginCodeResponse>();
        Assert.NotNull(codeResult);
        Assert.Matches(@"^\d{6}$", codeResult.DevelopmentCode);

        var wrongValue = codeResult.DevelopmentCode == "000000" ? "000001" : "000000";
        var wrongCode = await client.PostAsJsonAsync(
            "/api/auth/verify-code",
            new { email = "member@example.co.jp", code = wrongValue });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCode.StatusCode);

        var verify = await client.PostAsJsonAsync(
            "/api/auth/verify-code",
            new { email = "member@example.co.jp", code = codeResult.DevelopmentCode });
        verify.EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me.Authenticated);
        Assert.Equal("member@example.co.jp", me.Email);
        Assert.False(me.IsAdmin);

        var authorized = await client.GetAsync("/api/task-requests/recent");
        authorized.EnsureSuccessStatusCode();

        var logout = await client.PostAsync("/api/auth/logout", null);
        logout.EnsureSuccessStatusCode();
        var afterLogout = await client.GetAsync("/api/task-requests/recent");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.UserSessions.CountAsync());
        Assert.NotNull(await db.UserSessions.Select(item => item.RevokedAtUtc).SingleAsync());
        Assert.True(await db.AuditLogs.AnyAsync(item => item.EventType == "LoginSucceeded"));
        Assert.True(await db.AuditLogs.AnyAsync(item => item.EventType == "Logout"));
    }

    [Fact]
    public async Task UnregisteredCompanyEmail_IsRejectedBeforeCodeDelivery()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/request-code",
            new { email = "not-registered@example.co.jp" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Empty(await db.EmailLoginCodes.ToListAsync());
    }

    [Fact]
    public async Task Administrator_CanPreRegisterUserWhoCanThenRequestCode()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        await LoginAsync(client, "admin@example.co.jp");

        var create = await client.PostAsJsonAsync(
            "/api/admin/users",
            new PreRegisterUserRequest
            {
                Email = "new.member@example.co.jp",
                DisplayName = "New Member"
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var newUserClient = factory.CreateClient();
        var requestCode = await newUserClient.PostAsJsonAsync(
            "/api/auth/request-code",
            new { email = "new.member@example.co.jp" });
        requestCode.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        var user = await db.Users.SingleAsync(item => item.Email == "new.member@example.co.jp");
        Assert.Equal("New Member", user.DisplayName);
        Assert.Null(user.LastLoginAtUtc);
        Assert.True(await db.AuditLogs.AnyAsync(item => item.EventType == "UserPreRegistered"));
    }

    [Fact]
    public async Task DeactivatingUser_RevokesSessionsAndClearsAsanaConnection()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        await LoginAsync(client, "admin@example.co.jp");
        await PreRegisterAsync(factory, "member@example.co.jp", "Member");

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
            var user = await db.Users.SingleAsync(item => item.Email == "member@example.co.jp");
            userId = user.Id;
            db.UserSessions.Add(new UserSession
            {
                UserId = user.Id,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
            });
            db.AsanaConnections.Add(new AsanaConnection
            {
                UserId = user.Id,
                AsanaUserGid = "asana-member",
                AsanaUserName = "Member",
                AsanaUserEmail = "member@example.co.jp",
                ProtectedAccessToken = "protected-access",
                ProtectedRefreshToken = "protected-refresh"
            });
            await db.SaveChangesAsync();
        }

        var deactivate = await client.PutAsJsonAsync(
            $"/api/admin/users/{userId}/access",
            new UpdateUserAccessRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.False(await verifyDb.Users.Where(item => item.Id == userId).Select(item => item.IsActive).SingleAsync());
        Assert.NotNull(await verifyDb.UserSessions.Where(item => item.UserId == userId).Select(item => item.RevokedAtUtc).SingleAsync());
        var connection = await verifyDb.AsanaConnections.SingleAsync(item => item.UserId == userId);
        Assert.NotNull(connection.RevokedAtUtc);
        Assert.Equal(string.Empty, connection.ProtectedAccessToken);
        Assert.Null(connection.ProtectedRefreshToken);
        Assert.True(await verifyDb.AuditLogs.AnyAsync(item => item.EventType == "AsanaDisconnectedByAdministrator"));
    }

    [Fact]
    public async Task BusinessApi_IsBlockedUntilMatchingAsanaAccountIsConnected()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory(requireAsanaConnection: true);
        await PreRegisterAsync(factory, "member@example.co.jp", "Member");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        await LoginAsync(client, "member@example.co.jp");

        var connectionStatus = await client.GetAsync("/api/asana/connection");
        connectionStatus.EnsureSuccessStatusCode();
        var blocked = await client.GetAsync("/api/task-requests/recent");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
            var user = await db.Users.SingleAsync(item => item.Email == "member@example.co.jp");
            db.AsanaConnections.Add(new AsanaConnection
            {
                UserId = user.Id,
                AsanaUserGid = "asana-member",
                AsanaUserName = "Member",
                AsanaUserEmail = "member@example.co.jp",
                WorkspaceGid = "workspace-1",
                ProtectedAccessToken = "protected-access"
            });
            await db.SaveChangesAsync();
        }

        var authorized = await client.GetAsync("/api/task-requests/recent");
        Assert.True(
            authorized.IsSuccessStatusCode,
            await authorized.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EmailOutsideCompanyDomain_IsRejected()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/request-code",
            new { email = "member@outside.example" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DatabaseAdminPromotion_SurvivesNextEmailLogin()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
            db.Users.Add(new User
            {
                ClientKey = UserIdentityKey.Create("Email", "member@example.co.jp"),
                IdentityProvider = "Email",
                SubjectId = "member@example.co.jp",
                Email = "member@example.co.jp",
                DisplayName = "Member",
                IsActive = true,
                IsAdmin = true
            });
            await db.SaveChangesAsync();
        }

        var requestCode = await client.PostAsJsonAsync(
            "/api/auth/request-code",
            new { email = "member@example.co.jp" });
        requestCode.EnsureSuccessStatusCode();
        var codeResult = await requestCode.Content.ReadFromJsonAsync<RequestLoginCodeResponse>();
        Assert.NotNull(codeResult?.DevelopmentCode);

        var verify = await client.PostAsJsonAsync(
            "/api/auth/verify-code",
            new { email = "member@example.co.jp", code = codeResult.DevelopmentCode });
        verify.EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me.IsAdmin);
    }

    private sealed record AuthMeResponse(
        bool Authenticated,
        string Mode,
        string? Email,
        string? DisplayName,
        bool IsAdmin);

    private static async Task PreRegisterAsync(
        EmailAuthenticationWebApplicationFactory factory,
        string email,
        string displayName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        if (await db.Users.AnyAsync(item => item.Email == email)) return;
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

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var requestCode = await client.PostAsJsonAsync(
            "/api/auth/request-code",
            new { email });
        requestCode.EnsureSuccessStatusCode();
        var result = await requestCode.Content.ReadFromJsonAsync<RequestLoginCodeResponse>();
        Assert.NotNull(result?.DevelopmentCode);
        var verify = await client.PostAsJsonAsync(
            "/api/auth/verify-code",
            new { email, code = result.DevelopmentCode });
        verify.EnsureSuccessStatusCode();
    }
}

public sealed class EmailAuthenticationWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"TaskCaptureEmailAuth-{Guid.NewGuid():N}";
    private readonly bool _requireAsanaConnection;

    public EmailAuthenticationWebApplicationFactory(bool requireAsanaConnection = false)
    {
        _requireAsanaConnection = requireAsanaConnection;
    }

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
                ["Integration:Asana:Mode"] = _requireAsanaConnection ? "Api" : "Mock",
                ["Integration:Asana:CredentialMode"] = _requireAsanaConnection ? "PerUserOAuth" : "PersonalAccessToken",
                ["Integration:Asana:DefaultWorkspaceGid"] = "workspace-1",
                ["Integration:Asana:OAuth:ClientId"] = "client-id",
                ["Integration:Asana:OAuth:ClientSecret"] = "client-secret-for-test",
                ["Integration:Asana:OAuth:RedirectUri"] = "https://taskcapture.example/api/asana/connection/callback",
                ["Access:Mode"] = "EmailCode",
                ["Access:AllowedEmailDomains:0"] = "example.co.jp",
                ["Access:AdminEmails:0"] = "admin@example.co.jp",
                ["Access:EmailCode:Delivery:Mode"] = "Mock",
                ["Access:EmailCode:CodeLifetimeMinutes"] = "10",
                ["Access:EmailCode:SessionDays"] = "14",
                ["Access:EmailCode:MaximumAttempts"] = "5"
            });
        });
    }
}
