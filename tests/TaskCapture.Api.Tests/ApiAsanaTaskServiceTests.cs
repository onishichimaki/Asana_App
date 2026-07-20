using System.Net;
using System.Text;
using System.Text.Json;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class ApiAsanaTaskServiceTests
{
    [Theory]
    [InlineData(null, "1216674009964669")]
    [InlineData("   ", "1216674009964669")]
    [InlineData("111222333", "111222333")]
    public async Task CreateTaskAsync_UsesCandidateProjectBeforeDefaultProject(
        string? candidateProjectGid,
        string expectedProjectGid)
    {
        var handler = new RecordingHandler(
            """{"data":{"gid":"987654321","permalink_url":"https://app.asana.com/0/0/987654321"}}""");
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://app.asana.com/api/1.0/")
        };
        var options = Microsoft.Extensions.Options.Options.Create(new AsanaOptions
        {
            Mode = "Api",
            PersonalAccessToken = "test-secret",
            DefaultWorkspaceGid = "1216675064179067",
            DefaultProjectGid = "1216674009964669"
        });
        var service = new ApiAsanaTaskService(client, options);
        var candidate = new TaskCandidate
        {
            Title = "既定プロジェクト登録テスト",
            Description = "Asana API adapter test",
            ProjectGid = candidateProjectGid,
            TagsJson = "[]",
            CustomFieldsJson = "{}"
        };

        var result = await service.CreateTaskAsync(candidate, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("987654321", result.ExternalTaskGid);
        Assert.NotNull(handler.RequestBody);
        using var document = JsonDocument.Parse(handler.RequestBody);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(expectedProjectGid, data.GetProperty("projects")[0].GetString());
        Assert.False(data.TryGetProperty("workspace", out _));
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
