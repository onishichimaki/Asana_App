using System.ComponentModel.DataAnnotations;

namespace TaskCapture.Api.Contracts;

public sealed class WbsMappingRequest : IValidatableObject
{
    [Required, RegularExpression("^(none|parentKey|level|columns)$")]
    public string HierarchyMode { get; init; } = "none";

    [Required]
    public IReadOnlyDictionary<int, string> Roles { get; init; } = new Dictionary<int, string>();

    [StringLength(20)] public string TitleSeparator { get; init; } = " ";
    [StringLength(20)] public string DescriptionSeparator { get; init; } = "\n";
    [Required, RegularExpression("^(auto|yyyy-MM-dd|yyyy/MM/dd|yyyy.MM.dd|MM/dd/yyyy)$")]
    public string DateFormat { get; init; } = "auto";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var allowedRoles = new HashSet<string>(
            ["ignore", "title", "description", "assignee", "dueDate", "key", "parentKey", "level", "hierarchy"],
            StringComparer.Ordinal);
        if (Roles.Count > 500 || Roles.Any(pair => pair.Key is < 0 or > 499 || !allowedRoles.Contains(pair.Value)))
        {
            yield return new ValidationResult("Roles contains an unsupported column index or role.", [nameof(Roles)]);
        }

        var values = Roles.Values.ToArray();
        if (HierarchyMode != "columns" && !values.Contains("title", StringComparer.Ordinal))
        {
            yield return new ValidationResult("At least one title column is required.", [nameof(Roles)]);
        }
        if (HierarchyMode == "parentKey" &&
            (!values.Contains("key", StringComparer.Ordinal) || !values.Contains("parentKey", StringComparer.Ordinal)))
        {
            yield return new ValidationResult("Parent-key hierarchy requires key and parentKey columns.", [nameof(Roles)]);
        }
        if (HierarchyMode == "level" && !values.Contains("level", StringComparer.Ordinal))
        {
            yield return new ValidationResult("Level hierarchy requires a level column.", [nameof(Roles)]);
        }
        if (HierarchyMode == "columns" && !values.Contains("hierarchy", StringComparer.Ordinal))
        {
            yield return new ValidationResult("Column hierarchy requires one or more hierarchy columns.", [nameof(Roles)]);
        }
    }
}

public sealed class WbsImportProfileRequest : IValidatableObject
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9a-fA-F]{64}$")] public string LayoutSignature { get; init; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 1)] public string SheetName { get; init; } = string.Empty;
    [Range(1, 10_000)] public int HeaderRow { get; init; } = 1;
    [Range(1, 100_000)] public int DataStartRow { get; init; } = 2;
    [Required] public WbsMappingRequest Mapping { get; init; } = new();
    [RegularExpression("^[0-9]{1,64}$")] public string? ProjectGid { get; init; }
    [RegularExpression("^[0-9]{1,64}$")] public string? SectionGid { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DataStartRow <= HeaderRow)
        {
            yield return new ValidationResult("DataStartRow must be after HeaderRow.", [nameof(DataStartRow)]);
        }
        if (SectionGid is not null && ProjectGid is null)
        {
            yield return new ValidationResult("SectionGid requires ProjectGid.", [nameof(SectionGid), nameof(ProjectGid)]);
        }
    }
}

public sealed record WbsImportProfileResponse(
    Guid Id,
    string Name,
    string LayoutSignature,
    string SheetName,
    int HeaderRow,
    int DataStartRow,
    WbsMappingRequest Mapping,
    string? ProjectGid,
    string? SectionGid,
    DateTimeOffset UpdatedAtUtc);

public sealed class WbsNormalizedRowRequest
{
    [Range(1, 1_000_000)] public int SourceRowNumber { get; init; }
    [Required, StringLength(256, MinimumLength = 1)] public string SourceKey { get; init; } = string.Empty;
    public bool IsGeneratedKey { get; init; }
    [StringLength(256)] public string? ParentSourceKey { get; init; }
    [Range(0, 20)] public int Depth { get; init; }
    [Range(0, 1_000_000)] public int SortOrder { get; init; }
    public bool Included { get; init; } = true;
    [StringLength(200)] public string Title { get; init; } = string.Empty;
    [StringLength(10_000)] public string Description { get; init; } = string.Empty;
    [StringLength(200)] public string? Assignee { get; init; }
    public DateOnly? DueDate { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

public sealed class WbsImportBatchRequest : IValidatableObject
{
    [Required, StringLength(260, MinimumLength = 1)] public string FileName { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9a-fA-F]{64}$")] public string FileHash { get; init; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 1)] public string SheetName { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9a-fA-F]{64}$")] public string LayoutSignature { get; init; } = string.Empty;
    public Guid? ProfileId { get; init; }
    [RegularExpression("^[0-9]{1,64}$")] public string? ProjectGid { get; init; }
    [RegularExpression("^[0-9]{1,64}$")] public string? SectionGid { get; init; }
    [Required] public IReadOnlyList<WbsNormalizedRowRequest> Rows { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Rows.Count is < 1 or > 5_000)
        {
            yield return new ValidationResult("Rows must contain between 1 and 5,000 items.", [nameof(Rows)]);
        }
        if (Rows.Select(row => row.SourceKey.Trim()).Distinct(StringComparer.Ordinal).Count() != Rows.Count)
        {
            yield return new ValidationResult(
                "SourceKey must be unique within a batch after trimming whitespace.",
                [nameof(Rows)]);
        }
        if (SectionGid is not null && ProjectGid is null)
        {
            yield return new ValidationResult("SectionGid requires ProjectGid.", [nameof(SectionGid), nameof(ProjectGid)]);
        }
    }
}

public sealed record WbsImportRowResponse(
    Guid Id,
    Guid? ParentRowId,
    int SourceRowNumber,
    string SourceKey,
    int Depth,
    int SortOrder,
    bool Included,
    string Title,
    string Description,
    string? Assignee,
    DateOnly? DueDate,
    string Status,
    IReadOnlyList<string> ValidationErrors,
    string? Provider,
    string? ExternalTaskGid,
    string? ExternalTaskUrl,
    string? ErrorMessage,
    string? AssigneeResolutionStatus,
    string? ResolvedAssigneeName,
    string? WarningMessage);

public sealed record WbsImportBatchResponse(
    Guid Id,
    string Status,
    bool AlreadyRegistered,
    int TotalRows,
    int ValidRows,
    int SucceededRows,
    int FailedRows,
    IReadOnlyList<WbsImportRowResponse> Rows);
