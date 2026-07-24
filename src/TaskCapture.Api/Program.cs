using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Data;
using TaskCapture.Api.Infrastructure;
using TaskCapture.Api.Options;
using TaskCapture.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers();

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<TaskOrganizationOptions>(builder.Configuration.GetSection(TaskOrganizationOptions.SectionName));
builder.Services.Configure<AsanaOptions>(builder.Configuration.GetSection(AsanaOptions.SectionName));

builder.Services.AddDbContext<TaskCaptureDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new();
    if (databaseOptions.Provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase(databaseOptions.InMemoryDatabaseName);
        return;
    }

    var connectionString = configuration.GetConnectionString("TaskCapture");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "SQL Server mode requires ConnectionStrings:TaskCapture (environment: ConnectionStrings__TaskCapture).");
    }
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3));
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RuleBasedTaskOrganizer>();
builder.Services.AddScoped<IGeminiTaskClient, GoogleGeminiTaskClient>();
builder.Services.AddScoped<GeminiTaskOrganizer>();
builder.Services.AddScoped<ITaskOrganizer>(services =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TaskOrganizationOptions>>().Value;
    var ruleBased = services.GetRequiredService<RuleBasedTaskOrganizer>();

    if (options.Mode.Equals("RuleBased", StringComparison.OrdinalIgnoreCase))
    {
        return ruleBased;
    }

    if (!options.Mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Unsupported task organizer mode: {options.Mode}");
    }

    var gemini = services.GetRequiredService<GeminiTaskOrganizer>();
    return options.FallbackToRuleBased
        ? new FallbackTaskOrganizer(
            gemini,
            ruleBased,
            services.GetRequiredService<ILogger<FallbackTaskOrganizer>>())
        : gemini;
});
builder.Services.AddScoped<MockAsanaTaskService>();
builder.Services.AddHttpClient<ApiAsanaTaskService>(client =>
{
    client.BaseAddress = new Uri("https://app.asana.com/api/1.0/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<IAsanaTaskService>(services =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AsanaOptions>>().Value;
    return options.Mode.Equals("Api", StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<ApiAsanaTaskService>()
        : services.GetRequiredService<MockAsanaTaskService>();
});
builder.Services.AddScoped<TaskWorkflowService>();
builder.Services.AddScoped<WbsImportService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

await DbInitializer.InitializeAsync(app.Services, app.Lifetime.ApplicationStopping);

if (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
