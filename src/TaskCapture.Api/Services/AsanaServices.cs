using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Data;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Services;

public sealed record AsanaRegistrationResult(
    bool Succeeded,
    string Provider,
    string? ExternalTaskGid,
    string? ExternalTaskUrl,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IAsanaTaskService
{
    Task<AsanaRegistrationResult> CreateTaskAsync(TaskCandidate candidate, CancellationToken cancellationToken);
    Task<AsanaRegistrationResult> CreateSubtaskAsync(
        TaskCandidate candidate,
        TaskCandidateSubtask subtask,
        string parentTaskGid,
        CancellationToken cancellationToken);
}

public sealed class MockAsanaTaskService : IAsanaTaskService
{
    public Task<AsanaRegistrationResult> CreateTaskAsync(TaskCandidate candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gid = $"mock-{Guid.NewGuid():N}";
        return Task.FromResult(new AsanaRegistrationResult(true, "Mock", gid, null));
    }

    public Task<AsanaRegistrationResult> CreateSubtaskAsync(
        TaskCandidate candidate,
        TaskCandidateSubtask subtask,
        string parentTaskGid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gid = $"mock-{Guid.NewGuid():N}";
        return Task.FromResult(new AsanaRegistrationResult(true, "Mock", gid, null));
    }
}

public sealed class ApiAsanaTaskService(HttpClient httpClient, IOptions<AsanaOptions> options) : IAsanaTaskService
{
    private readonly AsanaOptions _options = options.Value;

    public async Task<AsanaRegistrationResult> CreateTaskAsync(
        TaskCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
        {
            throw new InvalidOperationException("Asana API mode requires Integration:Asana:PersonalAccessToken.");
        }

        var projectGid = NullIfWhiteSpace(candidate.ProjectGid) ?? NullIfWhiteSpace(_options.DefaultProjectGid);
        if (projectGid is null && string.IsNullOrWhiteSpace(_options.DefaultWorkspaceGid))
        {
            throw new InvalidOperationException(
                "Asana API mode requires a candidate/default project GID or DefaultWorkspaceGid.");
        }

        var notes = candidate.Description;
        if (!string.IsNullOrWhiteSpace(candidate.Priority))
        {
            notes = $"{notes}\n\nPriority: {candidate.Priority}".Trim();
        }

        var data = new Dictionary<string, object?>
        {
            ["name"] = candidate.Title,
            ["notes"] = notes,
            ["due_on"] = candidate.DueDate?.ToString("yyyy-MM-dd")
        };

        if (candidate.Assignee is "me" || (candidate.Assignee?.All(char.IsDigit) ?? false))
        {
            data["assignee"] = candidate.Assignee;
        }

        if (candidate.SectionGid is not null && projectGid is not null)
        {
            data["memberships"] = new[] { new { project = projectGid, section = candidate.SectionGid } };
        }
        else if (projectGid is not null)
        {
            data["projects"] = new[] { projectGid };
        }
        else
        {
            data["workspace"] = _options.DefaultWorkspaceGid;
        }

        var tags = JsonSerializer.Deserialize<string[]>(candidate.TagsJson) ?? [];
        if (tags.Length > 0) data["tags"] = tags;
        var customFields = JsonSerializer.Deserialize<Dictionary<string, string>>(candidate.CustomFieldsJson) ?? [];
        if (customFields.Count > 0) data["custom_fields"] = customFields;

        return await SendCreateAsync(
            "tasks",
            data,
            "Asana rejected the task registration. Check server logs and integration settings.",
            cancellationToken);
    }

    public async Task<AsanaRegistrationResult> CreateSubtaskAsync(
        TaskCandidate candidate,
        TaskCandidateSubtask subtask,
        string parentTaskGid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
        {
            throw new InvalidOperationException("Asana API mode requires Integration:Asana:PersonalAccessToken.");
        }

        if (string.IsNullOrWhiteSpace(parentTaskGid))
        {
            throw new ArgumentException("A parent Asana task GID is required.", nameof(parentTaskGid));
        }

        var data = new Dictionary<string, object?>
        {
            ["name"] = subtask.Title,
            ["due_on"] = candidate.DueDate?.ToString("yyyy-MM-dd")
        };
        if (candidate.Assignee is "me" || (candidate.Assignee?.All(char.IsDigit) ?? false))
        {
            data["assignee"] = candidate.Assignee;
        }

        return await SendCreateAsync(
            $"tasks/{Uri.EscapeDataString(parentTaskGid)}/subtasks",
            data,
            "Asana rejected a subtask registration. Check server logs and integration settings.",
            cancellationToken);
    }

    private async Task<AsanaRegistrationResult> SendCreateAsync(
        string requestUri,
        IReadOnlyDictionary<string, object?> data,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new { data })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AsanaRegistrationResult(
                false,
                "AsanaApi",
                null,
                null,
                $"HTTP_{(int)response.StatusCode}",
                failureMessage);
        }

        using var document = JsonDocument.Parse(body);
        var task = document.RootElement.GetProperty("data");
        var gid = task.GetProperty("gid").GetString();
        var url = task.TryGetProperty("permalink_url", out var urlElement) ? urlElement.GetString() : null;
        return new AsanaRegistrationResult(true, "AsanaApi", gid, url);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
