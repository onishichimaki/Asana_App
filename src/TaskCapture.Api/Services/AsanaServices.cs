using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
    string? ErrorMessage = null,
    string? AssigneeResolutionStatus = null,
    string? ResolvedAssigneeGid = null,
    string? ResolvedAssigneeName = null,
    string? WarningMessage = null);

public sealed record AsanaImportTask(
    string Title,
    string Description,
    string? Assignee,
    DateOnly? DueDate,
    string? ProjectGid,
    string? SectionGid);

public interface IAsanaTaskService
{
    Task<AsanaRegistrationResult> CreateTaskAsync(TaskCandidate candidate, CancellationToken cancellationToken);
    Task<AsanaRegistrationResult> CreateSubtaskAsync(
        TaskCandidate candidate,
        TaskCandidateSubtask subtask,
        string parentTaskGid,
        string? resolvedAssigneeGid,
        CancellationToken cancellationToken);
    Task<AsanaRegistrationResult> CreateImportTaskAsync(
        AsanaImportTask task,
        string? parentTaskGid,
        CancellationToken cancellationToken);
}

public sealed class MockAsanaTaskService : IAsanaTaskService
{
    public Task<AsanaRegistrationResult> CreateTaskAsync(TaskCandidate candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gid = $"mock-{Guid.NewGuid():N}";
        var assigneeStatus = string.IsNullOrWhiteSpace(candidate.Assignee) ? "NotRequested" : "Mock";
        return Task.FromResult(new AsanaRegistrationResult(
            true,
            "Mock",
            gid,
            null,
            AssigneeResolutionStatus: assigneeStatus,
            ResolvedAssigneeName: NullIfWhiteSpace(candidate.Assignee)));
    }

    public Task<AsanaRegistrationResult> CreateSubtaskAsync(
        TaskCandidate candidate,
        TaskCandidateSubtask subtask,
        string parentTaskGid,
        string? resolvedAssigneeGid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gid = $"mock-{Guid.NewGuid():N}";
        return Task.FromResult(new AsanaRegistrationResult(true, "Mock", gid, null));
    }

    public Task<AsanaRegistrationResult> CreateImportTaskAsync(
        AsanaImportTask task,
        string? parentTaskGid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gid = $"mock-{Guid.NewGuid():N}";
        var assigneeStatus = string.IsNullOrWhiteSpace(task.Assignee) ? "NotRequested" : "Mock";
        return Task.FromResult(new AsanaRegistrationResult(
            true,
            "Mock",
            gid,
            null,
            AssigneeResolutionStatus: assigneeStatus,
            ResolvedAssigneeName: NullIfWhiteSpace(task.Assignee)));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ApiAsanaTaskService(HttpClient httpClient, IOptions<AsanaOptions> options) : IAsanaTaskService
{
    private const string TaskResponseFields = "gid,permalink_url,assignee.gid,assignee.name";
    private readonly AsanaOptions _options = options.Value;

    public async Task<AsanaRegistrationResult> CreateTaskAsync(
        TaskCandidate candidate,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var projectGid = NullIfWhiteSpace(candidate.ProjectGid) ?? NullIfWhiteSpace(_options.DefaultProjectGid);
        if (projectGid is null && string.IsNullOrWhiteSpace(_options.DefaultWorkspaceGid))
        {
            throw new InvalidOperationException(
                "Asana API mode requires a candidate/default project GID or DefaultWorkspaceGid.");
        }

        var assignee = await ResolveAssigneeAsync(candidate.Assignee, cancellationToken);
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
        if (assignee.Gid is not null)
        {
            data["assignee"] = assignee.Gid;
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
            $"tasks?opt_fields={TaskResponseFields}",
            data,
            assignee,
            "Asana rejected the task registration. Check server logs and integration settings.",
            cancellationToken);
    }

    public async Task<AsanaRegistrationResult> CreateSubtaskAsync(
        TaskCandidate candidate,
        TaskCandidateSubtask subtask,
        string parentTaskGid,
        string? resolvedAssigneeGid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(parentTaskGid))
        {
            throw new ArgumentException("A parent Asana task GID is required.", nameof(parentTaskGid));
        }

        var assigneeGid = NullIfWhiteSpace(resolvedAssigneeGid);
        if (assigneeGid is null && IsDirectAssignee(candidate.Assignee))
        {
            assigneeGid = candidate.Assignee!.Trim();
        }

        var data = new Dictionary<string, object?>
        {
            ["name"] = subtask.Title,
            ["due_on"] = candidate.DueDate?.ToString("yyyy-MM-dd")
        };
        if (assigneeGid is not null)
        {
            data["assignee"] = assigneeGid;
        }

        return await SendCreateAsync(
            $"tasks/{Uri.EscapeDataString(parentTaskGid)}/subtasks?opt_fields={TaskResponseFields}",
            data,
            AssigneeResolution.NotRequested,
            "Asana rejected a subtask registration. Check server logs and integration settings.",
            cancellationToken);
    }

    public async Task<AsanaRegistrationResult> CreateImportTaskAsync(
        AsanaImportTask task,
        string? parentTaskGid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var assignee = await ResolveAssigneeAsync(task.Assignee, cancellationToken);
        var data = new Dictionary<string, object?>
        {
            ["name"] = task.Title,
            ["notes"] = task.Description,
            ["due_on"] = task.DueDate?.ToString("yyyy-MM-dd")
        };
        if (assignee.Gid is not null)
        {
            data["assignee"] = assignee.Gid;
        }

        string requestUri;
        if (string.IsNullOrWhiteSpace(parentTaskGid))
        {
            var projectGid = NullIfWhiteSpace(task.ProjectGid) ?? NullIfWhiteSpace(_options.DefaultProjectGid);
            if (projectGid is null && string.IsNullOrWhiteSpace(_options.DefaultWorkspaceGid))
            {
                throw new InvalidOperationException(
                    "Asana API mode requires an import/default project GID or DefaultWorkspaceGid.");
            }
            if (task.SectionGid is not null && projectGid is not null)
            {
                data["memberships"] = new[] { new { project = projectGid, section = task.SectionGid } };
            }
            else if (projectGid is not null)
            {
                data["projects"] = new[] { projectGid };
            }
            else
            {
                data["workspace"] = _options.DefaultWorkspaceGid;
            }
            requestUri = $"tasks?opt_fields={TaskResponseFields}";
        }
        else
        {
            requestUri =
                $"tasks/{Uri.EscapeDataString(parentTaskGid.Trim())}/subtasks?opt_fields={TaskResponseFields}";
        }

        return await SendCreateAsync(
            requestUri,
            data,
            assignee,
            "Asana rejected a WBS task registration. Check server logs and integration settings.",
            cancellationToken);
    }

    private async Task<AssigneeResolution> ResolveAssigneeAsync(
        string? requestedAssignee,
        CancellationToken cancellationToken)
    {
        var requested = NullIfWhiteSpace(requestedAssignee);
        if (requested is null) return AssigneeResolution.NotRequested;
        if (IsDirectAssignee(requested))
        {
            return new AssigneeResolution("Direct", requested, requested, null, null);
        }

        var workspaceGid = NullIfWhiteSpace(_options.DefaultWorkspaceGid);
        if (workspaceGid is null)
        {
            return new AssigneeResolution(
                "WorkspaceMissing",
                requested,
                null,
                null,
                "担当者名を検索するには、サーバー側のDefaultWorkspaceGid設定が必要です。タスクは未割り当てで登録しました。");
        }

        var users = new List<AsanaUser>();
        string? offset = null;
        do
        {
            var requestUri =
                $"workspaces/{Uri.EscapeDataString(workspaceGid)}/users?limit=100&opt_fields=gid,name";
            if (offset is not null)
            {
                requestUri += $"&offset={Uri.EscapeDataString(offset)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            AddAuthorization(request);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AssigneeResolution(
                    "LookupFailed",
                    requested,
                    null,
                    null,
                    "Asana担当者一覧を取得できなかったため、タスクは未割り当てで登録しました。");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            users.AddRange(document.RootElement.GetProperty("data")
                .EnumerateArray()
                .Select(element => new AsanaUser(
                    element.TryGetProperty("gid", out var gid) ? gid.GetString() : null,
                    element.TryGetProperty("name", out var name) ? name.GetString() : null))
                .Where(user => user.Gid is not null && user.Name is not null));
            offset = document.RootElement.TryGetProperty("next_page", out var nextPage)
                && nextPage.ValueKind == JsonValueKind.Object
                && nextPage.TryGetProperty("offset", out var offsetElement)
                ? NullIfWhiteSpace(offsetElement.GetString())
                : null;
        }
        while (offset is not null);

        var normalizedRequested = NormalizeName(requested);
        var exactMatches = users
            .Where(user => NormalizeName(user.Name!) == normalizedRequested)
            .ToArray();
        if (exactMatches.Length == 1)
        {
            return Resolved(requested, exactMatches[0]);
        }
        if (exactMatches.Length > 1)
        {
            return Ambiguous(requested);
        }

        var partialMatches = users
            .Where(user => NormalizeName(user.Name!).Contains(normalizedRequested, StringComparison.Ordinal))
            .ToArray();
        return partialMatches.Length switch
        {
            1 => Resolved(requested, partialMatches[0]),
            > 1 => Ambiguous(requested),
            _ => new AssigneeResolution(
                "NotFound",
                requested,
                null,
                null,
                $"担当者「{requested}」をAsanaで一意に特定できなかったため、タスクは未割り当てで登録しました。")
        };
    }

    private async Task<AsanaRegistrationResult> SendCreateAsync(
        string requestUri,
        IReadOnlyDictionary<string, object?> data,
        AssigneeResolution assignee,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new { data })
        };
        AddAuthorization(request);
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
                failureMessage,
                assignee.Status,
                assignee.Gid,
                assignee.Name,
                assignee.WarningMessage);
        }

        using var document = JsonDocument.Parse(body);
        var task = document.RootElement.GetProperty("data");
        var gid = task.GetProperty("gid").GetString();
        var url = task.TryGetProperty("permalink_url", out var urlElement) ? urlElement.GetString() : null;
        var actualAssignee = task.TryGetProperty("assignee", out var assigneeElement)
            && assigneeElement.ValueKind == JsonValueKind.Object
            ? new AsanaUser(
                assigneeElement.TryGetProperty("gid", out var assigneeGid) ? assigneeGid.GetString() : null,
                assigneeElement.TryGetProperty("name", out var assigneeName) ? assigneeName.GetString() : null)
            : null;

        var status = assignee.Status;
        var warning = assignee.WarningMessage;
        if (assignee.Gid is not null && actualAssignee?.Gid is null)
        {
            status = "NotApplied";
            warning = "Asanaタスクは作成されましたが、担当者を割り当てられませんでした。";
        }
        else if (actualAssignee?.Gid is not null)
        {
            status = assignee.Status == "Direct" ? "Direct" : "Resolved";
        }

        return new AsanaRegistrationResult(
            true,
            "AsanaApi",
            gid,
            url,
            AssigneeResolutionStatus: status,
            ResolvedAssigneeGid: status == "NotApplied" ? null : actualAssignee?.Gid ?? assignee.Gid,
            ResolvedAssigneeName: status == "NotApplied" ? null : actualAssignee?.Name ?? assignee.Name,
            WarningMessage: warning);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
        {
            throw new InvalidOperationException("Asana API mode requires Integration:Asana:PersonalAccessToken.");
        }
    }

    private void AddAuthorization(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);

    private static bool IsDirectAssignee(string? value) =>
        value is not null && (value.Trim() == "me" || value.Trim().All(char.IsDigit));

    private static AssigneeResolution Resolved(string requested, AsanaUser user) =>
        new("Resolved", requested, user.Gid, user.Name, null);

    private static AssigneeResolution Ambiguous(string requested) =>
        new(
            "Ambiguous",
            requested,
            null,
            null,
            $"担当者「{requested}」に一致するAsanaユーザーが複数いるため、タスクは未割り当てで登録しました。");

    private static string NormalizeName(string value) =>
        string.Concat(value.Normalize(NormalizationForm.FormKC).Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AsanaUser(string? Gid, string? Name);
    private sealed record AssigneeResolution(
        string Status,
        string? Requested,
        string? Gid,
        string? Name,
        string? WarningMessage)
    {
        public static AssigneeResolution NotRequested { get; } =
            new("NotRequested", null, null, null, null);
    }
}
