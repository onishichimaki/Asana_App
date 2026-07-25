using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Data;
using TaskCapture.Api.Infrastructure;
using TaskCapture.Api.Options;
using TaskCapture.Api.Security;
using TaskCapture.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers();

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<TaskOrganizationOptions>(builder.Configuration.GetSection(TaskOrganizationOptions.SectionName));
builder.Services.Configure<AsanaOptions>(builder.Configuration.GetSection(AsanaOptions.SectionName));
builder.Services.Configure<AccessOptions>(builder.Configuration.GetSection(AccessOptions.SectionName));

const string dynamicAuthenticationScheme = "TaskCapture.Dynamic";
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = dynamicAuthenticationScheme;
        options.DefaultChallengeScheme = dynamicAuthenticationScheme;
        options.DefaultSignInScheme = EmailAuthenticationDefaults.Scheme;
    })
    .AddPolicyScheme(
        dynamicAuthenticationScheme,
        dynamicAuthenticationScheme,
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var configured = context.RequestServices
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<AccessOptions>>()
                    .Value;
                if (configured.Mode.Equals("EmailCode", StringComparison.OrdinalIgnoreCase))
                {
                    return EmailAuthenticationDefaults.Scheme;
                }

                if (configured.Mode.Equals("Development", StringComparison.OrdinalIgnoreCase))
                {
                    return DevelopmentAuthenticationHandler.SchemeName;
                }

                throw new InvalidOperationException($"Unsupported access mode: {configured.Mode}");
            };
        })
    .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationHandler.SchemeName,
        _ => { })
    .AddCookie(
        EmailAuthenticationDefaults.Scheme,
        options =>
        {
            var localEnvironment = builder.Environment.IsDevelopment()
                || builder.Environment.IsEnvironment("Testing");
            options.Cookie.Name = localEnvironment ? "TaskCapture.Development" : "__Host-TaskCapture";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = localEnvironment
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            options.Events.OnValidatePrincipal = async context =>
            {
                var sessionText = context.Principal?.FindFirst(
                    EmailAuthenticationDefaults.SessionClaim)?.Value;
                if (!Guid.TryParse(sessionText, out var sessionId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(EmailAuthenticationDefaults.Scheme);
                    return;
                }

                var db = context.HttpContext.RequestServices
                    .GetRequiredService<TaskCaptureDbContext>();
                var now = context.HttpContext.RequestServices
                    .GetRequiredService<TimeProvider>()
                    .GetUtcNow();
                var session = await db.UserSessions
                    .Include(item => item.User)
                    .SingleOrDefaultAsync(
                        item => item.Id == sessionId,
                        context.HttpContext.RequestAborted);
                if (session is null
                    || session.RevokedAtUtc is not null
                    || session.ExpiresAtUtc <= now
                    || !session.User.IsActive)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(EmailAuthenticationDefaults.Scheme);
                    return;
                }

                if (now - session.LastSeenAtUtc > TimeSpan.FromMinutes(10))
                {
                    session.LastSeenAtUtc = now;
                    await db.SaveChangesAsync(context.HttpContext.RequestAborted);
                }
            };
        });

builder.Services.AddHttpContextAccessor();
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("TaskCapture");
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionBuilder.PersistKeysToFileSystem(
        new DirectoryInfo(dataProtectionKeysPath));
}
builder.Services.AddTransient<IClaimsTransformation, AccessClaimsTransformation>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<EmailAuthenticationService>();
builder.Services.AddScoped<MockAccountEmailSender>();
builder.Services.AddScoped<SmtpAccountEmailSender>();
builder.Services.AddScoped<IAccountEmailSender>(services =>
{
    var configured = services.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<AccessOptions>>().Value;
    return configured.EmailCode.Delivery.Mode.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
        ? services.GetRequiredService<SmtpAccountEmailSender>()
        : services.GetRequiredService<MockAccountEmailSender>();
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(TaskCapturePolicies.AllowedClaim, "true")
        .Build();
    options.AddPolicy(
        TaskCapturePolicies.Admin,
        policy => policy.RequireRole(TaskCapturePolicies.Admin));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.User.FindFirst("oid")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 180,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

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
builder.Services.AddHttpClient("AsanaOAuth", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<AsanaConnectionService>();
builder.Services.AddScoped<IAsanaAccessTokenProvider>(services =>
    services.GetRequiredService<AsanaConnectionService>());
builder.Services.AddScoped<ProjectAccessService>();
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
builder.Services.AddScoped<IAsanaMetadataService>(services =>
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
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
}));

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<CurrentUserProvisioningMiddleware>();
app.UseAuthorization();
app.MapControllers();

await DbInitializer.InitializeAsync(app.Services, app.Lifetime.ApplicationStopping);

if (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
{
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

app.Run();

public partial class Program;
