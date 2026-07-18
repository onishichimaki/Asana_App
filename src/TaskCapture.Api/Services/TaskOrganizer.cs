using System.Globalization;
using System.Text.RegularExpressions;

namespace TaskCapture.Api.Services;

public sealed record OrganizedTask(string Title, string Description, string? Assignee, DateOnly? DueDate);

public interface ITaskOrganizer
{
    Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken);
}

public sealed partial class RuleBasedTaskOrganizer(TimeProvider timeProvider) : ITaskOrganizer
{
    public Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = rawText.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return Task.FromResult(new OrganizedTask(
            CreateTitle(normalized),
            normalized,
            ExtractAssignee(normalized),
            ExtractDueDate(normalized)));
    }

    private static string CreateTitle(string text)
    {
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "新しいタスク";
        firstLine = BulletPrefixRegex().Replace(firstLine, string.Empty);
        firstLine = TaskPrefixRegex().Replace(firstLine, string.Empty).Trim();
        firstLine = MetadataSuffixRegex().Replace(firstLine, string.Empty).Trim(' ', '。', '.');
        if (string.IsNullOrWhiteSpace(firstLine)) return "新しいタスク";
        return firstLine.Length <= 120 ? firstLine : $"{firstLine[..117].TrimEnd()}...";
    }

    private static string? ExtractAssignee(string text)
    {
        var match = AssigneeRegex().Match(text);
        if (match.Success) return match.Groups["value"].Value.Trim();
        match = MentionRegex().Match(text);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private DateOnly? ExtractDueDate(string text)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(),
            ResolveJapanTimeZone()).DateTime);

        if (text.Contains("明後日", StringComparison.Ordinal)) return today.AddDays(2);
        if (text.Contains("明日", StringComparison.Ordinal)) return today.AddDays(1);
        if (text.Contains("今日", StringComparison.Ordinal) || text.Contains("本日", StringComparison.Ordinal)) return today;

        var match = AbsoluteDateRegex().Match(text);
        if (!match.Success) return null;

        var value = match.Groups["date"].Value.Replace('.', '/').Replace('-', '/');
        var hasYear = value.Count(ch => ch == '/') == 2;
        var formats = hasYear ? new[] { "yyyy/M/d", "yyyy/MM/dd" } : new[] { "M/d", "MM/dd" };
        if (!DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        return hasYear ? parsed : new DateOnly(today.Year, parsed.Month, parsed.Day);
    }

    private static TimeZoneInfo ResolveJapanTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"); }
    }

    [GeneratedRegex(@"^[\s\-–—*・●○□■]+")]
    private static partial Regex BulletPrefixRegex();

    [GeneratedRegex(@"^(?:タスク|todo|やること)\s*[:：]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TaskPrefixRegex();

    [GeneratedRegex(@"\s+(?:担当(?:者)?|期限|締切|期日)\s*[:：].*$", RegexOptions.IgnoreCase)]
    private static partial Regex MetadataSuffixRegex();

    [GeneratedRegex(@"(?:担当(?:者)?)\s*[:：]\s*(?<value>[^\r\n、,;；]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AssigneeRegex();

    [GeneratedRegex(@"(?:^|\s)@(?<value>[\p{L}\p{N}_\-.]+)")]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"(?:(?:期限|締切|期日)\s*[:：]?\s*)?(?<date>(?:\d{4}[/\-.])?\d{1,2}[/\-.]\d{1,2})")]
    private static partial Regex AbsoluteDateRegex();
}
