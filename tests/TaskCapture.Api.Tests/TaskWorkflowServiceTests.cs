using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class TaskWorkflowServiceTests
{
    [Fact]
    public async Task RegisterAsync_RetriesOnlyFailedSubtasksWithoutDuplicatingParent()
    {
        var options = new DbContextOptionsBuilder<TaskCaptureDbContext>()
            .UseInMemoryDatabase($"TaskWorkflowService-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TaskCaptureDbContext(options);
        var asana = new FlakySubtaskAsanaService();
        var workflow = new TaskWorkflowService(
            db,
            new DecomposingOrganizer(),
            asana,
            TimeProvider.System);
        var organized = await workflow.OrganizeAsync(
            new OrganizeTaskRequest { RawText = "親タスク", Source = "text" },
            "partial-retry-test",
            "organize-trace",
            CancellationToken.None);
        var update = new CandidateUpdateRequest
        {
            Title = organized.Candidate.Title,
            Description = organized.Candidate.Description,
            Subtasks = organized.Candidate.Subtasks
        };

        var first = await workflow.RegisterAsync(
            organized.Candidate.Id,
            update,
            "first-register-trace",
            CancellationToken.None);
        var second = await workflow.RegisterAsync(
            organized.Candidate.Id,
            update,
            "retry-register-trace",
            CancellationToken.None);
        var third = await workflow.RegisterAsync(
            organized.Candidate.Id,
            update,
            "idempotent-register-trace",
            CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.False(second.AlreadyRegistered);
        Assert.True(third.Succeeded);
        Assert.True(third.AlreadyRegistered);
        Assert.Equal(1, asana.ParentCreateCount);
        Assert.Equal(1, asana.SubtaskCreateCounts["最初の手順"]);
        Assert.Equal(2, asana.SubtaskCreateCounts["再試行する手順"]);
        Assert.Equal(1, await db.AsanaRegistrations.CountAsync());
        Assert.Equal(3, await db.AsanaSubtaskRegistrations.CountAsync());
        Assert.Equal("Registered", await db.TaskRequests.Select(x => x.Status).SingleAsync());
    }

    private sealed class DecomposingOrganizer : ITaskOrganizer
    {
        public Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken) =>
            Task.FromResult(new OrganizedTask(
                "親タスク",
                "親タスクの説明",
                null,
                null,
                ["最初の手順", "再試行する手順"]));
    }

    private sealed class FlakySubtaskAsanaService : IAsanaTaskService
    {
        public int ParentCreateCount { get; private set; }
        public Dictionary<string, int> SubtaskCreateCounts { get; } = [];

        public Task<AsanaRegistrationResult> CreateTaskAsync(
            TaskCandidate candidate,
            CancellationToken cancellationToken)
        {
            ParentCreateCount++;
            return Task.FromResult(new AsanaRegistrationResult(true, "Test", "parent-gid", null));
        }

        public Task<AsanaRegistrationResult> CreateSubtaskAsync(
            TaskCandidate candidate,
            TaskCandidateSubtask subtask,
            string parentTaskGid,
            CancellationToken cancellationToken)
        {
            SubtaskCreateCounts.TryGetValue(subtask.Title, out var current);
            SubtaskCreateCounts[subtask.Title] = ++current;
            return Task.FromResult(subtask.Title == "再試行する手順" && current == 1
                ? new AsanaRegistrationResult(false, "Test", null, null, "TEMPORARY", "Temporary failure")
                : new AsanaRegistrationResult(true, "Test", $"subtask-{current}", null));
        }
    }
}
