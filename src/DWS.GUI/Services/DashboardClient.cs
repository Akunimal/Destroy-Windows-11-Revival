using System.IO.Pipes;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json;
using DWS.Core;
using DWS.Core.Json;
using DWS.Core.Logging;
using DWS.Core.Rules;
using DWS.Core.Security;

namespace DWS.GUI.Services;

public class DashboardClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly EnterprisePipelineCoordinator _localCoordinator;
    private readonly TransactionJournal _localJournal;
    private readonly SecureRuleEngine _localRuleEngine;
    public const string PipeName = "DWS11_Enterprise_IPC_v2";

    public DashboardClient(
        EnterprisePipelineCoordinator? localCoordinator = null,
        TransactionJournal? localJournal = null,
        SecureRuleEngine? localRuleEngine = null)
    {
        _localCoordinator = localCoordinator ?? new EnterprisePipelineCoordinator();
        _localJournal = localJournal ?? new TransactionJournal();
        _localRuleEngine = localRuleEngine ?? new SecureRuleEngine();

        _httpClient = CreatePipeHttpClient();
    }

    private static HttpClient CreatePipeHttpClient()
    {
        var pipeHandler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var stream = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: PipeName,
                    direction: PipeDirection.InOut,
                    options: PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                    impersonationLevel: TokenImpersonationLevel.Identification);

                await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return stream;
            }
        };

        var authHandler = new TokenAuthHandler(pipeHandler);
        return new HttpClient(authHandler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    private sealed class TokenAuthHandler : DelegatingHandler
    {
        private static string? _cachedToken;

        public TokenAuthHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            EnsureTokenHeader(request);
            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Reload token and retry once if Worker restarted with a new ephemeral token
                ReloadToken();
                EnsureTokenHeader(request);
                response.Dispose();
                response = await base.SendAsync(request, cancellationToken);
            }

            return response;
        }

        private static void EnsureTokenHeader(HttpRequestMessage request)
        {
            string? token = GetCurrentToken();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Remove("X-DWS-IPC-Token");
                request.Headers.Add("X-DWS-IPC-Token", token);
            }
        }

        private static string? GetCurrentToken()
        {
            if (_cachedToken != null) return _cachedToken;
            ReloadToken();
            return _cachedToken;
        }

        private static void ReloadToken()
        {
            string tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DWS", "ipc.token");
            if (File.Exists(tokenPath))
            {
                try
                {
                    _cachedToken = File.ReadAllText(tokenPath).Trim();
                }
                catch { }
            }
        }
    }

    public async Task<SystemStatus?> GetStatusAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<SystemStatus>(
                "/api/status",
                DwsJsonContext.Default.SystemStatus);
        }
        catch
        {
            // In-process fallback when background worker service is not running
            var rules = _localRuleEngine.LoadAndVerifyRulesFromFile("rules/rules.json");
            var bitLockerStatus = await BitLockerGuard.GetProtectionStatusAsync("C:");
            return new SystemStatus(
                Timestamp: DateTime.UtcNow,
                TotalRules: rules.Rules.Count,
                AppliedRules: rules.Rules.Count(r => r.IsApplied),
                PendingRules: rules.Rules.Count(r => !r.IsApplied),
                WatchdogInstalled: new SystemPrivilegeBroker().IsWatchdogInstalled(),
                BitLockerStatus: bitLockerStatus
            );
        }
    }

    public async Task<List<Rule>?> GetRulesAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync(
                "/api/rules",
                DwsJsonContext.Default.ListRule);
        }
        catch
        {
            return _localRuleEngine.LoadAndVerifyRulesFromFile("rules/rules.json").Rules;
        }
    }

    public async Task<PipelineResult?> ApplyRulesAsync(
        List<string> ruleIds,
        bool dryRun,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ApplyRulesRequest(ruleIds, dryRun);
            string jsonPayload = JsonSerializer.Serialize(request, DwsJsonContext.Default.ApplyRulesRequest);
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/rules/apply", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errResp = await response.Content.ReadAsStringAsync(cancellationToken);
                return new PipelineResult
                {
                    Success = false,
                    ErrorMessage = $"Worker error {(int)response.StatusCode}: {errResp}"
                };
            }

            string respString = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize(respString, DwsJsonContext.Default.PipelineResult);
        }
        catch (HttpRequestException)
        {
            // Execute locally in-process when background worker is not running
            var pipeProgress = new Progress<PipelineProgress>(p =>
            {
                progress?.Report($"[{p.LogLevel}] {p.Message}");
            });

            return await _localCoordinator.ExecuteEnterpriseBaselineAsync(
                ruleIds,
                isDryRun: dryRun,
                progress: pipeProgress,
                cancellationToken: cancellationToken);
        }
    }

    public async Task<List<TransactionHistoryItem>?> GetAuditHistoryAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync(
                "/api/audit/history",
                DwsJsonContext.Default.ListTransactionHistoryItem);
        }
        catch
        {
            return _localJournal.GetTransactionHistory();
        }
    }

    public async Task<bool> RevertTransactionAsync(string transactionId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/audit/revert/{transactionId}", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return await _localCoordinator.RollbackTransactionAsync(transactionId);
        }
    }

    public async Task<bool> RevertByCategoryAsync(string transactionId, string category)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/audit/revert/{transactionId}/category/{category}", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return await _localJournal.RevertByCategoryAsync(transactionId, category);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
