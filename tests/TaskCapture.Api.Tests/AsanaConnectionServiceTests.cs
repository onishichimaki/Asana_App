using System.Net;
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

    private static Fixture CreateFixture(string companyEmail, string asanaEmail)
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
        var handler = new AsanaOAuthHandler(asanaEmail);
        var service = new AsanaConnectionService(
            db,
            currentUser,
            asanaOptions,
            new EphemeralDataProtectionProvider(),
            new TestHttpClientFactory(handler),
            TimeProvider.System);
        return new Fixture(db, user, service);
    }

    private static string GetState(string authorizationUri)
    {
        var query = new Uri(authorizationUri).Query.TrimStart('?');
        var item = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Single(pair => Uri.UnescapeDataString(pair[0]) == "state");
        return Uri.UnescapeDataString(item[1]);
    }

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

    private sealed class AsanaOAuthHandler(string asanaEmail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = request.Method == HttpMethod.Post
                ? """{"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600}"""
                : System.Text.Json.JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        gid = "asana-user",
                        name = "Employee",
                        email = asanaEmail,
                        workspaces = new[] { new { gid = "workspace-1", name = "Company" } }
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
        AsanaConnectionService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
