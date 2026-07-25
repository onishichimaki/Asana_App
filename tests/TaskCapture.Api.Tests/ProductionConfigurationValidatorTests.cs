using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TaskCapture.Api.Options;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Production_ReturnsNoIssuesWhenEveryRequiredIntegrationIsConfigured()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TaskCapture"] = "Server=sql;Database=TaskCapture;Integrated Security=true",
            ["DataProtection:KeysPath"] = "C:\\TaskCapture\\Keys"
        });

        var issues = ProductionConfigurationValidator.GetBlockingIssues(
            new TestEnvironment("Production"),
            configuration,
            new DatabaseOptions { Provider = "SqlServer" },
            ConfiguredOrganization(),
            ConfiguredAsana(),
            ConfiguredAccess());

        Assert.Empty(issues);
    }

    [Fact]
    public void Production_ReportsSafeActionableIssuesWithoutSecretValues()
    {
        var issues = ProductionConfigurationValidator.GetBlockingIssues(
            new TestEnvironment("Production"),
            CreateConfiguration(new Dictionary<string, string?>()),
            new DatabaseOptions { Provider = "InMemory" },
            new TaskOrganizationOptions(),
            new AsanaOptions(),
            new AccessOptions());

        Assert.Equal(5, issues.Count);
        Assert.Contains(issues, issue => issue.Contains("SQL Server"));
        Assert.Contains(issues, issue => issue.Contains("SMTP"));
        Assert.Contains(issues, issue => issue.Contains("Asana"));
        Assert.Contains(issues, issue => issue.Contains("Azure OpenAI"));
        Assert.Contains(issues, issue => issue.Contains("Data Protection"));
    }

    [Fact]
    public void Development_DoesNotRequireProductionCredentials()
    {
        var issues = ProductionConfigurationValidator.GetBlockingIssues(
            new TestEnvironment("Development"),
            CreateConfiguration(new Dictionary<string, string?>()),
            new DatabaseOptions { Provider = "InMemory" },
            new TaskOrganizationOptions(),
            new AsanaOptions(),
            new AccessOptions());

        Assert.Empty(issues);
    }

    private static TaskOrganizationOptions ConfiguredOrganization() => new()
    {
        Mode = "AzureOpenAI",
        AzureOpenAI = new AzureOpenAIOptions
        {
            Endpoint = "https://taskcapture.openai.azure.com",
            ApiKey = "test-key",
            DeploymentName = "task-gpt"
        }
    };

    private static AsanaOptions ConfiguredAsana() => new()
    {
        Mode = "Api",
        CredentialMode = "PerUserOAuth",
        OAuth = new AsanaOAuthOptions
        {
            ClientId = "client",
            ClientSecret = "test-secret",
            RedirectUri = "https://taskcapture.example/api/asana/connection/callback",
            RequireMatchingEmail = true
        }
    };

    private static AccessOptions ConfiguredAccess() => new()
    {
        Mode = "EmailCode",
        AllowedEmailDomains = ["example.co.jp"],
        EmailCode = new EmailCodeOptions
        {
            Delivery = new EmailDeliveryOptions
            {
                Mode = "Smtp",
                Host = "smtp.example.co.jp",
                FromAddress = "taskcapture@example.co.jp",
                EnableSsl = true
            }
        }
    };

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "TaskCapture.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
