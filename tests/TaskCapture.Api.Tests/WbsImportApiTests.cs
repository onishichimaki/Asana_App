using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;

namespace TaskCapture.Api.Tests;

public sealed class WbsImportApiTests : IAsyncLifetime
{
    private TaskCaptureWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TaskCaptureWebApplicationFactory();
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-TaskCapture-Client", "wbs-integration-device");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ProfilePreviewRegisterAndReimport_AreAuditedAndIdempotent()
    {
        var mapping = new WbsMappingRequest
        {
            HierarchyMode = "parentKey",
            Roles = new Dictionary<int, string>
            {
                [0] = "key",
                [1] = "parentKey",
                [2] = "title",
                [3] = "assignee",
                [4] = "dueDate"
            }
        };
        var profileResponse = await _client.PostAsJsonAsync(
            "/api/wbs-imports/profiles",
            new WbsImportProfileRequest
            {
                Name = "A社WBS",
                LayoutSignature = new string('a', 64),
                SheetName = "WBS",
                HeaderRow = 2,
                DataStartRow = 3,
                Mapping = mapping,
                ProjectGid = "123456789"
            });
        Assert.Equal(HttpStatusCode.Created, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<WbsImportProfileResponse>();
        Assert.NotNull(profile);

        var batch = await CreateBatchAsync(profile.Id, new string('b', 64));
        Assert.Equal("Ready", batch.Status);
        Assert.Equal(3, batch.ValidRows);
        Assert.Equal([0, 1, 1], batch.Rows.Select(row => row.Depth));

        var registerResponse = await _client.PostAsync(
            $"/api/wbs-imports/batches/{batch.Id}/register",
            null);
        registerResponse.EnsureSuccessStatusCode();
        var registered = await registerResponse.Content.ReadFromJsonAsync<WbsImportBatchResponse>();
        Assert.NotNull(registered);
        Assert.Equal("Registered", registered.Status);
        Assert.Equal(3, registered.SucceededRows);
        Assert.All(registered.Rows, row => Assert.Equal("Registered", row.Status));
        Assert.Equal("me", registered.Rows[0].ResolvedAssigneeName);

        var secondRegister = await _client.PostAsync(
            $"/api/wbs-imports/batches/{batch.Id}/register",
            null);
        secondRegister.EnsureSuccessStatusCode();
        var alreadyRegistered = await secondRegister.Content.ReadFromJsonAsync<WbsImportBatchResponse>();
        Assert.NotNull(alreadyRegistered);
        Assert.True(alreadyRegistered.AlreadyRegistered);

        var repeatedBatch = await CreateBatchAsync(profile.Id, new string('c', 64));
        var repeatedResponse = await _client.PostAsync(
            $"/api/wbs-imports/batches/{repeatedBatch.Id}/register",
            null);
        repeatedResponse.EnsureSuccessStatusCode();
        var repeated = await repeatedResponse.Content.ReadFromJsonAsync<WbsImportBatchResponse>();
        Assert.NotNull(repeated);
        Assert.Equal("Registered", repeated.Status);
        Assert.All(repeated.Rows, row => Assert.Equal("Duplicate", row.Status));
        Assert.All(repeated.Rows, row => Assert.NotNull(row.ExternalTaskGid));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Equal(1, await db.WbsImportProfiles.CountAsync());
        Assert.Equal(2, await db.WbsImportBatches.CountAsync());
        Assert.Equal(6, await db.WbsImportRows.CountAsync());
        Assert.True(await db.AuditLogs.CountAsync() >= 5);
    }

    [Fact]
    public async Task Preview_ReportsMissingParentAndExportsErrorCsv()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/wbs-imports/batches",
            new WbsImportBatchRequest
            {
                FileName = "invalid.csv",
                FileHash = new string('d', 64),
                SheetName = "Sheet1",
                LayoutSignature = new string('e', 64),
                Rows =
                [
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 2,
                        SourceKey = "1.1",
                        ParentSourceKey = "1",
                        Title = "親がないタスク",
                        SortOrder = 0
                    }
                ]
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await response.Content.ReadFromJsonAsync<WbsImportBatchResponse>();
        Assert.NotNull(batch);
        Assert.Equal("Invalid", batch.Status);
        Assert.Contains(batch.Rows[0].ValidationErrors, error => error.Contains("親キー", StringComparison.Ordinal));

        var csv = await _client.GetStringAsync($"/api/wbs-imports/batches/{batch.Id}/errors.csv");
        Assert.Contains("親がないタスク", csv, StringComparison.Ordinal);
        Assert.Contains("親キー", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteProfile_PreservesExistingBatchAndClearsItsReference()
    {
        var profileResponse = await _client.PostAsJsonAsync(
            "/api/wbs-imports/profiles",
            new WbsImportProfileRequest
            {
                Name = "削除テスト",
                LayoutSignature = new string('f', 64),
                SheetName = "WBS",
                HeaderRow = 1,
                DataStartRow = 2,
                Mapping = new WbsMappingRequest
                {
                    Roles = new Dictionary<int, string> { [0] = "title" }
                }
            });
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content.ReadFromJsonAsync<WbsImportProfileResponse>();
        Assert.NotNull(profile);

        var batch = await CreateBatchAsync(profile.Id, new string('9', 64));
        var deleteResponse = await _client.DeleteAsync($"/api/wbs-imports/profiles/{profile.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var preserved = await _client.GetFromJsonAsync<WbsImportBatchResponse>(
            $"/api/wbs-imports/batches/{batch.Id}");
        Assert.NotNull(preserved);
        Assert.Equal(batch.Id, preserved.Id);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
        Assert.Null((await db.WbsImportBatches.AsNoTracking().SingleAsync(item => item.Id == batch.Id)).WbsImportProfileId);
    }

    [Fact]
    public async Task Preview_RejectsSourceKeysThatOnlyDifferByWhitespace()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/wbs-imports/batches",
            new WbsImportBatchRequest
            {
                FileName = "duplicate-keys.csv",
                FileHash = new string('7', 64),
                SheetName = "WBS",
                LayoutSignature = new string('8', 64),
                Rows =
                [
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 2,
                        SourceKey = "A",
                        Title = "1件目",
                        SortOrder = 0
                    },
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 3,
                        SourceKey = " A ",
                        Title = "2件目",
                        SortOrder = 1
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<WbsImportBatchResponse> CreateBatchAsync(Guid profileId, string fileHash)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/wbs-imports/batches",
            new WbsImportBatchRequest
            {
                FileName = "a-company.xlsx",
                FileHash = fileHash,
                SheetName = "WBS",
                LayoutSignature = new string('a', 64),
                ProfileId = profileId,
                ProjectGid = "123456789",
                Rows =
                [
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 3,
                        SourceKey = "1",
                        Title = "システム開発",
                        Assignee = "me",
                        SortOrder = 0
                    },
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 4,
                        SourceKey = "1.1",
                        ParentSourceKey = "1",
                        Title = "要件定義",
                        SortOrder = 1
                    },
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 5,
                        SourceKey = "1.2",
                        ParentSourceKey = "1",
                        Title = "設計",
                        DueDate = new DateOnly(2026, 8, 31),
                        SortOrder = 2
                    }
                ]
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WbsImportBatchResponse>())!;
    }
}
