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
            [
                "ignore", "title", "description", "assignee", "startDate", "dueDate", "include",
                "key", "parentKey", "level", "hierarchy", "progress", "priority",
                "estimatedHours", "estimatedCost"
            ],
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

        var allowedExtraFields = new HashSet<string>(
            ["progress", "priority", "estimatedHours", "estimatedCost"],
            StringComparer.Ordinal);
        if (CustomFieldTargets.Count > 4 ||
            CustomFieldTargets.Any(pair =>
                !allowedExtraFields.Contains(pair.Key) ||
                pair.Value.Length is < 1 or > 64 ||
                !pair.Value.All(char.IsDigit)))
        {
            yield return new ValidationResult(
                "CustomFieldTargets contains an unsupported item or field number.",
                [nameof(CustomFieldTargets)]);
        }
    }

    public IReadOnlyDictionary<string, string> CustomFieldTargets { get; init; } =
        new Dictionary<string, string>();
    public bool PutUnmatchedExtraFieldsInDescription { get; init; } = true;
}

public sealed class WbsImportProfileRequest : IValidatableObject
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9a-fA-F]{64}$")] public string LayoutSignature { get; init; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 1)] public string SheetName { get; init; } = string.Empty;
    [Range(1, 10_000)] public int HeaderRow { get; init; } = 1;
    [Range(1, 100_000)] public int DataStartRow { get; init; } = 2;
    [Required] public WbsMappingRequest Mapping { get; init; } = new();
    public IReadOnlyDictionary<int, string> ColumnNames { get; init; } = new Dictionary<int, string>();
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
        if (ColumnNames.Count > 500 ||
            ColumnNames.Any(pair => pair.Key is < 0 or > 499 || pair.Value.Length is < 1 or > 200))
        {
            yield return new ValidationResult(
                "ColumnNames contains an unsupported column index or name.",
                [nameof(ColumnNames)]);
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
    public DateOnly? StartDate { get; init; }
    public DateOnly? DueDate { get; init; }
    [StringLength(100)] public string? Progress { get; init; }
    [StringLength(100)] public string? Priority { get; init; }
    [Range(0, 1_000_000)] public decimal? EstimatedHours { get; init; }
    [Range(0, 1_000_000_000)] public decimal? EstimatedCost { get; init; }
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
    public IReadOnlyDictionary<string, string> CustomFieldTargets { get; init; } =
        new Dictionary<string, string>();
    public bool PutUnmatchedExtraFieldsInDescription { get; init; } = true;
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
        var allowedExtraFields = new HashSet<string>(
            ["progress", "priority", "estimatedHours", "estimatedCost"],
            StringComparer.Ordinal);
        if (CustomFieldTargets.Count > 4 ||
            CustomFieldTargets.Any(pair =>
                !allowedExtraFields.Contains(pair.Key) ||
                pair.Value.Length is < 1 or > 64 ||
                !pair.Value.All(char.IsDigit)))
        {
            yield return new ValidationResult(
                "CustomFieldTargets contains an unsupported item or field number.",
                [nameof(CustomFieldTargets)]);
        }
        foreach (var row in Rows.Where(row => row.Included))
        {
            if (row.StartDate is not null && row.DueDate is null)
            {
                yield return new ValidationResult(
                    $"Rows[{row.SourceRowNumber}] requires DueDate when StartDate is specified.",
                    [nameof(Rows)]);
            }
            if (row.StartDate > row.DueDate)
            {
                yield return new ValidationResult(
                    $"Rows[{row.SourceRowNumber}] StartDate must be on or before DueDate.",
                    [nameof(Rows)]);
            }
        }
    }
}

public sealed class WbsRegisterBatchRequest
{
    public bool UpdateExistingTasks { get; init; } = true;
}

public sealed record WbsImportRowResponse(
    Guid Id,
    Guid? ParentRowId,
    string? ParentSourceKey,
    int SourceRowNumber,
    string SourceKey,
    int Depth,
    int SortOrder,
    bool Included,
    string Title,
    string Description,
    string? Assignee,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string? Progress,
    string? Priority,
    decimal? EstimatedHours,
    decimal? EstimatedCost,
    string Status,
    string ChangeType,
    IReadOnlyList<string> ValidationErrors,
    string? Provider,
    string? ExternalTaskGid,
    string? ExternalTaskUrl,
    string? ErrorMessage,
    string? AssigneeResolutionStatus,
    string? ResolvedAssigneeName,
    string? WarningMessage,
    string? PreviousExternalTaskUrl,
    bool WasCreatedInBatch,
    DateTimeOffset? RevertedAtUtc);

public sealed record WbsImportBatchResponse(
    Guid Id,
    string FileName,
    string SheetName,
    string? ProjectGid,
    string? SectionGid,
    string Status,
    bool AlreadyRegistered,
    int TotalRows,
    int ValidRows,
    int SucceededRows,
    int FailedRows,
    int NewRows,
    int ChangedRows,
    int UnchangedRows,
    bool CanUndo,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<WbsImportRowResponse> Rows);

public sealed record WbsImportBatchSummaryResponse(
    Guid Id,
    string FileName,
    string SheetName,
    string Status,
    int TotalRows,
    int SucceededRows,
    int FailedRows,
    int NewRows,
    int ChangedRows,
    int UnchangedRows,
    bool CanUndo,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WbsColumnAliasResponse(string ColumnName, string Role);
