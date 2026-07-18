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
                UpdatedAtUtc = now
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
        var candidate = await db.TaskCandidates.Include(x => x.TaskRequest)
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
            .SingleOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Task candidate was not found.");

        var successful = candidate.Registrations.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault(x => x.Succeeded);
        if (successful is not null) return ToResponse(successful, true);

        ApplyUpdate(candidate, input);
        candidate.TaskRequest.Status = "Registering";
        candidate.TaskRequest.UpdatedAtUtc = candidate.UpdatedAtUtc;
        await db.SaveChangesAsync(cancellationToken);

        AsanaRegistrationResult result;
        try
        {
            result = await asanaTaskService.CreateTaskAsync(candidate, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new AsanaRegistrationResult(false, "Configuration", null, null, "INTEGRATION_ERROR", SafeMessage(ex.Message));
        }

        var now = timeProvider.GetUtcNow();
        var registration = new AsanaRegistration
        {
            TaskCandidateId = candidate.Id,
            Succeeded = result.Succeeded,
            Provider = result.Provider,
            ExternalTaskGid = result.ExternalTaskGid,
            ExternalTaskUrl = result.ExternalTaskUrl,
            ErrorCode = result.ErrorCode,
            ErrorMessage = SafeMessage(result.ErrorMessage),
            CreatedAtUtc = now
        };
        db.AsanaRegistrations.Add(registration);
        candidate.TaskRequest.Status = result.Succeeded ? "Registered" : "Failed";
        candidate.TaskRequest.ErrorMessage = result.Succeeded ? null : registration.ErrorMessage;
        candidate.TaskRequest.UpdatedAtUtc = now;
        AddAudit(
            candidate.TaskRequest.UserId,
            result.Succeeded ? "AsanaRegistrationSucceeded" : "AsanaRegistrationFailed",
            "TaskCandidate",
            candidate.Id,
            result.Succeeded ? $"Provider={result.Provider}; Gid={result.ExternalTaskGid}" : registration.ErrorMessage,
            correlationId,
            now,
            result.Succeeded ? "Information" : "Error");
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(registration, false);
    }

    public async Task<IReadOnlyList<RecentTaskResponse>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var rows = await db.TaskRequests.AsNoTracking()
            .Include(x => x.Candidates).ThenInclude(x => x.Registrations)
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
                registration is null ? null : ToResponse(registration, false));
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
        candidate.DueDate = input.DueDate;
        candidate.ProjectGid = NullIfWhiteSpace(input.ProjectGid);
        candidate.SectionGid = NullIfWhiteSpace(input.SectionGid);
        candidate.TagsJson = JsonSerializer.Serialize(input.Tags.Distinct().ToArray(), JsonOptions);
        candidate.CustomFieldsJson = JsonSerializer.Serialize(input.CustomFields, JsonOptions);
        candidate.Priority = NullIfWhiteSpace(input.Priority);
        candidate.UpdatedAtUtc = timeProvider.GetUtcNow();
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
        candidate.DueDate,
        candidate.ProjectGid,
        candidate.SectionGid,
        JsonSerializer.Deserialize<string[]>(candidate.TagsJson, JsonOptions) ?? [],
        JsonSerializer.Deserialize<Dictionary<string, string>>(candidate.CustomFieldsJson, JsonOptions) ?? [],
        candidate.Priority);

    private static RegistrationResponse ToResponse(AsanaRegistration registration, bool alreadyRegistered) => new(
        registration.Id,
        registration.TaskCandidateId,
        registration.Succeeded,
        alreadyRegistered,
        registration.Provider,
        registration.ExternalTaskGid,
        registration.ExternalTaskUrl,
        registration.ErrorMessage);

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? SafeMessage(string? value) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 1_000)];
}
