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

        var delivery = access.EmailCode.Delivery;
        if (!access.Mode.Equals("EmailCode", StringComparison.OrdinalIgnoreCase)
            || access.AllowedEmailDomains.Length == 0
            || !delivery.Mode.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(delivery.Host)
            || string.IsNullOrWhiteSpace(delivery.FromAddress)
            || !delivery.EnableSsl)
        {
            issues.Add("会社メール確認コード用の許可ドメインとSSL対応SMTPを設定してください。");
        }

        if (!asana.Mode.Equals("Api", StringComparison.OrdinalIgnoreCase)
            || !asana.CredentialMode.Equals("PerUserOAuth", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(asana.OAuth.ClientId)
            || string.IsNullOrWhiteSpace(asana.OAuth.ClientSecret)
            || !IsHttps(asana.OAuth.RedirectUri)
            || !asana.OAuth.RequireMatchingEmail)
        {
            issues.Add("Asanaを利用者別OAuth・同一メール必須・HTTPS callbackで設定してください。");
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
