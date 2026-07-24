using System.ComponentModel.DataAnnotations;

namespace TaskCapture.Api.Data;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(200)] public string DisplayName { get; set; } = "Local user";
    [MaxLength(128)] public string ClientKey { get; set; } = "local-device";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TaskRequest> TaskRequests { get; set; } = [];
    public List<AuditLog> AuditLogs { get; set; } = [];
    public List<WbsImportProfile> WbsImportProfiles { get; set; } = [];
    public List<WbsImportBatch> WbsImportBatches { get; set; } = [];
}

public sealed class TaskRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    [MaxLength(10_000)] public string RawText { get; set; } = string.Empty;
    [MaxLength(32)] public string Source { get; set; } = "text";
    [MaxLength(32)] public string Status { get; set; } = "Received";
    [MaxLength(1_000)] public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TaskCandidate> Candidates { get; set; } = [];
}

public sealed class TaskCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskRequestId { get; set; }
    public TaskRequest TaskRequest { get; set; } = null!;
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(10_000)] public string Description { get; set; } = string.Empty;
    [MaxLength(200)] public string? Assignee { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    [MaxLength(64)] public string? ProjectGid { get; set; }
    [MaxLength(64)] public string? SectionGid { get; set; }
    [MaxLength(2_000)] public string TagsJson { get; set; } = "[]";
    [MaxLength(4_000)] public string CustomFieldsJson { get; set; } = "{}";
    [MaxLength(32)] public string? Priority { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TaskCandidateSubtask> Subtasks { get; set; } = [];
    public List<AsanaRegistration> Registrations { get; set; } = [];
}

public sealed class TaskCandidateSubtask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskCandidateId { get; set; }
    public TaskCandidate TaskCandidate { get; set; } = null!;
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AsanaSubtaskRegistration> Registrations { get; set; } = [];
}

public sealed class AsanaRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskCandidateId { get; set; }
    public TaskCandidate TaskCandidate { get; set; } = null!;
    public bool Succeeded { get; set; }
    [MaxLength(32)] public string Provider { get; set; } = "Mock";
    [MaxLength(64)] public string? ExternalTaskGid { get; set; }
    [MaxLength(500)] public string? ExternalTaskUrl { get; set; }
    [MaxLength(100)] public string? ErrorCode { get; set; }
    [MaxLength(1_000)] public string? ErrorMessage { get; set; }
    [MaxLength(32)] public string? AssigneeResolutionStatus { get; set; }
    [MaxLength(64)] public string? ResolvedAssigneeGid { get; set; }
    [MaxLength(200)] public string? ResolvedAssigneeName { get; set; }
    [MaxLength(500)] public string? WarningMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AsanaSubtaskRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskCandidateSubtaskId { get; set; }
    public TaskCandidateSubtask TaskCandidateSubtask { get; set; } = null!;
    public bool Succeeded { get; set; }
    [MaxLength(32)] public string Provider { get; set; } = "Mock";
    [MaxLength(64)] public string? ExternalTaskGid { get; set; }
    [MaxLength(500)] public string? ExternalTaskUrl { get; set; }
    [MaxLength(100)] public string? ErrorCode { get; set; }
    [MaxLength(1_000)] public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ApplicationSetting
{
    [MaxLength(128)] public string Key { get; set; } = string.Empty;
    [MaxLength(2_000)] public string Value { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    [MaxLength(64)] public string EventType { get; set; } = string.Empty;
    [MaxLength(64)] public string EntityType { get; set; } = string.Empty;
    [MaxLength(64)] public string EntityId { get; set; } = string.Empty;
    [MaxLength(32)] public string Level { get; set; } = "Information";
    [MaxLength(2_000)] public string? Detail { get; set; }
    [MaxLength(64)] public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WbsImportProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(128)] public string LayoutSignature { get; set; } = string.Empty;
    [MaxLength(200)] public string SheetName { get; set; } = string.Empty;
    public int HeaderRow { get; set; }
    public int DataStartRow { get; set; }
    [MaxLength(20_000)] public string MappingJson { get; set; } = "{}";
    [MaxLength(64)] public string? ProjectGid { get; set; }
    [MaxLength(64)] public string? SectionGid { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<WbsImportBatch> Batches { get; set; } = [];
}

public sealed class WbsImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? WbsImportProfileId { get; set; }
    public WbsImportProfile? WbsImportProfile { get; set; }
    [MaxLength(260)] public string FileName { get; set; } = string.Empty;
    [MaxLength(64)] public string FileHash { get; set; } = string.Empty;
    [MaxLength(200)] public string SheetName { get; set; } = string.Empty;
    [MaxLength(128)] public string LayoutSignature { get; set; } = string.Empty;
    [MaxLength(64)] public string? ProjectGid { get; set; }
    [MaxLength(64)] public string? SectionGid { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Preview";
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int SucceededRows { get; set; }
    public int FailedRows { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<WbsImportRow> Rows { get; set; } = [];
}

public sealed class WbsImportRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WbsImportBatchId { get; set; }
    public WbsImportBatch WbsImportBatch { get; set; } = null!;
    public Guid? ParentRowId { get; set; }
    public WbsImportRow? ParentRow { get; set; }
    public List<WbsImportRow> Children { get; set; } = [];
    public int SourceRowNumber { get; set; }
    [MaxLength(256)] public string SourceKey { get; set; } = string.Empty;
    public bool IsGeneratedKey { get; set; }
    [MaxLength(64)] public string RowHash { get; set; } = string.Empty;
    [MaxLength(64)] public string ContentHash { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int SortOrder { get; set; }
    public bool Included { get; set; } = true;
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(10_000)] public string Description { get; set; } = string.Empty;
    [MaxLength(200)] public string? Assignee { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Ready";
    [MaxLength(2_000)] public string ValidationErrorsJson { get; set; } = "[]";
    [MaxLength(32)] public string? Provider { get; set; }
    [MaxLength(64)] public string? ExternalTaskGid { get; set; }
    [MaxLength(500)] public string? ExternalTaskUrl { get; set; }
    [MaxLength(100)] public string? ErrorCode { get; set; }
    [MaxLength(1_000)] public string? ErrorMessage { get; set; }
    [MaxLength(32)] public string? AssigneeResolutionStatus { get; set; }
    [MaxLength(64)] public string? ResolvedAssigneeGid { get; set; }
    [MaxLength(200)] public string? ResolvedAssigneeName { get; set; }
    [MaxLength(500)] public string? WarningMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
