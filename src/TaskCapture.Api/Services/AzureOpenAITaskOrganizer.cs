using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Services;

public interface IAzureOpenAITaskClient
{
    Task<string> GenerateTaskJsonAsync(
        string systemInstruction,
        string rawText,
        CancellationToken cancellationToken);
}

public sealed class AzureOpenAITaskClient(
    IOptions<TaskOrganizationOptions> options,
    IHttpClientFactory httpClientFactory) : IAzureOpenAITaskClient
{
    private static readonly JsonElement TaskSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "実行する作業がひと目で分かる簡潔な日本語タイトル" },
            "description": { "type": "string", "description": "作業に必要な背景、条件、参照情報を含むタスク内容" },
            "assignee": { "type": ["string", "null"], "description": "入力に明記された担当者。明記されていなければnull" },
            "dueDate": { "type": ["string", "null"], "description": "期限をyyyy-MM-ddで表した値。不明ならnull" },
            "subtasks": {
              "type": "array",
              "description": "必要な場合の実行順の具体的なサブタスク。分解不要なら空配列",
              "items": { "type": "string" }
            }
          },
          "required": ["title", "description", "assignee", "dueDate", "subtasks"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<string> GenerateTaskJsonAsync(
        string systemInstruction,
        string rawText,
        CancellationToken cancellationToken)
    {
        var configured = options.Value.AzureOpenAI;
        var apiKey = string.IsNullOrWhiteSpace(configured.ApiKey)
            ? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            : configured.ApiKey;
        if (string.IsNullOrWhiteSpace(configured.Endpoint)
            || string.IsNullOrWhiteSpace(configured.DeploymentName)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Azure OpenAI is not configured. Set Endpoint, DeploymentName, and AZURE_OPENAI_API_KEY.");
        }

        var requestUri = BuildRequestUri(configured.Endpoint);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configured.TimeoutSeconds, 5, 120)));
        var client = httpClientFactory.CreateClient("AzureOpenAI");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new
            {
                model = configured.DeploymentName,
                messages = new object[]
                {
                    new { role = "system", content = systemInstruction },
                    new { role = "user", content = rawText }
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "asana_task_candidate",
                        strict = true,
                        schema = TaskSchema
                    }
                }
            })
        };
        request.Headers.Add("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Azure OpenAI request failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(content)
                ? throw new InvalidOperationException("Azure OpenAI returned an empty task candidate.")
                : content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Azure OpenAI task organization timed out.");
        }
    }

    private static Uri BuildRequestUri(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var parsed)
            || !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Azure OpenAI Endpoint must be an absolute HTTPS URL.");
        }

        var root = parsed.AbsoluteUri.TrimEnd('/');
        if (!root.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            root += "/openai/v1";
        }
        return new Uri(root + "/chat/completions");
    }
}

public sealed class AzureOpenAITaskOrganizer(
    IAzureOpenAITaskClient client,
    TimeProvider timeProvider) : ITaskOrganizer
{
    public async Task<OrganizedTask> OrganizeAsync(string rawText, CancellationToken cancellationToken)
    {
        var instruction = StructuredTaskOrganization.CreateSystemInstruction(timeProvider);
        var json = await client.GenerateTaskJsonAsync(instruction, rawText, cancellationToken);
        return StructuredTaskOrganization.Parse(json, rawText, "Azure OpenAI");
    }
}
