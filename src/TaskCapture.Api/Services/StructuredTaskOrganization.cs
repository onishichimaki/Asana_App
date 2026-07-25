using System.Globalization;
using System.Text.Json;

namespace TaskCapture.Api.Services;

internal static class StructuredTaskOrganization
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static string CreateSystemInstruction(TimeProvider timeProvider)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(),
            ResolveJapanTimeZone()).DateTime);
        return $"""
            入力文からAsanaへ登録する親タスク候補を1件整理してください。
            今日の日付は日本時間で{today:yyyy-MM-dd}です。「今日」「明日」などの相対期限はこの日付を基準にしてください。
            入力にない担当者や期限を推測しないでください。タイトルは120文字以内にしてください。
            内容には作業の背景と必要情報を残し、入力にない事実を追加しないでください。
            親タスクが複数の行動を必要とする場合は、一般的に必要な実行手順を0〜6件のsubtasksへ分解してください。
            各subtaskは200文字以内の動詞を含む具体的な作業にし、実行順に並べ、親タイトルの単なる言い換えは避けてください。
            単純で一工程のタスク、または分解が役に立たないタスクではsubtasksを空配列にしてください。
            サブタスクでは一般的な手順を補って構いませんが、入力にない担当者、期限、固有名詞、数量、認証情報は作らないでください。
            例: 「カレーを作る、大西」なら、titleは「カレーを作る」、assigneeは「大西」、subtasksは
            「レシピを決める」「冷蔵庫の食材を確認する」「不足している食材を買う」「カレーを調理する」のように整理します。
            """;
    }

    public static OrganizedTask Parse(string json, string rawText, string providerName)
    {
        TaskResult? result;
        try
        {
            result = JsonSerializer.Deserialize<TaskResult>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{providerName} returned an invalid task candidate.", ex);
        }

        if (result is null || string.IsNullOrWhiteSpace(result.Title))
        {
            throw new InvalidOperationException($"{providerName} did not return a task title.");
        }

        var title = Limit(result.Title.Trim(), 120);
        var description = string.IsNullOrWhiteSpace(result.Description)
            ? rawText.Trim()
            : Limit(result.Description.Trim(), 10_000);
        var assignee = string.IsNullOrWhiteSpace(result.Assignee)
            ? null
            : Limit(result.Assignee.Trim(), 200);

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(result.DueDate)
            && DateOnly.TryParseExact(
                result.DueDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDueDate))
        {
            dueDate = parsedDueDate;
        }

        var subtasks = (result.Subtasks ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Limit(value.Trim(), 200))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        return new OrganizedTask(title, description, assignee, dueDate, subtasks);
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

    private static TimeZoneInfo ResolveJapanTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"); }
    }

    private sealed record TaskResult(
        string Title,
        string Description,
        string? Assignee,
        string? DueDate,
        IReadOnlyList<string>? Subtasks);
}
