using TaskCapture.Api.Options;

namespace TaskCapture.Api.Services;

public static class ProductionConfigurationValidator
{
    public static IReadOnlyList<string> GetBlockingIssues(
        IHostEnvironment environment,
        IConfiguration configuration,
        DatabaseOptions database,
        TaskOrganizationOptions organization,
        AsanaOptions asana,
        AccessOptions access)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return [];
        }

        var issues = new List<string>();
        if (!database.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(configuration.GetConnectionString("TaskCapture")))
        {
            issues.Add("本番DBとしてSQL Serverの接続文字列を設定してください。");
        }

        if (!access.Mode.Equals("AsanaOAuth", StringComparison.OrdinalIgnoreCase)
            || access.AllowedEmailDomains.Length == 0)
        {
            issues.Add("本番ログインをAsanaOAuthにし、許可する会社メールドメインを設定してください。");
        }

        var requiredScopes = new[]
        {
            "projects:read", "sections:read", "tasks:read", "tasks:write", "tasks:delete",
            "users:read", "workspaces:read"
        };
        if (!asana.Mode.Equals("Api", StringComparison.OrdinalIgnoreCase)
            || !asana.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(asana.DefaultWorkspaceGid)
            || string.IsNullOrWhiteSpace(asana.OAuth.ClientId)
            || string.IsNullOrWhiteSpace(asana.OAuth.ClientSecret)
            || !IsHttps(asana.OAuth.RedirectUri)
            || !asana.OAuth.RequireMatchingEmail
            || requiredScopes.Any(required => !asana.OAuth.Scopes.Contains(
                required,
                StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add("Asanaを社内ワークスペース指定・利用者別OAuth・必要scope・HTTPS callbackで設定してください。");
        }

        var azure = organization.AzureOpenAI;
        var azureKey = string.IsNullOrWhiteSpace(azure.ApiKey)
            ? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            : azure.ApiKey;
        if (!organization.Mode.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase)
            || !IsHttps(azure.Endpoint)
            || string.IsNullOrWhiteSpace(azure.DeploymentName)
            || string.IsNullOrWhiteSpace(azureKey))
        {
            issues.Add("Azure OpenAIのHTTPS endpoint、deployment、APIキーを設定してください。");
        }

        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath) || !Path.IsPathRooted(keysPath))
        {
            issues.Add("Data Protection鍵の永続保存先を絶対パスで設定してください。");
        }

        return issues;
    }

    private static bool IsHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
