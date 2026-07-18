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
    public DateOnly? DueDate { get; set; }
    [MaxLength(64)] public string? ProjectGid { get; set; }
    [MaxLength(64)] public string? SectionGid { get; set; }
    [MaxLength(2_000)] public string TagsJson { get; set; } = "[]";
    [MaxLength(4_000)] public string CustomFieldsJson { get; set; } = "{}";
    [MaxLength(32)] public string? Priority { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AsanaRegistration> Registrations { get; set; } = [];
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
