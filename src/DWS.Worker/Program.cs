using System.Net;
using DWS.Core;
using DWS.Core.Json;
using DWS.Core.Logging;
using DWS.Core.Rules;
using DWS.Core.Security;
using DWS.Worker.Security;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateSlimBuilder(args);

// Initialize secure IPC token in %ProgramData%\DWS\ipc.token
IpcSecurity.InitializeSessionToken();

// Verify minimum OS version: Windows 10 22H2 (build 19045)
var osVersion = Environment.OSVersion.Version;
if (osVersion.Build < 19045)
{
    Console.WriteLine($"[FATAL] DWS 11 Enterprise Worker requires Windows 10 22H2 (build 19045) or higher. Current: {osVersion.Build}");
    Environment.Exit(1);
}

// Windows Service configuration
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DWS Enterprise Worker";
});

// Configure Kestrel to listen exclusively on Named Pipe for secure local IPC
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenNamedPipe(IpcSecurity.PipeName);
});

// Native AOT Json source generation
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = DwsJsonContext.Default;
});

// Core Enterprise Services
builder.Services.AddSingleton<SecureRuleEngine>();
builder.Services.AddSingleton<SystemPrivilegeBroker>();
builder.Services.AddSingleton<TransactionJournal>();
builder.Services.AddSingleton<EnterprisePipelineCoordinator>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Apply strict Named Pipe SDDL permissions on startup
app.Lifetime.ApplicationStarted.Register(() =>
{
    IpcSecurity.ApplyPipeSecurity();
});

// IPC Authorization Middleware: Enforce token validation on /api routes
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (!context.Request.Headers.TryGetValue(IpcSecurity.TokenHeaderName, out var token) ||
            !IpcSecurity.ValidateToken(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Unauthorized IPC client: Invalid or missing token.\"}");
            return;
        }
    }
    await next();
});

// Global Exception Handler (Native AOT Safe - Direct UTF8 Output)
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var exception = context.Features.Get<IExceptionHandlerFeature>();
        string msg = exception?.Error.Message ?? "Unknown internal error";
        string escaped = msg.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");

        await context.Response.WriteAsync($"{{\"error\":\"{escaped}\",\"timestamp\":\"{DateTime.UtcNow:O}\"}}");
    });
});

// ═══════════════════════════════════════════════════════════
// ENDPOINTS DE LA API (Minimal APIs - Native AOT Safe)
// ═══════════════════════════════════════════════════════════

// GET /api/status - Current system and rule status
app.MapGet("/api/status", async (
    SecureRuleEngine ruleEngine,
    SystemPrivilegeBroker broker,
    ILogger<Program> logger) =>
{
    try
    {
        var rules = ruleEngine.LoadAndVerifyRulesFromFile("rules/rules.json");
        var bitLockerStatus = await BitLockerGuard.GetProtectionStatusAsync("C:");
        var systemStatus = new SystemStatus(
            Timestamp: DateTime.UtcNow,
            TotalRules: rules.Rules.Count,
            AppliedRules: rules.Rules.Count(r => r.IsApplied),
            PendingRules: rules.Rules.Count(r => !r.IsApplied),
            WatchdogInstalled: broker.IsWatchdogInstalled(),
            BitLockerStatus: bitLockerStatus
        );

        logger.LogInformation("Status requested: {Total} rules", systemStatus.TotalRules);
        return Results.Ok(systemStatus);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error getting system status");
        return Results.Problem(ex.Message);
    }
});

// GET /api/rules - List all loaded rules
app.MapGet("/api/rules", (SecureRuleEngine ruleEngine) =>
{
    var rules = ruleEngine.LoadAndVerifyRulesFromFile("rules/rules.json");
    return Results.Ok(rules.Rules);
});

// POST /api/rules/apply - Apply selected rules or dry-run
app.MapPost("/api/rules/apply", async (
    ApplyRulesRequest request,
    EnterprisePipelineCoordinator coordinator,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    logger.LogInformation("Applying rules (Count: {Count}, DryRun: {DryRun}, Profile: {Profile})",
        request.RuleIds?.Count ?? 0, request.DryRun, request.Profile ?? "default");

    var progress = new Progress<PipelineProgress>(p =>
    {
        logger.LogInformation("[{Level}] {Message}", p.LogLevel, p.Message);
    });

    var result = await coordinator.ExecuteEnterpriseBaselineAsync(
        request.RuleIds,
        isDryRun: request.DryRun,
        progress: progress,
        cancellationToken: cancellationToken);

    return Results.Ok(result);
});

// GET /api/audit/history - Transaction history
app.MapGet("/api/audit/history", (TransactionJournal journal) =>
{
    var history = journal.GetTransactionHistory();
    return Results.Ok(history);
});

// POST /api/audit/revert/{transactionId} - Revert full transaction
app.MapPost("/api/audit/revert/{transactionId}", async (
    string transactionId,
    EnterprisePipelineCoordinator coordinator,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Reverting transaction {TransactionId}", transactionId);
    var success = await coordinator.RollbackTransactionAsync(transactionId);
    return success ? Results.Ok(new { success = true, transactionId }) : Results.BadRequest(new { error = "Transaction not found or already reverted" });
});

// POST /api/audit/revert/{transactionId}/category/{category} - Revert specific category
app.MapPost("/api/audit/revert/{transactionId}/category/{category}", async (
    string transactionId,
    string category,
    TransactionJournal journal,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Reverting category {Category} in transaction {TransactionId}",
        category, transactionId);
    var success = await journal.RevertByCategoryAsync(transactionId, category);
    return success ? Results.Ok(new { success = true, category }) : Results.BadRequest(new { error = "Failed to revert category" });
});

// GET /api/watchdog - Watchdog state
app.MapGet("/api/watchdog", (SystemPrivilegeBroker broker) =>
{
    return Results.Ok(new
    {
        installed = broker.IsWatchdogInstalled(),
        lastRun = broker.GetWatchdogLastRunTime()
    });
});

// POST /api/watchdog/install - Install Watchdog
app.MapPost("/api/watchdog/install", (SystemPrivilegeBroker broker, ILogger<Program> logger) =>
{
    logger.LogInformation("Installing Watchdog persistence task");
    bool ok = broker.InstallWatchdog(out string message);
    return ok ? Results.Ok(new { message }) : Results.Problem(message);
});

// POST /api/watchdog/uninstall - Uninstall Watchdog
app.MapPost("/api/watchdog/uninstall", (SystemPrivilegeBroker broker, ILogger<Program> logger) =>
{
    logger.LogInformation("Uninstalling Watchdog persistence task");
    bool ok = broker.UninstallWatchdog(out string message);
    return ok ? Results.Ok(new { message }) : Results.Problem(message);
});

// Health check
app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy", Environment.OSVersion.VersionString, DateTime.UtcNow)));

app.Run();
