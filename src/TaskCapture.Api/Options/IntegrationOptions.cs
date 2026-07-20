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
    public bool FallbackToRuleBased { get; set; } = true;
    public GeminiOptions Gemini { get; set; } = new();
}

public sealed class GeminiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.5-flash";
    public int TimeoutSeconds { get; set; } = 20;
}

public sealed class AsanaOptions
{
    public const string SectionName = "Integration:Asana";
    public string Mode { get; set; } = "Mock";
    public string? PersonalAccessToken { get; set; }
    public string? DefaultWorkspaceGid { get; set; }
    public string? DefaultProjectGid { get; set; }
}
