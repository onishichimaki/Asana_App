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
    public async Task CompanyEmailCode_CreatesAccountProtectsApiAndLogsOut()
    {
        await using var factory = new EmailAuthenticationWebApplicationFactory();
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
}

public sealed class EmailAuthenticationWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"TaskCaptureEmailAuth-{Guid.NewGuid():N}";

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
                ["Integration:Asana:Mode"] = "Mock",
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
