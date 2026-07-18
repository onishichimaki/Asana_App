namespace TaskCapture.Api.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public string Provider { get; set; } = "SqlServer";
    public string InMemoryDatabaseName { get; set; } = "TaskCaptureDevelopment";
    public bool ApplyMigrations { get; set; } = true;
}

public sealed class TaskOrganizationOptions
{
    public const string SectionName = "TaskOrganization";
    public string Mode { get; set; } = "RuleBased";
}

public sealed class AsanaOptions
{
    public const string SectionName = "Integration:Asana";
    public string Mode { get; set; } = "Mock";
    public string? PersonalAccessToken { get; set; }
    public string? DefaultWorkspaceGid { get; set; }
}
