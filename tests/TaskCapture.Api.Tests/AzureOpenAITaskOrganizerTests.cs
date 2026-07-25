using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class AzureOpenAITaskOrganizerTests
{
    [Fact]
    public async Task Organizer_UsesSharedStructuredTaskRules()
    {
        var client = new StubAzureClient("""
            {
              "title": " カレーを作る ",
              "description": "夕食のカレーを準備する",
              "assignee": "大西",
              "dueDate": "2026-10-03",
              "subtasks": ["レシピを決める", "材料を確認する"]
            }
            """);
        var organizer = new AzureOpenAITaskOrganizer(client, TimeProvider.System);

        var result = await organizer.OrganizeAsync("カレーを作る、大西", CancellationToken.None);

        Assert.Equal("カレーを作る", result.Title);
        Assert.Equal("大西", result.Assignee);
        Assert.Equal(new DateOnly(2026, 10, 3), result.DueDate);
        Assert.Equal(["レシピを決める", "材料を確認する"], result.Subtasks);
        Assert.Contains("入力にない担当者や期限を推測しない", client.SystemInstruction);
    }

    [Fact]
    public async Task Client_SendsAzureV1StructuredOutputRequest()
    {
        const string taskJson = """
            {"title":"確認する","description":"内容","assignee":null,"dueDate":null,"subtasks":[]}
            """;
        var responseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = taskJson } } }
        });
        var handler = new RecordingHandler(responseJson);
        var options = Microsoft.Extensions.Options.Options.Create(new TaskOrganizationOptions
        {
            AzureOpenAI = new AzureOpenAIOptions
            {
                Endpoint = "https://example.openai.azure.com",
                ApiKey = "test-api-key",
                DeploymentName = "task-gpt"
            }
        });
        var client = new AzureOpenAITaskClient(options, new TestHttpClientFactory(handler));

        var result = await client.GenerateTaskJsonAsync("system", "user input", CancellationToken.None);

        Assert.Equal(taskJson, result);
        Assert.Equal(
            "https://example.openai.azure.com/openai/v1/chat/completions",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal("test-api-key", handler.ApiKey);
        Assert.Contains("\"model\":\"task-gpt\"", handler.Body);
        Assert.Contains("\"type\":\"json_schema\"", handler.Body);
        Assert.Contains("\"additionalProperties\":false", handler.Body);
    }

    private sealed class StubAzureClient(string json) : IAzureOpenAITaskClient
    {
        public string SystemInstruction { get; private set; } = string.Empty;

        public Task<string> GenerateTaskJsonAsync(
            string systemInstruction,
            string rawText,
            CancellationToken cancellationToken)
        {
            SystemInstruction = systemInstruction;
            return Task.FromResult(json);
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("api-key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
