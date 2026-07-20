using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class RuleBasedTaskOrganizerTests
{
    [Fact]
    public async Task OrganizeAsync_ExtractsJapaneseMetadataAndRelativeDueDate()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
        var organizer = new RuleBasedTaskOrganizer(clock);

        var result = await organizer.OrganizeAsync(
            "タスク：見積書を確認\n担当：田中さん\n期限：明日",
            CancellationToken.None);

        Assert.Equal("見積書を確認", result.Title);
        Assert.Equal("田中さん", result.Assignee);
        Assert.Equal(new DateOnly(2026, 7, 19), result.DueDate);
        Assert.Contains("見積書", result.Description);
        Assert.Empty(result.Subtasks);
    }

    [Theory]
    [InlineData("請求書を送る 期限: 2026-08-02", 2026, 8, 2)]
    [InlineData("請求書を送る 期限: 8/2", 2026, 8, 2)]
    public async Task OrganizeAsync_ParsesAbsoluteDate(string input, int year, int month, int day)
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
        var organizer = new RuleBasedTaskOrganizer(clock);

        var result = await organizer.OrganizeAsync(input, CancellationToken.None);

        Assert.Equal(new DateOnly(year, month, day), result.DueDate);
    }

    [Fact]
    public async Task OrganizeAsync_LimitsLongTitle()
    {
        var organizer = new RuleBasedTaskOrganizer(TimeProvider.System);
        var result = await organizer.OrganizeAsync(new string('あ', 250), CancellationToken.None);

        Assert.Equal(120, result.Title.Length);
        Assert.EndsWith("...", result.Title);
    }

    [Fact]
    public async Task OrganizeAsync_PreservesExplicitBulletSubtasksForFallback()
    {
        var organizer = new RuleBasedTaskOrganizer(TimeProvider.System);

        var result = await organizer.OrganizeAsync(
            "カレーを作る\n- レシピを決める\n- 冷蔵庫を確認する\n- 買い物に行く",
            CancellationToken.None);

        Assert.Equal(
            ["レシピを決める", "冷蔵庫を確認する", "買い物に行く"],
            result.Subtasks);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
