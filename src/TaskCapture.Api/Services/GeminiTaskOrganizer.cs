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
        Description = "入力から抽出したAsana登録用のタスク候補1件。推測で情報を追加しない。",
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
            }
        },
        PropertyOrdering = ["title", "description", "assignee", "dueDate"],
        Required = ["title", "description", "assignee", "dueDate"]
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
                    MaxOutputTokens = 1_024,
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
            入力文からAsanaへ登録するタスク候補を1件だけ整理してください。
            今日の日付は日本時間で{today:yyyy-MM-dd}です。「今日」「明日」などの相対期限はこの日付を基準にしてください。
            入力にない担当者や期限を推測しないでください。タイトルは120文字以内にしてください。
            内容には作業の背景と必要情報を残し、入力にない事実を追加しないでください。
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

        return new OrganizedTask(title, description, assignee, dueDate);
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
        string? DueDate);
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
