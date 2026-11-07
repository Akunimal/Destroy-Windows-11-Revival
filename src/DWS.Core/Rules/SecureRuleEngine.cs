using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using DWS.Core.Json;
using Microsoft.Extensions.Logging;

namespace DWS.Core.Rules;

public class SecureRuleEngine
{
    private readonly ILogger<SecureRuleEngine>? _logger;

    // Hardcoded Enterprise RSA-4096 Public Key (Pinned Authority, SPKI DER format)
    public const string EnterprisePublicKeyBase64 =
        "MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAooYW3TM5V2+ovq/ytbTtchlNGtOwBawPtbtTyKR+bLRGar0qQlOque7UbzSRumD2vZ/EvjERJ3TV8gcwDHeR8x/oHubKY9CuFEZGpSGWGF7oJ51YuhfGgcoStxgLCMsR2iqL+uHJD+acxw/R8ByHiH2QwR65bUnQf9y7oBwlGluaeAcwLWfUgcEmhhb0tSEw6zsPyC529JwxgHLHZi1KPfJAanUMH+6AVrx1pBxWJUpk/6ujqsB+jJ2oYItdxiWPMVObjo2EOYMxzVe7Svl1KAwXQeWROmIvsMoELogADNTQsvE6QZdUjlY343iq1TwqkFSFzu5rSrDQGR+mixm4z3xjP4twAexPW5OZv20onSCcTH4IKUdcS2MbEsEPWHdW2VNMRvm/3N6q+tGsvLFFZyJygMzN2p5qurWQvrZnJQZis7mPBLUIymd3R5bD4f0TaVKpcczJXBSB0sFNFcG4SgqHbF5Qpf2BM6yiiX3G1G/0DKjJigOe7NlSJHXQ2XMzL7aHmagICqFZprphPUtc1wVi9HVEjt1a2QBh9p9aCupiE48u2dlD6DjVvOj2ufqWm0qIuREL6lLZY7UhJ1yvekY+W2O2EGBqMuaIEJPKtJiPgFkBzmOglVN9VIh59gNuNhdlLM4UyFZ1FypF5RhdqCukLC3v/1ZEVCMAN9ZMub0CAwEAAQ==";

    public static byte[] EnterprisePublicKeyBytes { get; set; } = Convert.FromBase64String(EnterprisePublicKeyBase64);

    public SecureRuleEngine(ILogger<SecureRuleEngine>? logger = null)
    {
        _logger = logger;
    }

    public RuleSet LoadAndVerifyRules(string jsonPayload)
    {
        _logger?.LogInformation("Loading and verifying rules from payload");

        var ruleSet = JsonSerializer.Deserialize(jsonPayload, DwsJsonContext.Default.RuleSet);

        if (ruleSet == null)
        {
            throw new InvalidOperationException("Failed to deserialize rules JSON.");
        }

        if (!VerifySignature(ruleSet))
        {
            throw new SecurityException("Security Verification Failed: Digital signature mismatch or unsigned rule package.");
        }

        _logger?.LogInformation("Successfully loaded and verified {Count} rules", ruleSet.Rules.Count);
        return ruleSet;
    }

    public RuleSet LoadAndVerifyRulesFromFile(string filePath)
    {
        string? targetPath = null;
        string fileName = Path.GetFileName(filePath);

        var candidates = new List<string>
        {
            filePath,
            Path.Combine(AppContext.BaseDirectory, filePath),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "rules", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), filePath),
            Path.Combine(Directory.GetCurrentDirectory(), "rules", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName)
        };

        // Traverse upwards from AppContext.BaseDirectory to find repository root if running inside bin/
        string? probeDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5 && !string.IsNullOrEmpty(probeDir); i++)
        {
            candidates.Add(Path.Combine(probeDir, filePath));
            candidates.Add(Path.Combine(probeDir, "rules", fileName));
            probeDir = Path.GetDirectoryName(probeDir);
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                targetPath = candidate;
                break;
            }
        }

        if (targetPath == null)
        {
            // If file does not exist, return built-in baseline
            _logger?.LogWarning("Rules file not found at '{Path}'. Using built-in baseline.", filePath);
            return GetDefaultEnterpriseRuleSet();
        }

        string jsonPayload = File.ReadAllText(targetPath);
        return LoadAndVerifyRules(jsonPayload);
    }

    public static byte[] GetCanonicalPayloadBytes(RuleSet ruleSet)
    {
        var unsignedRuleSet = new RuleSet
        {
            SchemaVersion = ruleSet.SchemaVersion,
            Rules = ruleSet.Rules,
            Signature = null
        };

        return JsonSerializer.SerializeToUtf8Bytes(
            unsignedRuleSet,
            DwsJsonContext.Default.RuleSet);
    }

    private bool VerifySignature(RuleSet ruleSet)
    {
        if (string.IsNullOrEmpty(ruleSet.Signature))
        {
            _logger?.LogWarning("Rule set has no signature.");
            return false;
        }

        try
        {
            if (EnterprisePublicKeyBytes.Length == 0)
            {
                _logger?.LogWarning("Enterprise public key not configured.");
                return false;
            }

            byte[] jsonBytes = GetCanonicalPayloadBytes(ruleSet);
            byte[] signatureBytes = Convert.FromBase64String(ruleSet.Signature);

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(EnterprisePublicKeyBytes, out _);

            return rsa.VerifyData(
                jsonBytes,
                signatureBytes,
                HashAlgorithmName.SHA512,
                RSASignaturePadding.Pss);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during signature verification");
            return false;
        }
    }

    public static RuleSet GetDefaultEnterpriseRuleSet()
    {
        return new RuleSet
        {
            SchemaVersion = "2.0.0",
            Signature = "bsvfjkjokNTJ97CyXjcogOLzEp4NBMwIh0reyW7s2CN2WGqM82lDx0aKCYtWv/EkRNZQU5N0zUshuG+pa/6H0yY88YYLnlvacL3QeM+12hSiguye3u82sd7cNA5Gupg2kM8mQDbRlD4SSHuY8niETqZR/05AyCseXV72fpI6QPAug8BUL99UTQxYvAnBx50VDWnKjP4ddF05cmIP1nl8H0a17El/Mcbmhue3fkr4L+eAV8XgZQTqC7FjbbloBxxHgxSEce2YrSHKTeAKORmASYGn90C4Wh9fL8mt+eyT7uSrISkmraReQTEetMHgoLUe54/5Gga5ydEOrz/ik/nQ9UMVK/J1eG+ldvii7EJ5lP67yBBzN/DeNcw+5zlhiE+n3VHDKOJpn3pC2NBxSZv9Uzjn4uTdD0pEYp20Z3zp20C4PfIb0v0i5XjKNctlRfccmLNE64UReGCz1Uma8A0YJjesBpjfR19t9FWP+2OLLHR8BhDo+LHes9vFEkPWC+LQd+2MSbJUc7Ef0e1qZ/fibrcu2B3O/Ondo/zxHgPue1tpSIjcLjvBo1PBJxC62hGq38nyHmrvwn9ABbnkTv4WrkadaC25W6E37i8kSs62vBghx2TghugYToK3fMrbzvQOYk5+tbxiWe7laHO3pciyLJjXEJswQPeo0GCWcdql+Uw=",
            Rules = new List<Rule>
            {
                new()
                {
                    Id = "disable_telemetry",
                    Category = "Telemetry",
                    DisplayName = "Disable Diagnostic Data Collection (Telemetry)",
                    Description = "Forces AllowTelemetry to 0 via Group Policy.",
                    MinBuild = 19045,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                            Name = "AllowTelemetry",
                            Kind = "DWord",
                            Value = "0"
                        }
                    }
                },
                new()
                {
                    Id = "disable_copilot",
                    Category = "AI",
                    DisplayName = "Disable Windows Copilot",
                    Description = "Disables Windows Copilot integration across user sessions.",
                    MinBuild = 22621,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot",
                            Name = "TurnOffWindowsCopilot",
                            Kind = "DWord",
                            Value = "1"
                        }
                    }
                },
                new()
                {
                    Id = "disable_bing_search",
                    Category = "Privacy",
                    DisplayName = "Disable Bing Web Search in Start Menu",
                    Description = "Prevents Windows Search from sending queries to Bing over the network.",
                    MinBuild = 19045,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKCU",
                            Path = @"Software\Policies\Microsoft\Windows\Explorer",
                            Name = "DisableSearchBoxSuggestions",
                            Kind = "DWord",
                            Value = "1"
                        }
                    }
                },
                new()
                {
                    Id = "disable_cortana",
                    Category = "Privacy",
                    DisplayName = "Disable Cortana Voice Assistant",
                    Description = "Disables Cortana integration in Windows Search.",
                    MinBuild = 19045,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                            Name = "AllowCortana",
                            Kind = "DWord",
                            Value = "0"
                        }
                    }
                },
                new()
                {
                    Id = "disable_advertising_id",
                    Category = "Privacy",
                    DisplayName = "Disable Advertising ID for User Tracking",
                    Description = "Disables the advertising identifier used by apps for profiling.",
                    MinBuild = 19045,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo",
                            Name = "DisabledByGroupPolicy",
                            Kind = "DWord",
                            Value = "1"
                        }
                    }
                },
                new()
                {
                    Id = "disable_wpbt",
                    Category = "Security",
                    DisplayName = "Disable WPBT (Windows Platform Binary Table Execution)",
                    Description = "Blocks OEM firmware from injecting third-party binaries during boot.",
                    MinBuild = 19045,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SYSTEM\CurrentControlSet\Control\Session Manager",
                            Name = "DisableWPBTExecution",
                            Kind = "DWord",
                            Value = "1"
                        }
                    }
                },
                new()
                {
                    Id = "disable_edge_prelaunch",
                    Category = "Performance",
                    DisplayName = "Disable Microsoft Edge Background Prelaunch",
                    Description = "Stops Edge from prelaunching in background during system startup.",
                    MinBuild = 19045,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SOFTWARE\Policies\Microsoft\MicrosoftEdge\Main",
                            Name = "AllowPrelaunch",
                            Kind = "DWord",
                            Value = "0"
                        }
                    }
                },
                new()
                {
                    Id = "disable_widgets",
                    Category = "UI",
                    DisplayName = "Disable Windows Widgets (News & Interests)",
                    Description = "Disables background widget process feed and taskbar widget icon.",
                    MinBuild = 22000,
                    IsSelected = true,
                    Operations = new List<RuleOperation>
                    {
                        new()
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SOFTWARE\Policies\Microsoft\Dsh",
                            Name = "AllowNewsAndInterests",
                            Kind = "DWord",
                            Value = "0"
                        }
                    }
                }
            }
        };
    }
}
