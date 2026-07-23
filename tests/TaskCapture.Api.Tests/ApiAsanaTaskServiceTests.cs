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

    [Fact]
    public async Task CreateSubtaskAsync_PostsToParentSubtasksEndpoint()
    {
        var handler = new RecordingHandler(
            """{"data":{"gid":"555666777","permalink_url":"https://app.asana.com/0/0/555666777"}}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://app.asana.com/api/1.0/") };
        var options = Microsoft.Extensions.Options.Options.Create(new AsanaOptions
        {
            Mode = "Api",
            PersonalAccessToken = "test-secret",
            DefaultProjectGid = "1216674009964669"
        });
        var service = new ApiAsanaTaskService(client, options);
        var candidate = new TaskCandidate
        {
            Title = "カレーを作る",
            Description = "カレーを作る。",
            Assignee = "me",
            DueDate = new DateOnly(2026, 7, 21),
            TagsJson = "[]",
            CustomFieldsJson = "{}"
        };
        var subtask = new TaskCandidateSubtask { Title = "冷蔵庫の食材を確認する" };

        var result = await service.CreateSubtaskAsync(
            candidate,
            subtask,
            "111222333",
            "444555666",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("/tasks/111222333/subtasks?", handler.RequestUri);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("冷蔵庫の食材を確認する", data.GetProperty("name").GetString());
        Assert.Equal("444555666", data.GetProperty("assignee").GetString());
        Assert.Equal("2026-07-21", data.GetProperty("due_on").GetString());
    }

    [Fact]
    public async Task CreateTaskAsync_ResolvesUniquePartialWorkspaceUserName()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
                """{"data":[{"gid":"444555666","name":"大西 千茉季"},{"gid":"777888999","name":"田中 太郎"}]}"""),
            (HttpStatusCode.Created,
                """{"data":{"gid":"987654321","permalink_url":"https://app.asana.com/0/0/987654321","assignee":{"gid":"444555666","name":"大西 千茉季"}}}"""));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://app.asana.com/api/1.0/") };
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
            Title = "担当者解決テスト",
            Description = "大西を一意に解決する。",
            Assignee = "大西",
            TagsJson = "[]",
            CustomFieldsJson = "{}"
        };

        var result = await service.CreateTaskAsync(candidate, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Resolved", result.AssigneeResolutionStatus);
        Assert.Equal("444555666", result.ResolvedAssigneeGid);
        Assert.Equal("大西 千茉季", result.ResolvedAssigneeName);
        Assert.Null(result.WarningMessage);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("/workspaces/1216675064179067/users?", handler.Requests[0].Uri);
        using var requestDocument = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(
            "444555666",
            requestDocument.RootElement.GetProperty("data").GetProperty("assignee").GetString());
    }

    [Fact]
    public async Task CreateTaskAsync_DoesNotGuessWhenWorkspaceNameIsAmbiguous()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
                """{"data":[{"gid":"111","name":"大西 千茉季"},{"gid":"222","name":"大西 太郎"}]}"""),
            (HttpStatusCode.Created,
                """{"data":{"gid":"987654321","permalink_url":"https://app.asana.com/0/0/987654321","assignee":null}}"""));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://app.asana.com/api/1.0/") };
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
            Title = "担当者曖昧テスト",
            Description = "曖昧な名前を割り当てない。",
            Assignee = "大西",
            TagsJson = "[]",
            CustomFieldsJson = "{}"
        };

        var result = await service.CreateTaskAsync(candidate, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Ambiguous", result.AssigneeResolutionStatus);
        Assert.Null(result.ResolvedAssigneeGid);
        Assert.NotNull(result.WarningMessage);
        using var requestDocument = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.False(requestDocument.RootElement.GetProperty("data").TryGetProperty("assignee", out _));
    }

    [Fact]
    public async Task CreateTaskAsync_SearchesAllWorkspaceUserPages()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
                """{"data":[{"gid":"111","name":"田中 太郎"}],"next_page":{"offset":"second-page"}}"""),
            (HttpStatusCode.OK,
                """{"data":[{"gid":"444555666","name":"大西 千茉季"}],"next_page":null}"""),
            (HttpStatusCode.Created,
                """{"data":{"gid":"987654321","permalink_url":"https://app.asana.com/0/0/987654321","assignee":{"gid":"444555666","name":"大西 千茉季"}}}"""));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://app.asana.com/api/1.0/") };
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
            Title = "担当者ページングテスト",
            Description = "2ページ目の利用者を解決する。",
            Assignee = "大西",
            TagsJson = "[]",
            CustomFieldsJson = "{}"
        };

        var result = await service.CreateTaskAsync(candidate, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("444555666", result.ResolvedAssigneeGid);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("offset=second-page", handler.Requests[1].Uri);
        using var requestDocument = JsonDocument.Parse(handler.Requests[2].Body!);
        Assert.Equal(
            "444555666",
            requestDocument.RootElement.GetProperty("data").GetProperty("assignee").GetString());
    }

    [Fact]
    public async Task CreateTaskAsync_WarnsWhenAsanaDoesNotApplyDirectAssignee()
    {
        var handler = new RecordingHandler(
            """{"data":{"gid":"987654321","permalink_url":"https://app.asana.com/0/0/987654321","assignee":null}}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://app.asana.com/api/1.0/") };
        var options = Microsoft.Extensions.Options.Options.Create(new AsanaOptions
        {
            Mode = "Api",
            PersonalAccessToken = "test-secret",
            DefaultProjectGid = "1216674009964669"
        });
        var service = new ApiAsanaTaskService(client, options);
        var candidate = new TaskCandidate
        {
            Title = "担当者未適用テスト",
            Description = "Asana応答を正本にする。",
            Assignee = "me",
            TagsJson = "[]",
            CustomFieldsJson = "{}"
        };

        var result = await service.CreateTaskAsync(candidate, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("NotApplied", result.AssigneeResolutionStatus);
        Assert.Null(result.ResolvedAssigneeGid);
        Assert.Null(result.ResolvedAssigneeName);
        Assert.NotNull(result.WarningMessage);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Body);

    private sealed class SequenceHandler(
        params (HttpStatusCode StatusCode, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses = new(responses);
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                body));
            var response = _responses.Dequeue();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
