using Microsoft.Extensions.Logging.Abstractions;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class GeminiTaskOrganizerTests
{
    [Fact]
    public async Task OrganizeAsync_ParsesStructuredCandidateAndProvidesJapanDate()
    {
        var client = new StubGeminiTaskClient(
            """
            {
              "title": "月次報告書を確認する",
              "description": "月次報告書の数値とコメントを確認する。",
              "assignee": "田中さん",
              "dueDate": "2026-07-21"
            }
            """);
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));
        var organizer = new GeminiTaskOrganizer(client, clock);

        var result = await organizer.OrganizeAsync(
            "月次報告書を確認。担当は田中さん、期限は明日。",
            CancellationToken.None);

        Assert.Equal("月次報告書を確認する", result.Title);
        Assert.Equal("月次報告書の数値とコメントを確認する。", result.Description);
        Assert.Equal("田中さん", result.Assignee);
        Assert.Equal(new DateOnly(2026, 7, 21), result.DueDate);
        Assert.Contains("2026-07-20", client.SystemInstruction);
    }

    [Fact]
    public async Task OrganizeAsync_UsesRawTextWhenDescriptionIsEmptyAndIgnoresInvalidDate()
    {
        var client = new StubGeminiTaskClient(
            """
            {"title":"確認する","description":"","assignee":null,"dueDate":"明日"}
            """);
        var organizer = new GeminiTaskOrganizer(client, TimeProvider.System);

        var result = await organizer.OrganizeAsync("原文を保持する", CancellationToken.None);

        Assert.Equal("原文を保持する", result.Description);
        Assert.Null(result.Assignee);
        Assert.Null(result.DueDate);
    }

    [Fact]
    public async Task FallbackOrganizer_UsesRuleBasedResultWhenGeminiFails()
    {
        var primary = new ThrowingTaskOrganizer();
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));
        var fallback = new RuleBasedTaskOrganizer(clock);
        var organizer = new FallbackTaskOrganizer(
            primary,
            fallback,
            NullLogger<FallbackTaskOrganizer>.Instance);

        var result = await organizer.OrganizeAsync(
            "請求書を送る\n担当：佐藤さん\n期限：明日",
            CancellationToken.None);

        Assert.Equal("請求書を送る", result.Title);
        Assert.Equal("佐藤さん", result.Assignee);
        Assert.Equal(new DateOnly(2026, 7, 21), result.DueDate);
    }

    [Fact]
    public async Task FallbackOrganizer_DoesNotSwallowRequestCancellation()
    {
        var organizer = new FallbackTaskOrganizer(
            new CanceledTaskOrganizer(),
            new RuleBasedTaskOrganizer(TimeProvider.System),
            NullLogger<FallbackTaskOrganizer>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            organizer.OrganizeAsync("キャンセルされる入力", CancellationToken.None));
    }

    private sealed class StubGeminiTaskClient(string response) : IGeminiTaskClient
    {
        public string SystemInstruction { get; private set; } = string.Empty;

        public Task<string> GenerateTaskJsonAsync(
            string systemInstruction,
            string rawText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemInstruction = systemInstruction;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingTaskOrganizer : ITaskOrganizer
    {
        public Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated Gemini failure.");
    }

    private sealed class CanceledTaskOrganizer : ITaskOrganizer
    {
        public Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken) =>
            throw new OperationCanceledException("Simulated request cancellation.");
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
