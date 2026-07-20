using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;

namespace TaskCapture.Api.Tests;

public sealed class TaskWorkflowApiTests : IAsyncLifetime
{
    private TaskCaptureWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TaskCaptureWebApplicationFactory();
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-TaskCapture-Client", "integration-test-device");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task OrganizeAndRegister_PersistsEntireWorkflowAndIsIdempotent()
    {
        var organizeResponse = await _client.PostAsJsonAsync("/api/task-requests/organize", new
        {
            rawText = "発注書を確認する\n担当：田中さん\n期限：2026-08-02",
            source = "paste"
        });
        organizeResponse.EnsureSuccessStatusCode();
        var organized = await organizeResponse.Content.ReadFromJsonAsync<OrganizeTaskResponse>();
        Assert.NotNull(organized);
        Assert.Equal("発注書を確認する", organized.Candidate.Title);
        Assert.Equal("田中さん", organized.Candidate.Assignee);
        Assert.Equal(new DateOnly(2026, 8, 2), organized.Candidate.DueDate);

        var update = new CandidateUpdateRequest
        {
            Title = "発注書を最終確認する",
            Description = organized.Candidate.Description,
            Assignee = "me",
            DueDate = organized.Candidate.DueDate,
            ProjectGid = "123456789",
            Tags = ["987654321"],
            Priority = "high"
        };
        var registerResponse = await _client.PostAsJsonAsync(
            $"/api/task-candidates/{organized.Candidate.Id}/register",
            update);
        registerResponse.EnsureSuccessStatusCode();
        var registration = await registerResponse.Content.ReadFromJsonAsync<RegistrationResponse>();
        Assert.NotNull(registration);
        Assert.True(registration.Succeeded);
        Assert.Equal("Mock", registration.Provider);
        Assert.StartsWith("mock-", registration.ExternalTaskGid);

        var secondResponse = await _client.PostAsJsonAsync(
            $"/api/task-candidates/{organized.Candidate.Id}/register",
            update);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<RegistrationResponse>();
        Assert.NotNull(second);
        Assert.True(second.AlreadyRegistered);
        Assert.Equal(registration.RegistrationId, second.RegistrationId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Equal(1, await db.TaskRequests.CountAsync());
        Assert.Equal(1, await db.TaskCandidates.CountAsync());
        Assert.Equal(1, await db.AsanaRegistrations.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync());
        Assert.Equal("Registered", await db.TaskRequests.Select(x => x.Status).SingleAsync());
        Assert.Equal("発注書を最終確認する", await db.TaskCandidates.Select(x => x.Title).SingleAsync());
    }

    [Fact]
    public async Task Organize_RejectsEmptyInputWithoutWritingHistory()
    {
        var response = await _client.PostAsJsonAsync("/api/task-requests/organize", new
        {
            rawText = "",
            source = "text"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Equal(0, await db.TaskRequests.CountAsync());
    }

    [Theory]
    [InlineData("image")]
    [InlineData("minutes")]
    public async Task Organize_AcceptsExtractedMediaSources(string source)
    {
        var response = await _client.PostAsJsonAsync("/api/task-requests/organize", new
        {
            rawText = "画像または議事録から抽出したタスク",
            source
        });

        response.EnsureSuccessStatusCode();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Equal(source, await db.TaskRequests.Select(x => x.Source).SingleAsync());
    }

    [Fact]
    public async Task Register_RejectsSectionWithoutProject()
    {
        var organizeResponse = await _client.PostAsJsonAsync("/api/task-requests/organize", new
        {
            rawText = "テストタスク",
            source = "text"
        });
        organizeResponse.EnsureSuccessStatusCode();
        var organized = await organizeResponse.Content.ReadFromJsonAsync<OrganizeTaskResponse>();

        var response = await _client.PostAsJsonAsync(
            $"/api/task-candidates/{organized!.Candidate.Id}/register",
            new CandidateUpdateRequest
            {
                Title = organized.Candidate.Title,
                Description = organized.Candidate.Description,
                SectionGid = "12345"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class TaskCaptureWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"TaskCaptureTests-{Guid.NewGuid():N}";

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
                ["Integration:Asana:Mode"] = "Mock"
            });
        });
    }
}
