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
    public async Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken)
    {
        var systemInstruction = StructuredTaskOrganization.CreateSystemInstruction(timeProvider);
        var json = await client.GenerateTaskJsonAsync(systemInstruction, rawText, cancellationToken);
        return StructuredTaskOrganization.Parse(json, rawText, "Gemini");
    }
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
