using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class WbsImportServiceTests
{
    [Fact]
    public async Task RegisterBatch_RetriesOnlyFailedChild()
    {
        var options = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseInMemoryDatabase($"WbsImportService-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TaskCaptureDbContext(options);
        var asana = new FlakyImportAsanaService();
        var service = new WbsImportService(db, asana, TimeProvider.System);
        var batch = await service.CreateBatchAsync(
            new WbsImportBatchRequest
            {
                FileName = "retry.csv",
                FileHash = new string('1', 64),
                SheetName = "Sheet1",
                LayoutSignature = new string('2', 64),
                Rows =
                [
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 2,
                        SourceKey = "P",
                        Title = "親",
                        SortOrder = 0
                    },
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 3,
                        SourceKey = "C",
                        ParentSourceKey = "P",
                        Title = "失敗する子",
                        SortOrder = 1
                    }
                ]
            },
            "wbs-retry-device",
            "preview",
            CancellationToken.None);

        var first = await service.RegisterBatchAsync(
            batch.Id,
            "wbs-retry-device",
            "register-1",
            CancellationToken.None);
        Assert.Equal("PartiallyRegistered", first.Status);
        Assert.Equal(1, asana.CreateCounts["親"]);
        Assert.Equal(1, asana.CreateCounts["失敗する子"]);

        var second = await service.RegisterBatchAsync(
            batch.Id,
            "wbs-retry-device",
            "register-2",
            CancellationToken.None);
        Assert.Equal("Registered", second.Status);
        Assert.Equal(1, asana.CreateCounts["親"]);
        Assert.Equal(2, asana.CreateCounts["失敗する子"]);
    }

    private sealed class FlakyImportAsanaService : IAsanaTaskService
    {
        public Dictionary<string, int> CreateCounts { get; } = [];

        public Task<AsanaRegistrationResult> CreateTaskAsync(
            TaskCandidate candidate,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AsanaRegistrationResult(true, "Test", "regular", null));

        public Task<AsanaRegistrationResult> CreateSubtaskAsync(
            TaskCandidate candidate,
            TaskCandidateSubtask subtask,
            string parentTaskGid,
            string? resolvedAssigneeGid,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AsanaRegistrationResult(true, "Test", "regular-child", null));

        public Task<AsanaRegistrationResult> CreateImportTaskAsync(
            AsanaImportTask task,
            string? parentTaskGid,
            CancellationToken cancellationToken)
        {
            CreateCounts.TryGetValue(task.Title, out var count);
            CreateCounts[task.Title] = ++count;
            var shouldFail = task.Title == "失敗する子" && count == 1;
            return Task.FromResult(shouldFail
                ? new AsanaRegistrationResult(false, "Test", null, null, "TEMPORARY", "一時エラー")
                : new AsanaRegistrationResult(true, "Test", $"gid-{task.Title}", null));
        }
    }
}
