using System.ComponentModel.DataAnnotations;

namespace TaskCapture.Api.Contracts;

public sealed class OrganizeTaskRequest
{
    [Required, StringLength(10_000, MinimumLength = 1)]
    public string RawText { get; init; } = string.Empty;

    [Required, RegularExpression("^(text|paste|voice|clipboard|launcher|image|minutes)$")]
    public string Source { get; init; } = "text";
}

public sealed class CandidateUpdateRequest : IValidatableObject
{
    [Required, StringLength(200, MinimumLength = 1)] public string Title { get; init; } = string.Empty;
    [StringLength(10_000)] public string Description { get; init; } = string.Empty;
    [StringLength(200)] public string? Assignee { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public IReadOnlyList<string> Subtasks { get; init; } = [];
    [RegularExpression("^[0-9]{1,64}$")] public string? ProjectGid { get; init; }
    [RegularExpression("^[0-9]{1,64}$")] public string? SectionGid { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, string> CustomFields { get; init; } = new Dictionary<string, string>();
    [RegularExpression("^(low|normal|high|urgent)?$")] public string? Priority { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SectionGid is not null && ProjectGid is null)
        {
            yield return new ValidationResult("SectionGid requires ProjectGid.", [nameof(SectionGid), nameof(ProjectGid)]);
        }
        if (StartDate is not null && DueDate is null)
        {
            yield return new ValidationResult("DueDate is required when StartDate is specified.", [nameof(StartDate), nameof(DueDate)]);
        }
        if (StartDate > DueDate)
        {
            yield return new ValidationResult("StartDate must be on or before DueDate.", [nameof(StartDate), nameof(DueDate)]);
        }

        if (Tags is null || Tags.Count > 20 || Tags.Any(tag => !System.Text.RegularExpressions.Regex.IsMatch(tag, "^[0-9]{1,64}$")))
        {
            yield return new ValidationResult("Tags must contain at most 20 Asana numeric GIDs.", [nameof(Tags)]);
        }

        if (CustomFields is null || CustomFields.Count > 50 || CustomFields.Any(pair =>
                !System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^[0-9]{1,64}$") || pair.Value.Length > 500))
        {
            yield return new ValidationResult("CustomFields must contain at most 50 numeric GID keys and 500-character values.", [nameof(CustomFields)]);
        }

        if (Subtasks is null || Subtasks.Count > 10 || Subtasks.Any(subtask =>
                string.IsNullOrWhiteSpace(subtask) || subtask.Trim().Length > 200))
        {
            yield return new ValidationResult("Subtasks must contain at most 10 non-empty titles of 200 characters or fewer.", [nameof(Subtasks)]);
        }
    }
}

public sealed record TaskCandidateResponse(
    Guid Id,
    Guid TaskRequestId,
    string Title,
    string Description,
    string? Assignee,
    DateOnly? StartDate,
    DateOnly? DueDate,
    IReadOnlyList<string> Subtasks,
    string? ProjectGid,
    string? SectionGid,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> CustomFields,
    string? Priority);

public sealed record OrganizeTaskResponse(Guid TaskRequestId, string Status, TaskCandidateResponse Candidate);

public sealed record RegistrationResponse(
    Guid RegistrationId,
    Guid TaskCandidateId,
    bool Succeeded,
    bool AlreadyRegistered,
    string Provider,
    string? ExternalTaskGid,
    string? ExternalTaskUrl,
    string? ErrorMessage,
    string? AssigneeResolutionStatus,
    string? ResolvedAssigneeGid,
    string? ResolvedAssigneeName,
    string? WarningMessage,
    IReadOnlyList<SubtaskRegistrationResponse> Subtasks);

public sealed record SubtaskRegistrationResponse(
    Guid TaskCandidateSubtaskId,
    string Title,
    bool Succeeded,
    string Provider,
    string? ExternalTaskGid,
    string? ExternalTaskUrl,
    string? ErrorMessage);

public sealed record RecentTaskResponse(
    Guid TaskRequestId,
    string RawText,
    string Source,
    string Status,
    DateTimeOffset CreatedAtUtc,
    TaskCandidateResponse? Candidate,
    RegistrationResponse? Registration);
