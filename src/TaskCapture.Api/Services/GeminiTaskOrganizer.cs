using System.Globalization;
using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;
using GeminiSchemaType = Google.GenAI.Types.Type;

namespace TaskCapture.Api.Services;

public interface IGeminiTaskClient
{
    Task<string> GenerateTaskJsonAsync(
        string systemInstruction,
        string rawText,
        CancellationToken cancellationToken);
}

public sealed class GoogleGeminiTaskClient(IOptions<TaskOrganizationOptions> options) : IGeminiTaskClient
{
    private static readonly Schema TaskSchema = new()
    {
        Title = "AsanaTaskCandidate",
        Type = GeminiSchemaType.Object,
        Description = "入力から整理したAsana登録用の親タスク候補と、必要な場合の実行可能なサブタスク。",
        Properties = new Dictionary<string, Schema>
        {
            ["title"] = new()
            {
                Type = GeminiSchemaType.String,
                Description = "実行する作業がひと目で分かる、簡潔な日本語タイトル。"
            },
            ["description"] = new()
            {
                Type = GeminiSchemaType.String,
                Description = "作業に必要な背景、条件、参照情報を含むタスク内容。"
            },
            ["assignee"] = new()
            {
                Type = GeminiSchemaType.String,
                Nullable = true,
                Description = "入力に明記された担当者。明記されていなければnull。"
            },
            ["dueDate"] = new()
            {
                Type = GeminiSchemaType.String,
                Format = "date",
                Nullable = true,
                Description = "入力に明記または相対表現された期限をyyyy-MM-ddで表した値。不明ならnull。"
            },
            ["subtasks"] = new()
            {
                Type = GeminiSchemaType.Array,
                Description = "親タスクを実行するために役立つ、実行順の具体的なサブタスク。分解不要なら空配列。",
                Items = new Schema
                {
                    Type = GeminiSchemaType.String,
                    Description = "200文字以内の簡潔な実行項目。"
                }
            }
        },
        PropertyOrdering = ["title", "description", "assignee", "dueDate", "subtasks"],
        Required = ["title", "description", "assignee", "dueDate", "subtasks"]
    };

    public async Task<string> GenerateTaskJsonAsync(
        string systemInstruction,
        string rawText,
        CancellationToken cancellationToken)
    {
        var geminiOptions = options.Value.Gemini;
        var apiKey = string.IsNullOrWhiteSpace(geminiOptions.ApiKey)
            ? System.Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            : geminiOptions.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API is not configured. Set TaskOrganization:Gemini:ApiKey or GEMINI_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(geminiOptions.Model))
        {
            throw new InvalidOperationException("TaskOrganization:Gemini:Model is required.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(geminiOptions.TimeoutSeconds, 5, 120)));

        try
        {
            var client = new Client(apiKey: apiKey);
            var response = await client.Models.GenerateContentAsync(
                model: geminiOptions.Model,
                contents: rawText,
                config: new GenerateContentConfig
                {
                    SystemInstruction = new Content
                    {
                        Parts = [new Part { Text = systemInstruction }]
                    },
                    Temperature = 0.1,
                    MaxOutputTokens = 2_048,
                    ResponseMimeType = "application/json",
                    ResponseSchema = TaskSchema
                },
                cancellationToken: timeout.Token);

            var text = response.Candidates?
                .FirstOrDefault()?
                .Content?
                .Parts?
                .FirstOrDefault(part => part.Thought != true && !string.IsNullOrWhiteSpace(part.Text))?
                .Text;
            return string.IsNullOrWhiteSpace(text)
                ? throw new InvalidOperationException("Gemini returned an empty task candidate.")
                : text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Gemini task organization timed out.");
        }
    }
}

public sealed class GeminiTaskOrganizer(
    IGeminiTaskClient client,
    TimeProvider timeProvider) : ITaskOrganizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(),
            ResolveJapanTimeZone()).DateTime);
        var systemInstruction = $"""
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

        var json = await client.GenerateTaskJsonAsync(systemInstruction, rawText, cancellationToken);
        GeminiTaskResult? result;
        try
        {
            result = JsonSerializer.Deserialize<GeminiTaskResult>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Gemini returned an invalid task candidate.", ex);
        }

        if (result is null || string.IsNullOrWhiteSpace(result.Title))
        {
            throw new InvalidOperationException("Gemini did not return a task title.");
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

    private sealed record GeminiTaskResult(
        string Title,
        string Description,
        string? Assignee,
        string? DueDate,
        IReadOnlyList<string>? Subtasks);
}

public sealed class FallbackTaskOrganizer(
    ITaskOrganizer primary,
    ITaskOrganizer fallback,
    ILogger<FallbackTaskOrganizer> logger) : ITaskOrganizer
{
    public async Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken)
    {
        try
        {
            return await primary.OrganizeAsync(rawText, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "AI task organization failed with {ExceptionType}; using the rule-based fallback.",
                ex.GetType().Name);
            return await fallback.OrganizeAsync(rawText, cancellationToken);
        }
    }
}
