using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
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
    DateOnly? StartDate,
    DateOnly? DueDate,
    string? ProjectGid,
    string? SectionGid,
    IReadOnlyList<AsanaCustomFieldInput>? ExtraFields = null);

public sealed record AsanaProjectOption(string Gid, string Name, bool IsFavorite = false);
public sealed record AsanaSectionOption(string Gid, string Name);
public sealed record AsanaProjectCatalog(string? DefaultProjectGid, IReadOnlyList<AsanaProjectOption> Projects);
public sealed record AsanaAssigneeResolution(
    string RequestedName,
    string Status,
    string? ResolvedGid,
    string? ResolvedName,
    string? Message);
public sealed record AsanaCustomFieldOption(string Gid, string Name);
public sealed record AsanaCustomFieldDefinition(
    string Gid,
    string Name,
    string Type,
    IReadOnlyList<AsanaCustomFieldOption> Options);
public sealed record AsanaCustomFieldInput(string FieldGid, string Label, string Value);
public sealed record AsanaActionResult(
    bool Succeeded,
    string Provider,
    string? ErrorCode = null,
    string? ErrorMessage = null);

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
    Task<AsanaRegistrationResult> UpdateImportTaskAsync(
        AsanaImportTask task,
        string taskGid,
        CancellationToken cancellationToken);
    Task<AsanaActionResult> DeleteTaskAsync(string taskGid, CancellationToken cancellationToken);
    Task<IReadOnlyList<AsanaAssigneeResolution>> ResolveAssigneesAsync(
        IReadOnlyList<string> requestedNames,
        CancellationToken cancellationToken);
}

public interface IAsanaMetadataService
{
    Task<AsanaProjectCatalog> GetProjectsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AsanaSectionOption>> GetSectionsAsync(
        string projectGid,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AsanaCustomFieldDefinition>> GetCustomFieldsAsync(
        string projectGid,
        CancellationToken cancellationToken);
}

public sealed class MockAsanaTaskService : IAsanaTaskService, IAsanaMetadataService
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

    public Task<AsanaRegistrationResult> UpdateImportTaskAsync(
        AsanaImportTask task,
        string taskGid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assigneeStatus = string.IsNullOrWhiteSpace(task.Assignee) ? "NotRequested" : "Mock";
        return Task.FromResult(new AsanaRegistrationResult(
            true,
            "Mock",
            taskGid,
            null,
            AssigneeResolutionStatus: assigneeStatus,
            ResolvedAssigneeName: NullIfWhiteSpace(task.Assignee)));
    }

    public Task<AsanaActionResult> DeleteTaskAsync(string taskGid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AsanaActionResult(true, "Mock"));
    }

    public Task<IReadOnlyList<AsanaAssigneeResolution>> ResolveAssigneesAsync(
        IReadOnlyList<string> requestedNames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AsanaAssigneeResolution> result = requestedNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .Select(name => new AsanaAssigneeResolution(name, "Mock", name, name, null))
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<AsanaProjectCatalog> GetProjectsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string defaultProjectGid = "1000000000000001";
        return Task.FromResult(new AsanaProjectCatalog(
            defaultProjectGid,
            [
                new(defaultProjectGid, "モック・既定プロジェクト"),
                new("1000000000000002", "モック・給食プロジェクト")
            ]));
    }

    public Task<IReadOnlyList<AsanaSectionOption>> GetSectionsAsync(
        string projectGid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AsanaSectionOption> result =
        [
            new("2000000000000001", "未着手"),
            new("2000000000000002", "進行中")
        ];
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<AsanaCustomFieldDefinition>> GetCustomFieldsAsync(
        string projectGid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AsanaCustomFieldDefinition> result =
        [
            new("3000000000000001", "進み具合", "enum",
            [
                new("3100000000000001", "未着手"),
                new("3100000000000002", "進行中"),
                new("3100000000000003", "完了")
            ]),
            new("3000000000000002", "優先度", "enum",
            [
                new("3200000000000001", "高"),
                new("3200000000000002", "中"),
                new("3200000000000003", "低")
            ]),
            new("3000000000000003", "予定時間", "number", []),
            new("3000000000000004", "予定費用", "number", [])
        ];
        return Task.FromResult(result);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ApiAsanaTaskService(
    HttpClient httpClient,
    IOptions<AsanaOptions> options,
    IAsanaAccessTokenProvider accessTokenProvider,
    ProjectAccessService? projectAccess)
    : IAsanaTaskService, IAsanaMetadataService
{
    private const string TaskResponseFields = "gid,permalink_url,assignee.gid,assignee.name";
    private readonly AsanaOptions _options = options.Value;
    private readonly Dictionary<string, AsanaCustomFieldDefinition> _customFieldCache = new(StringComparer.Ordinal);

    public ApiAsanaTaskService(HttpClient httpClient, IOptions<AsanaOptions> options)
        : this(httpClient, options, new ConfiguredAccessTokenProvider(options), null)
    {
    }

    public async Task<AsanaRegistrationResult> CreateTaskAsync(
        TaskCandidate candidate,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var projectGid = NullIfWhiteSpace(candidate.ProjectGid) ?? NullIfWhiteSpace(_options.DefaultProjectGid);
        if (projectAccess is not null)
        {
            await projectAccess.EnsureAllowedAsync(projectGid, cancellationToken);
        }
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
            ["start_on"] = candidate.StartDate?.ToString("yyyy-MM-dd"),
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
            ["start_on"] = candidate.StartDate?.ToString("yyyy-MM-dd"),
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
            ["start_on"] = task.StartDate?.ToString("yyyy-MM-dd"),
            ["due_on"] = task.DueDate?.ToString("yyyy-MM-dd")
        };
        if (assignee.Gid is not null)
        {
            data["assignee"] = assignee.Gid;
        }
        var (customFields, customFieldWarning) = await BuildCustomFieldsAsync(
            task.ExtraFields,
            cancellationToken);
        if (customFields.Count > 0)
        {
            data["custom_fields"] = customFields;
        }

        string requestUri;
        if (string.IsNullOrWhiteSpace(parentTaskGid))
        {
            var projectGid = NullIfWhiteSpace(task.ProjectGid) ?? NullIfWhiteSpace(_options.DefaultProjectGid);
            if (projectAccess is not null)
            {
                await projectAccess.EnsureAllowedAsync(projectGid, cancellationToken);
            }
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
            cancellationToken,
            customFieldWarning);
    }

    public async Task<AsanaRegistrationResult> UpdateImportTaskAsync(
        AsanaImportTask task,
        string taskGid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var normalizedTaskGid = NullIfWhiteSpace(taskGid)
            ?? throw new ArgumentException("An Asana task number is required.", nameof(taskGid));
        var assignee = await ResolveAssigneeAsync(task.Assignee, cancellationToken);
        var data = new Dictionary<string, object?>
        {
            ["name"] = task.Title,
            ["notes"] = task.Description,
            ["start_on"] = task.StartDate?.ToString("yyyy-MM-dd"),
            ["due_on"] = task.DueDate?.ToString("yyyy-MM-dd"),
            ["assignee"] = assignee.Gid
        };
        var (customFields, customFieldWarning) = await BuildCustomFieldsAsync(
            task.ExtraFields,
            cancellationToken);
        if (customFields.Count > 0)
        {
            data["custom_fields"] = customFields;
        }

        return await SendTaskAsync(
            HttpMethod.Put,
            $"tasks/{Uri.EscapeDataString(normalizedTaskGid)}?opt_fields={TaskResponseFields}",
            data,
            assignee,
            "Asanaの既存タスクを更新できませんでした。",
            cancellationToken,
            customFieldWarning);
    }

    public async Task<AsanaActionResult> DeleteTaskAsync(
        string taskGid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var normalizedTaskGid = NullIfWhiteSpace(taskGid)
            ?? throw new ArgumentException("An Asana task number is required.", nameof(taskGid));
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"tasks/{Uri.EscapeDataString(normalizedTaskGid)}");
        await AddAuthorizationAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? new AsanaActionResult(true, "AsanaApi")
            : new AsanaActionResult(
                false,
                "AsanaApi",
                $"HTTP_{(int)response.StatusCode}",
                "Asanaで作成したタスクを元に戻せませんでした。");
    }

    public async Task<IReadOnlyList<AsanaAssigneeResolution>> ResolveAssigneesAsync(
        IReadOnlyList<string> requestedNames,
        CancellationToken cancellationToken)
    {
        var result = new List<AsanaAssigneeResolution>();
        foreach (var requested in requestedNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name.Trim())
                     .Distinct(StringComparer.Ordinal))
        {
            var resolution = await ResolveAssigneeAsync(requested, cancellationToken);
            result.Add(new AsanaAssigneeResolution(
                requested,
                resolution.Status,
                resolution.Gid,
                resolution.Name,
                resolution.WarningMessage));
        }
        return result;
    }

    public async Task<AsanaProjectCatalog> GetProjectsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var workspaceGid = NullIfWhiteSpace(_options.DefaultWorkspaceGid)
            ?? throw new InvalidOperationException(
                "Asanaプロジェクト一覧にはサーバー側のDefaultWorkspaceGid設定が必要です。");
        var projects = new List<AsanaProjectOption>();
        string? offset = null;
        do
        {
            var requestUri =
                $"workspaces/{Uri.EscapeDataString(workspaceGid)}/projects?archived=false&limit=100&opt_fields=gid,name";
            if (offset is not null)
            {
                requestUri += $"&offset={Uri.EscapeDataString(offset)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await AddAuthorizationAsync(request, cancellationToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Asanaプロジェクト一覧を取得できませんでした。");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            projects.AddRange(document.RootElement.GetProperty("data")
                .EnumerateArray()
                .Select(element => new AsanaProjectOption(
                    element.GetProperty("gid").GetString() ?? string.Empty,
                    element.GetProperty("name").GetString() ?? string.Empty))
                .Where(project => project.Gid.Length > 0 && project.Name.Length > 0));
            offset = NextOffset(document.RootElement);
        }
        while (offset is not null);

        return new AsanaProjectCatalog(
            NullIfWhiteSpace(_options.DefaultProjectGid),
            projects
                .DistinctBy(project => project.Gid)
                .OrderBy(project => project.Name, StringComparer.Create(
                    System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                    ignoreCase: true))
                .ToArray());
    }

    public async Task<IReadOnlyList<AsanaSectionOption>> GetSectionsAsync(
        string projectGid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var sections = new List<AsanaSectionOption>();
        string? offset = null;
        do
        {
            var requestUri =
                $"projects/{Uri.EscapeDataString(projectGid)}/sections?limit=100&opt_fields=gid,name";
            if (offset is not null)
            {
                requestUri += $"&offset={Uri.EscapeDataString(offset)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await AddAuthorizationAsync(request, cancellationToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Asanaセクション一覧を取得できませんでした。");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            sections.AddRange(document.RootElement.GetProperty("data")
                .EnumerateArray()
                .Select(element => new AsanaSectionOption(
                    element.GetProperty("gid").GetString() ?? string.Empty,
                    element.GetProperty("name").GetString() ?? string.Empty))
                .Where(section => section.Gid.Length > 0 && section.Name.Length > 0));
            offset = NextOffset(document.RootElement);
        }
        while (offset is not null);

        return sections
            .DistinctBy(section => section.Gid)
            .OrderBy(section => section.Name, StringComparer.Create(
                System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                ignoreCase: true))
            .ToArray();
    }

    public async Task<IReadOnlyList<AsanaCustomFieldDefinition>> GetCustomFieldsAsync(
        string projectGid,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var fields = new List<AsanaCustomFieldDefinition>();
        string? offset = null;
        do
        {
            var requestUri =
                $"projects/{Uri.EscapeDataString(projectGid)}/custom_field_settings?limit=100" +
                "&opt_fields=custom_field.gid,custom_field.name,custom_field.resource_subtype," +
                "custom_field.enum_options.gid,custom_field.enum_options.name,custom_field.enum_options.enabled," +
                "custom_field.is_value_read_only";
            if (offset is not null)
            {
                requestUri += $"&offset={Uri.EscapeDataString(offset)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await AddAuthorizationAsync(request, cancellationToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Asanaの追加項目一覧を取得できませんでした。");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            foreach (var setting in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (!setting.TryGetProperty("custom_field", out var field) ||
                    field.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (field.TryGetProperty("is_value_read_only", out var readOnly) &&
                    readOnly.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var gid = field.TryGetProperty("gid", out var gidElement)
                    ? gidElement.GetString()
                    : null;
                var name = field.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var type = field.TryGetProperty("resource_subtype", out var typeElement)
                    ? typeElement.GetString()
                    : null;
                if (gid is null || name is null || type is null) continue;
                var options = field.TryGetProperty("enum_options", out var optionsElement) &&
                    optionsElement.ValueKind == JsonValueKind.Array
                    ? optionsElement.EnumerateArray()
                        .Where(option =>
                            !option.TryGetProperty("enabled", out var enabled) ||
                            enabled.ValueKind != JsonValueKind.False)
                        .Select(option => new AsanaCustomFieldOption(
                            option.GetProperty("gid").GetString() ?? string.Empty,
                            option.GetProperty("name").GetString() ?? string.Empty))
                        .Where(option => option.Gid.Length > 0 && option.Name.Length > 0)
                        .ToArray()
                    : [];
                var definition = new AsanaCustomFieldDefinition(gid, name, type, options);
                fields.Add(definition);
                _customFieldCache[gid] = definition;
            }
            offset = NextOffset(document.RootElement);
        }
        while (offset is not null);

        return fields
            .DistinctBy(field => field.Gid)
            .OrderBy(field => field.Name, StringComparer.Create(
                System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                ignoreCase: true))
            .ToArray();
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
            await AddAuthorizationAsync(request, cancellationToken);
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
        CancellationToken cancellationToken,
        string? additionalWarning = null) =>
        await SendTaskAsync(
            HttpMethod.Post,
            requestUri,
            data,
            assignee,
            failureMessage,
            cancellationToken,
            additionalWarning);

    private async Task<AsanaRegistrationResult> SendTaskAsync(
        HttpMethod method,
        string requestUri,
        IReadOnlyDictionary<string, object?> data,
        AssigneeResolution assignee,
        string failureMessage,
        CancellationToken cancellationToken,
        string? additionalWarning = null)
    {
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(new { data })
        };
        await AddAuthorizationAsync(request, cancellationToken);
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
                CombineWarnings(assignee.WarningMessage, additionalWarning));
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
        var warning = CombineWarnings(assignee.WarningMessage, additionalWarning);
        if (assignee.Gid is not null && actualAssignee?.Gid is null)
        {
            status = "NotApplied";
            warning = CombineWarnings(
                "Asanaタスクは作成されましたが、担当者を割り当てられませんでした。",
                additionalWarning);
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

    private async Task<(Dictionary<string, object?> Values, string? Warning)> BuildCustomFieldsAsync(
        IReadOnlyList<AsanaCustomFieldInput>? inputs,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var input in inputs ?? [])
        {
            var fieldGid = NullIfWhiteSpace(input.FieldGid);
            var rawValue = NullIfWhiteSpace(input.Value);
            if (fieldGid is null || rawValue is null) continue;
            var definition = await GetCustomFieldDefinitionAsync(fieldGid, cancellationToken);
            switch (definition.Type)
            {
                case "number":
                    var numericText = rawValue
                        .Replace(",", string.Empty, StringComparison.Ordinal)
                        .Replace("¥", string.Empty, StringComparison.Ordinal)
                        .Replace("￥", string.Empty, StringComparison.Ordinal)
                        .Trim()
                        .TrimEnd('h', 'H', '時', '間');
                    if (decimal.TryParse(
                            numericText,
                            NumberStyles.Number | NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture,
                            out var number))
                    {
                        values[fieldGid] = number;
                    }
                    else
                    {
                        warnings.Add($"「{input.Label}」の値「{rawValue}」を数値として送れませんでした。");
                    }
                    break;
                case "enum":
                    var option = definition.Options.SingleOrDefault(item =>
                        NormalizeName(item.Name) == NormalizeName(rawValue));
                    if (option is null)
                    {
                        warnings.Add($"「{input.Label}」の値「{rawValue}」に合う選択肢がAsanaにありません。");
                    }
                    else
                    {
                        values[fieldGid] = option.Gid;
                    }
                    break;
                case "multi_enum":
                    var requestedOptions = rawValue
                        .Split([',', '、', '/', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var optionGids = requestedOptions
                        .Select(requested => definition.Options.SingleOrDefault(item =>
                            NormalizeName(item.Name) == NormalizeName(requested)))
                        .Where(option => option is not null)
                        .Select(option => option!.Gid)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (optionGids.Length == requestedOptions.Length)
                    {
                        values[fieldGid] = optionGids;
                    }
                    else
                    {
                        warnings.Add($"「{input.Label}」の一部の値に合う選択肢がAsanaにありません。");
                    }
                    break;
                case "text":
                    values[fieldGid] = rawValue;
                    break;
                default:
                    warnings.Add($"Asanaの「{definition.Name}」は、この画面から値を送れない種類です。");
                    break;
            }
        }
        return (values, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    private async Task<AsanaCustomFieldDefinition> GetCustomFieldDefinitionAsync(
        string fieldGid,
        CancellationToken cancellationToken)
    {
        if (_customFieldCache.TryGetValue(fieldGid, out var cached)) return cached;
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"custom_fields/{Uri.EscapeDataString(fieldGid)}" +
            "?opt_fields=gid,name,resource_subtype,enum_options.gid,enum_options.name,enum_options.enabled");
        await AddAuthorizationAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Asanaの追加項目を確認できませんでした。");
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var field = document.RootElement.GetProperty("data");
        var options = field.TryGetProperty("enum_options", out var optionsElement) &&
            optionsElement.ValueKind == JsonValueKind.Array
            ? optionsElement.EnumerateArray()
                .Where(option =>
                    !option.TryGetProperty("enabled", out var enabled) ||
                    enabled.ValueKind != JsonValueKind.False)
                .Select(option => new AsanaCustomFieldOption(
                    option.GetProperty("gid").GetString() ?? string.Empty,
                    option.GetProperty("name").GetString() ?? string.Empty))
                .Where(option => option.Gid.Length > 0 && option.Name.Length > 0)
                .ToArray()
            : [];
        var definition = new AsanaCustomFieldDefinition(
            field.GetProperty("gid").GetString() ?? fieldGid,
            field.GetProperty("name").GetString() ?? fieldGid,
            field.GetProperty("resource_subtype").GetString() ?? "text",
            options);
        _customFieldCache[fieldGid] = definition;
        return definition;
    }

    private static string? CombineWarnings(string? first, string? second)
    {
        var values = new[] { NullIfWhiteSpace(first), NullIfWhiteSpace(second) }
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 0 ? null : string.Join(" ", values!);
    }

    private void EnsureConfigured()
    {
        if (_options.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
        {
            throw new InvalidOperationException("Asana API mode requires Integration:Asana:PersonalAccessToken.");
        }
    }

    private async Task AddAuthorizationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await accessTokenProvider.GetAccessTokenAsync(cancellationToken));

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

    private static string? NextOffset(JsonElement root) =>
        root.TryGetProperty("next_page", out var nextPage)
        && nextPage.ValueKind == JsonValueKind.Object
        && nextPage.TryGetProperty("offset", out var offsetElement)
            ? NullIfWhiteSpace(offsetElement.GetString())
            : null;

    private sealed record AsanaUser(string? Gid, string? Name);
    private sealed class ConfiguredAccessTokenProvider(IOptions<AsanaOptions> configuredOptions)
        : IAsanaAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(configuredOptions.Value.PersonalAccessToken
                ?? throw new InvalidOperationException(
                    "Asana API mode requires Integration:Asana:PersonalAccessToken."));
        }
    }

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
