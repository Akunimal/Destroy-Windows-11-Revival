using System.Security;
using DWS.Core.Rules;
using Xunit;

namespace DWS.Tests;

public class RuleEngineTests
{
    private readonly SecureRuleEngine _engine = new();

    [Fact]
    public void LoadAndVerifyRulesFromFile_LoadsAllRulesSuccessfully()
    {
        // Act
        var ruleSet = _engine.LoadAndVerifyRulesFromFile("rules/rules.json");

        // Assert
        Assert.NotNull(ruleSet);
        Assert.NotNull(ruleSet.Rules);
        Assert.True(ruleSet.Rules.Count >= 35, $"Expected at least 35 rules, but got {ruleSet.Rules.Count}");
        Assert.False(string.IsNullOrWhiteSpace(ruleSet.Signature));
        byte[] sigBytes = Convert.FromBase64String(ruleSet.Signature);
        Assert.Equal(512, sigBytes.Length); // RSA-4096 = 512 bytes signature
    }

    [Fact]
    public void Rules_ContainRequiredEnterpriseCategories()
    {
        var ruleSet = _engine.LoadAndVerifyRulesFromFile("rules/rules.json");
        var categories = ruleSet.Rules.Select(r => r.Category).Distinct().ToList();

        Assert.Contains("Telemetry", categories);
        Assert.Contains("Privacy", categories);
        Assert.Contains("Windows 11", categories);
        Assert.Contains("Security", categories);
        Assert.Contains("Apps", categories);
    }

    [Fact]
    public void EveryRule_HasValidIdAndOperations()
    {
        var ruleSet = _engine.LoadAndVerifyRulesFromFile("rules/rules.json");

        foreach (var rule in ruleSet.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id), "Rule Id cannot be null or empty");
            Assert.False(string.IsNullOrWhiteSpace(rule.DisplayName), $"Rule '{rule.Id}' must have DisplayName");
            Assert.True(rule.MinBuild >= 19045, $"Rule '{rule.Id}' MinBuild must be >= 19045");
            Assert.NotEmpty(rule.Operations);

            foreach (var op in rule.Operations)
            {
                Assert.Contains(op.Type.ToLowerInvariant(), new[] { "registry", "service", "appx", "hosts" });
                if (op.Type.Equals("registry", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True(op.Hive is "HKLM" or "HKCU", $"Hive must be HKLM or HKCU, got {op.Hive}");
                    Assert.False(string.IsNullOrWhiteSpace(op.Path), "Operation Path required");
                    Assert.False(string.IsNullOrWhiteSpace(op.Name), "Operation Name required");
                    Assert.False(string.IsNullOrWhiteSpace(op.Value), "Operation Value required");
                }
                else if (op.Type.Equals("service", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.False(string.IsNullOrWhiteSpace(op.ServiceName ?? op.Name), "Service name required");
                }
                else if (op.Type.Equals("appx", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.False(string.IsNullOrWhiteSpace(op.PackagePattern ?? op.Name ?? op.Value), "AppX pattern required");
                }
                else if (op.Type.Equals("hosts", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True((op.Domains != null && op.Domains.Count > 0) || !string.IsNullOrWhiteSpace(op.Value), "Hosts domains required");
                }
            }
        }
    }

    [Fact]
    public void TamperedOrUnsignedRuleSet_ThrowsSecurityException()
    {
        string tamperedPayload = """
        {
            "schemaVersion": "2.0.0",
            "signature": "INVALID_SIGNATURE_ATTEMPT",
            "rules": [
                {
                    "id": "fake_rule",
                    "category": "Malicious",
                    "displayName": "Injected Rule",
                    "description": "Test",
                    "minBuild": 19045,
                    "operations": []
                }
            ]
        }
        """;

        Assert.Throws<SecurityException>(() => _engine.LoadAndVerifyRules(tamperedPayload));
    }

    [Fact]
    public void TamperedRulesJsonContent_WithValidOldSignature_FailsVerification()
    {
        var ruleSet = _engine.LoadAndVerifyRulesFromFile("rules/rules.json");
        string originalSig = ruleSet.Signature!;

        // Adversary alters one rule operation value but keeps the genuine signature
        ruleSet.Rules[0].Operations[0].Value = "999";
        ruleSet.Signature = originalSig;

        string tamperedJson = System.Text.Json.JsonSerializer.Serialize(ruleSet, DWS.Core.Json.DwsJsonContext.Default.RuleSet);

        Assert.Throws<SecurityException>(() => _engine.LoadAndVerifyRules(tamperedJson));
    }

    [Fact]
    public void LoadEmbeddedRules_LoadsAndVerifiesAll37Rules()
    {
        var embeddedSet = _engine.LoadEmbeddedRules();

        Assert.NotNull(embeddedSet);
        Assert.NotNull(embeddedSet.Rules);
        Assert.Equal(37, embeddedSet.Rules.Count);
        Assert.False(string.IsNullOrWhiteSpace(embeddedSet.Signature));
        byte[] sigBytes = Convert.FromBase64String(embeddedSet.Signature);
        Assert.Equal(512, sigBytes.Length);
    }

    [Fact]
    public void LoadAndVerifyRulesFromFile_WhenFileDoesNotExist_FallsBackToEmbedded37Rules()
    {
        // When non-existent file is requested, fallback must load all 37 embedded rules
        var ruleSet = _engine.LoadAndVerifyRulesFromFile("non_existent_rules_path.json");

        Assert.NotNull(ruleSet);
        Assert.NotNull(ruleSet.Rules);
        Assert.Equal(37, ruleSet.Rules.Count);
    }

    [Fact]
    public void LoadAndVerifyRulesFromFile_WhenTamperedExternalFileProvided_ThrowsSecurityException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"dws_tamper_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string fakeRulesPath = Path.Combine(tempDir, "rules.json");

        try
        {
            string tamperedContent = """
            {
                "schemaVersion": "2.0.0",
                "signature": "BAD_SIGNATURE",
                "rules": []
            }
            """;
            File.WriteAllText(fakeRulesPath, tamperedContent);

            // An invalid external override must be rejected with SecurityException
            Assert.Throws<SecurityException>(() => _engine.LoadAndVerifyRulesFromFile(fakeRulesPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void DefaultEnterpriseRuleSet_IsValidAndNotEmpty()
    {
        var defaultSet = SecureRuleEngine.GetDefaultEnterpriseRuleSet();

        Assert.NotNull(defaultSet);
        Assert.NotEmpty(defaultSet.Rules);
        Assert.False(string.IsNullOrWhiteSpace(defaultSet.Signature));

        // Must verify cryptographically with RSA-4096-PSS
        string json = System.Text.Json.JsonSerializer.Serialize(defaultSet, DWS.Core.Json.DwsJsonContext.Default.RuleSet);
        var verified = _engine.LoadAndVerifyRules(json);
        Assert.NotNull(verified);
        Assert.Equal(defaultSet.Rules.Count, verified.Rules.Count);
    }
}
