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
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
}

public sealed class GeminiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.5-flash";
    public int TimeoutSeconds { get; set; } = 20;
}

public sealed class AzureOpenAIOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DeploymentName { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
}

public sealed class AsanaOptions
{
    public const string SectionName = "Integration:Asana";
    public string Mode { get; set; } = "Mock";
    public string CredentialMode { get; set; } = "PersonalAccessToken";
    public string? PersonalAccessToken { get; set; }
    public string? DefaultWorkspaceGid { get; set; }
    public string? DefaultProjectGid { get; set; }
    public AsanaOAuthOptions OAuth { get; set; } = new();
}

public sealed class AsanaOAuthOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
    public string? ConnectionRedirectUri { get; set; }
    public bool RequireMatchingEmail { get; set; } = true;
    public string[] Scopes { get; set; } =
    [
        "projects:read",
        "sections:read",
        "tasks:read",
        "tasks:write",
        "tasks:delete",
        "users:read",
        "workspaces:read"
    ];
}

public sealed class AccessOptions
{
    public const string SectionName = "Access";
    public string Mode { get; set; } = "Development";
    public string LocalEmail { get; set; } = "local-user@taskcapture.local";
    public string LocalDisplayName { get; set; } = "ローカル利用者";
    public string[] AdminEmails { get; set; } = [];
    public string[] AllowedEmailDomains { get; set; } = [];
    public AsanaLoginOptions AsanaLogin { get; set; } = new();
    public EmailCodeOptions EmailCode { get; set; } = new();
}

public sealed class AsanaLoginOptions
{
    public int CorrelationLifetimeMinutes { get; set; } = 10;
    public int SessionDays { get; set; } = 14;
}

public sealed class EmailCodeOptions
{
    public int CodeLifetimeMinutes { get; set; } = 10;
    public int SessionDays { get; set; } = 14;
    public int MaximumAttempts { get; set; } = 5;
    public EmailDeliveryOptions Delivery { get; set; } = new();
}

public sealed class EmailDeliveryOptions
{
    public string Mode { get; set; } = "Mock";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Task Capture";
}
