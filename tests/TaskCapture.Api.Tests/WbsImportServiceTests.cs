using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class WbsImportServiceTests
{
    [Fact]
    public async Task Preview_RollsUpDatesAndResolvesAssignee()
    {
        var options = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseInMemoryDatabase($"WbsDates-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TaskCaptureDbContext(options);
        var service = new WbsImportService(db, new FlakyImportAsanaService(), TimeProvider.System);

        var batch = await service.CreateBatchAsync(
            new WbsImportBatchRequest
            {
                FileName = "dates.xlsx",
                FileHash = new string('3', 64),
                SheetName = "予定",
                LayoutSignature = new string('4', 64),
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
                        SourceKey = "C1",
                        ParentSourceKey = "P",
                        Title = "子1",
                        Assignee = "大西",
                        StartDate = new DateOnly(2026, 10, 2),
                        DueDate = new DateOnly(2026, 10, 4),
                        SortOrder = 1
                    },
                    new WbsNormalizedRowRequest
                    {
                        SourceRowNumber = 4,
                        SourceKey = "C2",
                        ParentSourceKey = "P",
                        Title = "子2",
                        StartDate = new DateOnly(2026, 9, 28),
                        DueDate = new DateOnly(2026, 10, 10),
                        SortOrder = 2
                    }
                ]
            },
            "dates-device",
            "preview",
            CancellationToken.None);

        var parent = Assert.Single(batch.Rows, row => row.SourceKey == "P");
        Assert.Equal(new DateOnly(2026, 9, 28), parent.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 10), parent.DueDate);
        var assigned = Assert.Single(batch.Rows, row => row.SourceKey == "C1");
        Assert.Equal("Resolved", assigned.AssigneeResolutionStatus);
        Assert.Equal("大西", assigned.ResolvedAssigneeName);
    }

    [Fact]
    public async Task Reimport_UpdatesChangedTaskAndUndoRemovesOnlyNewTask()
    {
        var options = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseInMemoryDatabase($"WbsChanges-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TaskCaptureDbContext(options);
        var asana = new FlakyImportAsanaService();
        var service = new WbsImportService(db, asana, TimeProvider.System);

        WbsImportBatchRequest Request(string title, string key, char hash) => new()
        {
            FileName = "same.xlsx",
            FileHash = new string(hash, 64),
            SheetName = "WBS",
            LayoutSignature = new string('9', 64),
            Rows =
            [
                new WbsNormalizedRowRequest
                {
                    SourceRowNumber = 2,
                    SourceKey = key,
                    Title = title,
                    SortOrder = 0
                }
            ]
        };

        var first = await service.CreateBatchAsync(
            Request("最初の内容", "1", '5'),
            "change-device",
            "preview-1",
            CancellationToken.None);
        await service.RegisterBatchAsync(
            first.Id,
            new WbsRegisterBatchRequest(),
            "change-device",
            "register-1",
            CancellationToken.None);

        var changed = await service.CreateBatchAsync(
            Request("変更後の内容", "1", '6'),
            "change-device",
            "preview-2",
            CancellationToken.None);
        Assert.Equal(1, changed.ChangedRows);
        var updated = await service.RegisterBatchAsync(
            changed.Id,
            new WbsRegisterBatchRequest(),
            "change-device",
            "register-2",
            CancellationToken.None);
        Assert.Equal("Updated", Assert.Single(updated.Rows).Status);
        Assert.Contains("変更後の内容", asana.UpdatedTitles);

        var newBatch = await service.CreateBatchAsync(
            Request("新しいタスク", "2", '7'),
            "change-device",
            "preview-3",
            CancellationToken.None);
        var registered = await service.RegisterBatchAsync(
            newBatch.Id,
            new WbsRegisterBatchRequest(),
            "change-device",
            "register-3",
            CancellationToken.None);
        Assert.True(registered.CanUndo);
        var reverted = await service.UndoBatchAsync(
            newBatch.Id,
            "change-device",
            "undo",
            CancellationToken.None);
        Assert.Equal("Reverted", reverted.Status);
        Assert.Single(asana.DeletedTaskGids);
    }

    [Fact]
    public async Task SavedReading_LearnsCompanyColumnName()
    {
        var options = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseInMemoryDatabase($"WbsAliases-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TaskCaptureDbContext(options);
        var service = new WbsImportService(db, new FlakyImportAsanaService(), TimeProvider.System);
        await service.SaveProfileAsync(
            null,
            new WbsImportProfileRequest
            {
                Name = "社内WBS",
                LayoutSignature = new string('8', 64),
                SheetName = "計画",
                Mapping = new WbsMappingRequest
                {
                    Roles = new Dictionary<int, string> { [0] = "title", [1] = "assignee" }
                },
                ColumnNames = new Dictionary<int, string> { [0] = "実施事項", [1] = "主担当" }
            },
            "alias-device",
            "save",
            CancellationToken.None);

        var aliases = await service.GetColumnAliasesAsync("alias-device", CancellationToken.None);
        Assert.Contains(aliases, alias => alias.ColumnName == "実施事項" && alias.Role == "title");
        Assert.Contains(aliases, alias => alias.ColumnName == "主担当" && alias.Role == "assignee");
    }

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
            new WbsRegisterBatchRequest(),
            "wbs-retry-device",
            "register-1",
            CancellationToken.None);
        Assert.Equal("PartiallyRegistered", first.Status);
        Assert.Equal(1, asana.CreateCounts["親"]);
        Assert.Equal(1, asana.CreateCounts["失敗する子"]);

        var second = await service.RegisterBatchAsync(
            batch.Id,
            new WbsRegisterBatchRequest(),
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
        public List<string> UpdatedTitles { get; } = [];
        public List<string> DeletedTaskGids { get; } = [];

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

        public Task<AsanaRegistrationResult> UpdateImportTaskAsync(
            AsanaImportTask task,
            string taskGid,
            CancellationToken cancellationToken)
        {
            UpdatedTitles.Add(task.Title);
            return Task.FromResult(new AsanaRegistrationResult(true, "Test", taskGid, null));
        }

        public Task<AsanaActionResult> DeleteTaskAsync(
            string taskGid,
            CancellationToken cancellationToken)
        {
            DeletedTaskGids.Add(taskGid);
            return Task.FromResult(new AsanaActionResult(true, "Test"));
        }

        public Task<IReadOnlyList<AsanaAssigneeResolution>> ResolveAssigneesAsync(
            IReadOnlyList<string> requestedNames,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AsanaAssigneeResolution>>(
                requestedNames.Select(name => new AsanaAssigneeResolution(
                    name,
                    "Resolved",
                    $"gid-{name}",
                    name,
                    null)).ToArray());
    }
}
