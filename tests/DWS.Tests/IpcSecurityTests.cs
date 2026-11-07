using DWS.GUI.Services;
using DWS.Worker.Security;
using Xunit;

namespace DWS.Tests;

public class IpcSecurityTests
{
    [Fact]
    public void IpcSecurity_TokenGenerationAndValidation_Succeeds()
    {
        string token = IpcSecurity.InitializeSessionToken();

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(token.Length >= 64); // 32 bytes hex = 64 characters

        // Correct token validates
        Assert.True(IpcSecurity.ValidateToken(token));

        // Wrong token fails
        Assert.False(IpcSecurity.ValidateToken("invalid_tampered_token_string"));
        Assert.False(IpcSecurity.ValidateToken(null));
        Assert.False(IpcSecurity.ValidateToken(""));
    }

    [Fact]
    public void PipeName_MatchesBetweenWorkerAndGuiClient()
    {
        Assert.Equal(IpcSecurity.PipeName, DashboardClient.PipeName);
        Assert.Equal("DWS11_Enterprise_IPC_v2", IpcSecurity.PipeName);
    }

    [Fact]
    public async Task DashboardClient_FallbackToInProcess_WhenWorkerNotRunning()
    {
        using var client = new DashboardClient();

        // When Worker is not running, GetStatusAsync falls back to in-process local evaluation
        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status.TotalRules >= 35);
    }

    [Fact]
    public async Task DashboardClient_GetRulesAsync_FallsBackGracefully()
    {
        using var client = new DashboardClient();

        var rules = await client.GetRulesAsync();

        Assert.NotNull(rules);
        Assert.True(rules.Count >= 35);
    }
}
