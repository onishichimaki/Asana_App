using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;

namespace TaskCapture.Api.Services;

public sealed class TaskWorkflowService(
    TaskCaptureDbContext db,
    ITaskOrganizer organizer,
    IAsanaTaskService asanaTaskService,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OrganizeTaskResponse> OrganizeAsync(
        OrganizeTaskRequest input,
        string clientKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = await GetOrCreateUserAsync(clientKey, now, cancellationToken);
        var request = new TaskRequest
        {
            UserId = user.Id,
            RawText = input.RawText.Trim(),
            Source = input.Source,
            Status = "Received",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.TaskRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var organized = await organizer.OrganizeAsync(request.RawText, cancellationToken);
            var candidate = new TaskCandidate
            {
                TaskRequestId = request.Id,
                Title = organized.Title,
                Description = organized.Description,
                Assignee = organized.Assignee,
                DueDate = organized.DueDate,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Subtasks = organized.Subtasks.Select((title, index) => new TaskCandidateSubtask
                {
                    Title = title,
                    SortOrder = index,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }).ToList()
            };
            request.Status = "Organized";
            request.UpdatedAtUtc = now;
            db.TaskCandidates.Add(candidate);
            AddAudit(user.Id, "TaskOrganized", "TaskRequest", request.Id, "Candidate generated.", correlationId, now);
            await db.SaveChangesAsync(cancellationToken);
            return new OrganizeTaskResponse(request.Id, request.Status, ToResponse(candidate));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            request.Status = "Failed";
            request.ErrorMessage = SafeMessage(ex.Message);
            request.UpdatedAtUtc = timeProvider.GetUtcNow();
            AddAudit(user.Id, "TaskOrganizationFailed", "TaskRequest", request.Id, request.ErrorMessage, correlationId, request.UpdatedAtUtc, "Error");
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TaskCandidateResponse> UpdateCandidateAsync(
        Guid candidateId,
        CandidateUpdateRequest input,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var candidate = await db.TaskCandidates
            .Include(x => x.TaskRequest)
            .Include(x => x.Subtasks)
            .SingleOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Task candidate was not found.");
        ApplyUpdate(candidate, input);
        candidate.TaskRequest.Status = "Edited";
        AddAudit(candidate.TaskRequest.UserId, "TaskCandidateUpdated", "TaskCandidate", candidate.Id, "Candidate confirmed or edited.", correlationId, candidate.UpdatedAtUtc);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(candidate);
    }

    public async Task<RegistrationResponse> RegisterAsync(
        Guid candidateId,
        CandidateUpdateRequest input,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var candidate = await db.TaskCandidates
            .Include(x => x.TaskRequest)
            .Include(x => x.Registrations)
            .Include(x => x.Subtasks).ThenInclude(x => x.Registrations)
            .SingleOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Task candidate was not found.");

        var parentRegistration = candidate.Registrations
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault(x => x.Succeeded);
        var alreadyComplete = parentRegistration is not null && candidate.Subtasks.All(HasSuccessfulRegistration);
        if (alreadyComplete) return ToResponse(parentRegistration!, true, candidate.Subtasks, true);

        if (parentRegistration is null)
        {
            ApplyUpdate(candidate, input);
            candidate.TaskRequest.Status = "Registering";
            candidate.TaskRequest.UpdatedAtUtc = candidate.UpdatedAtUtc;
            await db.SaveChangesAsync(cancellationToken);

            var parentResult = await CreateParentSafelyAsync(candidate, cancellationToken);
            var parentNow = timeProvider.GetUtcNow();
            parentRegistration = new AsanaRegistration
            {
                TaskCandidateId = candidate.Id,
                Succeeded = parentResult.Succeeded,
                Provider = parentResult.Provider,
                ExternalTaskGid = parentResult.ExternalTaskGid,
                ExternalTaskUrl = parentResult.ExternalTaskUrl,
                ErrorCode = parentResult.ErrorCode,
                ErrorMessage = SafeMessage(parentResult.ErrorMessage),
                AssigneeResolutionStatus = parentResult.AssigneeResolutionStatus,
                ResolvedAssigneeGid = parentResult.ResolvedAssigneeGid,
                ResolvedAssigneeName = parentResult.ResolvedAssigneeName,
                WarningMessage = SafeWarning(parentResult.WarningMessage),
                CreatedAtUtc = parentNow
            };
            db.AsanaRegistrations.Add(parentRegistration);
            await db.SaveChangesAsync(cancellationToken);

            if (!parentRegistration.Succeeded || string.IsNullOrWhiteSpace(parentRegistration.ExternalTaskGid))
            {
                candidate.TaskRequest.Status = "Failed";
                candidate.TaskRequest.ErrorMessage = parentRegistration.ErrorMessage;
                candidate.TaskRequest.UpdatedAtUtc = parentNow;
                AddAudit(candidate.TaskRequest.UserId, "AsanaRegistrationFailed", "TaskCandidate", candidate.Id,
                    parentRegistration.ErrorMessage, correlationId, parentNow, "Error");
                await db.SaveChangesAsync(cancellationToken);
                return ToResponse(parentRegistration, false, candidate.Subtasks, false);
            }
        }

        foreach (var subtask in candidate.Subtasks.OrderBy(x => x.SortOrder))
        {
            if (HasSuccessfulRegistration(subtask)) continue;
            var result = await CreateSubtaskSafelyAsync(
                candidate,
                subtask,
                parentRegistration.ExternalTaskGid!,
                parentRegistration.ResolvedAssigneeGid,
                cancellationToken);
            db.AsanaSubtaskRegistrations.Add(new AsanaSubtaskRegistration
            {
                TaskCandidateSubtaskId = subtask.Id,
                Succeeded = result.Succeeded,
                Provider = result.Provider,
                ExternalTaskGid = result.ExternalTaskGid,
                ExternalTaskUrl = result.ExternalTaskUrl,
                ErrorCode = result.ErrorCode,
                ErrorMessage = SafeMessage(result.ErrorMessage),
                CreatedAtUtc = timeProvider.GetUtcNow()
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var allSubtasksSucceeded = candidate.Subtasks.All(HasSuccessfulRegistration);
        var now = timeProvider.GetUtcNow();
        var firstSubtaskError = candidate.Subtasks
            .Where(subtask => !HasSuccessfulRegistration(subtask))
            .SelectMany(x => x.Registrations)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault(x => !x.Succeeded)?.ErrorMessage;
        candidate.TaskRequest.Status = allSubtasksSucceeded ? "Registered" : "PartiallyRegistered";
        candidate.TaskRequest.ErrorMessage = allSubtasksSucceeded ? null : firstSubtaskError;
        candidate.TaskRequest.UpdatedAtUtc = now;
        AddAudit(
            candidate.TaskRequest.UserId,
            allSubtasksSucceeded ? "AsanaRegistrationSucceeded" : "AsanaSubtaskRegistrationFailed",
            "TaskCandidate",
            candidate.Id,
            allSubtasksSucceeded
                ? $"Provider={parentRegistration.Provider}; Gid={parentRegistration.ExternalTaskGid}; Subtasks={candidate.Subtasks.Count}"
                : firstSubtaskError,
            correlationId,
            now,
            allSubtasksSucceeded ? "Information" : "Error");
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(parentRegistration, false, candidate.Subtasks, allSubtasksSucceeded, firstSubtaskError);
    }

    public async Task<IReadOnlyList<RecentTaskResponse>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var rows = await db.TaskRequests.AsNoTracking()
            .Include(x => x.Candidates).ThenInclude(x => x.Registrations)
            .Include(x => x.Candidates).ThenInclude(x => x.Subtasks).ThenInclude(x => x.Registrations)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 20))
            .ToListAsync(cancellationToken);

        return rows.Select(request =>
        {
            var candidate = request.Candidates.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
            var registration = candidate?.Registrations.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
            return new RecentTaskResponse(
                request.Id,
                request.RawText,
                request.Source,
                request.Status,
                request.CreatedAtUtc,
                candidate is null ? null : ToResponse(candidate),
                registration is null || candidate is null
                    ? null
                    : ToResponse(registration, false, candidate.Subtasks, candidate.Subtasks.All(HasSuccessfulRegistration)));
        }).ToList();
    }

    private async Task<User> GetOrCreateUserAsync(string clientKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trimmed = string.IsNullOrWhiteSpace(clientKey) ? "local-device" : clientKey.Trim();
        var normalized = trimmed[..Math.Min(trimmed.Length, 128)];
        var user = await db.Users.SingleOrDefaultAsync(x => x.ClientKey == normalized, cancellationToken);
        if (user is not null) return user;
        user = new User { ClientKey = normalized, DisplayName = "Limited user", CreatedAtUtc = now };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    private void ApplyUpdate(TaskCandidate candidate, CandidateUpdateRequest input)
    {
        candidate.Title = input.Title.Trim();
        candidate.Description = input.Description.Trim();
        candidate.Assignee = NullIfWhiteSpace(input.Assignee);
        candidate.StartDate = input.StartDate;
        candidate.DueDate = input.DueDate;
        candidate.ProjectGid = NullIfWhiteSpace(input.ProjectGid);
        candidate.SectionGid = NullIfWhiteSpace(input.SectionGid);
        candidate.TagsJson = JsonSerializer.Serialize(input.Tags.Distinct().ToArray(), JsonOptions);
        candidate.CustomFieldsJson = JsonSerializer.Serialize(input.CustomFields, JsonOptions);
        candidate.Priority = NullIfWhiteSpace(input.Priority);
        candidate.UpdatedAtUtc = timeProvider.GetUtcNow();
        var normalizedSubtasks = input.Subtasks
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        var existingSubtasks = candidate.Subtasks.OrderBy(x => x.SortOrder).ToArray();
        var retainedCount = Math.Min(existingSubtasks.Length, normalizedSubtasks.Length);
        for (var index = 0; index < retainedCount; index++)
        {
            existingSubtasks[index].Title = normalizedSubtasks[index];
            existingSubtasks[index].SortOrder = index;
            existingSubtasks[index].UpdatedAtUtc = candidate.UpdatedAtUtc;
        }

        foreach (var removed in existingSubtasks.Skip(normalizedSubtasks.Length))
        {
            db.TaskCandidateSubtasks.Remove(removed);
            candidate.Subtasks.Remove(removed);
        }

        for (var index = existingSubtasks.Length; index < normalizedSubtasks.Length; index++)
        {
            var subtask = new TaskCandidateSubtask
            {
                TaskCandidateId = candidate.Id,
                Title = normalizedSubtasks[index],
                SortOrder = index,
                CreatedAtUtc = candidate.UpdatedAtUtc,
                UpdatedAtUtc = candidate.UpdatedAtUtc
            };
            db.TaskCandidateSubtasks.Add(subtask);
        }
    }

    private async Task<AsanaRegistrationResult> CreateParentSafelyAsync(
        TaskCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            return await asanaTaskService.CreateTaskAsync(candidate, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AsanaRegistrationResult(false, "Configuration", null, null, "INTEGRATION_ERROR", SafeMessage(ex.Message));
        }
    }

    private async Task<AsanaRegistrationResult> CreateSubtaskSafelyAsync(
        TaskCandidate candidate,
        TaskCandidateSubtask subtask,
        string parentTaskGid,
        string? resolvedAssigneeGid,
        CancellationToken cancellationToken)
    {
        try
        {
            return await asanaTaskService.CreateSubtaskAsync(
                candidate,
                subtask,
                parentTaskGid,
                resolvedAssigneeGid,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AsanaRegistrationResult(false, "Configuration", null, null, "INTEGRATION_ERROR", SafeMessage(ex.Message));
        }
    }

    private void AddAudit(Guid? userId, string eventType, string entityType, Guid entityId, string? detail, string correlationId, DateTimeOffset now, string level = "Information")
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Level = level,
            Detail = SafeMessage(detail),
            CorrelationId = correlationId,
            CreatedAtUtc = now
        });
    }

    private static TaskCandidateResponse ToResponse(TaskCandidate candidate) => new(
        candidate.Id,
        candidate.TaskRequestId,
        candidate.Title,
        candidate.Description,
        candidate.Assignee,
        candidate.StartDate,
        candidate.DueDate,
        candidate.Subtasks.OrderBy(x => x.SortOrder).Select(x => x.Title).ToArray(),
        candidate.ProjectGid,
        candidate.SectionGid,
        JsonSerializer.Deserialize<string[]>(candidate.TagsJson, JsonOptions) ?? [],
        JsonSerializer.Deserialize<Dictionary<string, string>>(candidate.CustomFieldsJson, JsonOptions) ?? [],
        candidate.Priority);

    private static RegistrationResponse ToResponse(
        AsanaRegistration registration,
        bool alreadyRegistered,
        IEnumerable<TaskCandidateSubtask> subtasks,
        bool succeeded,
        string? errorMessage = null) => new(
        registration.Id,
        registration.TaskCandidateId,
        succeeded,
        alreadyRegistered,
        registration.Provider,
        registration.ExternalTaskGid,
        registration.ExternalTaskUrl,
        errorMessage ?? registration.ErrorMessage,
        registration.AssigneeResolutionStatus,
        registration.ResolvedAssigneeGid,
        registration.ResolvedAssigneeName,
        registration.WarningMessage,
        subtasks.OrderBy(x => x.SortOrder).Select(subtask =>
        {
            var result = subtask.Registrations.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault(x => x.Succeeded)
                ?? subtask.Registrations.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
            return new SubtaskRegistrationResponse(
                subtask.Id,
                subtask.Title,
                result?.Succeeded ?? false,
                result?.Provider ?? registration.Provider,
                result?.ExternalTaskGid,
                result?.ExternalTaskUrl,
                result?.ErrorMessage);
        }).ToArray());

    private static bool HasSuccessfulRegistration(TaskCandidateSubtask subtask) =>
        subtask.Registrations.Any(x => x.Succeeded);

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? SafeMessage(string? value) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 1_000)];
    private static string? SafeWarning(string? value) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 500)];
}
