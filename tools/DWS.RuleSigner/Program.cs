using System.Security.Cryptography;
using System.Text.Json;
using DWS.Core.Json;
using DWS.Core.Rules;

namespace DWS.RuleSigner;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string command = args[0].ToLowerInvariant();

        try
        {
            switch (command)
            {
                case "keygen":
                    string outDir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "keys");
                    return KeyGen(outDir);

                case "sign":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Error: Missing arguments for sign command.");
                        PrintUsage();
                        return 1;
                    }
                    return Sign(args[1], args[2]);

                case "verify":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: Missing rules file path for verify command.");
                        PrintUsage();
                        return 1;
                    }
                    string? pubKeyPath = args.Length > 2 ? args[2] : null;
                    return Verify(args[1], pubKeyPath);

                case "sign-default":
                    string privPath = args.Length > 1 ? args[1] : "tools/keys/dws_enterprise_private.pkcs8";
                    return SignDefault(privPath);

                case "upgrade-catalog":
                    string catRulesPath = args.Length > 1 ? args[1] : "rules/rules.json";
                    string catPrivPath = args.Length > 2 ? args[2] : "tools/keys/dws_enterprise_private.pkcs8";
                    return UpgradeCatalog(catRulesPath, catPrivPath);

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("DWS 11 Enterprise Rule Signer & Key Tool (v2.0)");
        Console.WriteLine("Usage:");
        Console.WriteLine("  DWS.RuleSigner keygen [outputDirectory]");
        Console.WriteLine("  DWS.RuleSigner sign <rules.json> <privateKey.pkcs8>");
        Console.WriteLine("  DWS.RuleSigner verify <rules.json> [publicKey.spki]");
    }

    private static int KeyGen(string outDir)
    {
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"Generating enterprise RSA-4096 keypair in: {outDir}");

        using var rsa = RSA.Create(4096);
        byte[] privBytes = rsa.ExportPkcs8PrivateKey();
        byte[] pubBytes = rsa.ExportSubjectPublicKeyInfo();
        string pubB64 = Convert.ToBase64String(pubBytes);

        string privPath = Path.Combine(outDir, "dws_enterprise_private.pkcs8");
        string pubPath = Path.Combine(outDir, "dws_enterprise_public.spki");
        string pubB64Path = Path.Combine(outDir, "dws_enterprise_public.b64");

        File.WriteAllBytes(privPath, privBytes);
        File.WriteAllBytes(pubPath, pubBytes);
        File.WriteAllText(pubB64Path, pubB64);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("RSA-4096 Keypair successfully generated!");
        Console.WriteLine($"  Private Key: {privPath} ({privBytes.Length} bytes)");
        Console.WriteLine($"  Public Key (SPKI): {pubPath} ({pubBytes.Length} bytes)");
        Console.WriteLine($"  Public Key (Base64): {pubB64Path}");
        Console.ResetColor();

        Console.WriteLine();
        Console.WriteLine("C# Base64 for SecureRuleEngine.cs:");
        Console.WriteLine($"\"{pubB64}\"");

        return 0;
    }

    private static int Sign(string rulesPath, string privateKeyPath)
    {
        if (!File.Exists(rulesPath))
        {
            Console.WriteLine($"Rules file not found: {rulesPath}");
            return 1;
        }
        if (!File.Exists(privateKeyPath))
        {
            Console.WriteLine($"Private key file not found: {privateKeyPath}");
            return 1;
        }

        string json = File.ReadAllText(rulesPath);
        var ruleSet = JsonSerializer.Deserialize(json, DwsJsonContext.Default.RuleSet);
        if (ruleSet == null)
        {
            Console.WriteLine("Failed to deserialize RuleSet.");
            return 1;
        }

        byte[] payloadBytes = SecureRuleEngine.GetCanonicalPayloadBytes(ruleSet);

        using var rsa = RSA.Create();
        byte[] privKeyBytes = File.ReadAllBytes(privateKeyPath);
        rsa.ImportPkcs8PrivateKey(privKeyBytes, out _);

        byte[] signatureBytes = rsa.SignData(
            payloadBytes,
            HashAlgorithmName.SHA512,
            RSASignaturePadding.Pss);

        string signatureB64 = Convert.ToBase64String(signatureBytes);
        ruleSet.Signature = signatureB64;

        // Write updated rules.json with indented formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = DwsJsonContext.Default
        };
        string updatedJson = JsonSerializer.Serialize(ruleSet, DwsJsonContext.Default.RuleSet);

        File.WriteAllText(rulesPath, updatedJson);

        // Also write detached .sig file
        string sigPath = Path.ChangeExtension(rulesPath, ".json.sig");
        File.WriteAllBytes(sigPath, signatureBytes);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully signed {ruleSet.Rules.Count} rules with RSA-4096-PSS SHA-512!");
        Console.WriteLine($"Updated rules file: {rulesPath}");
        Console.WriteLine($"Detached signature: {sigPath} ({signatureBytes.Length} bytes)");
        Console.ResetColor();

        return 0;
    }

    private static int Verify(string rulesPath, string? pubKeyPath)
    {
        if (!File.Exists(rulesPath))
        {
            Console.WriteLine($"Rules file not found: {rulesPath}");
            return 1;
        }

        string json = File.ReadAllText(rulesPath);
        var ruleSet = JsonSerializer.Deserialize(json, DwsJsonContext.Default.RuleSet);
        if (ruleSet == null || string.IsNullOrEmpty(ruleSet.Signature))
        {
            Console.WriteLine("Rule set is missing or has no signature.");
            return 1;
        }

        byte[] payloadBytes = SecureRuleEngine.GetCanonicalPayloadBytes(ruleSet);
        byte[] sigBytes = Convert.FromBase64String(ruleSet.Signature);

        using var rsa = RSA.Create();
        if (!string.IsNullOrEmpty(pubKeyPath) && File.Exists(pubKeyPath))
        {
            byte[] pubBytes = File.ReadAllBytes(pubKeyPath);
            rsa.ImportSubjectPublicKeyInfo(pubBytes, out _);
        }
        else
        {
            rsa.ImportSubjectPublicKeyInfo(SecureRuleEngine.EnterprisePublicKeyBytes, out _);
        }

        bool valid = rsa.VerifyData(payloadBytes, sigBytes, HashAlgorithmName.SHA512, RSASignaturePadding.Pss);
        if (valid)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Signature Verified Successfully (RSA-4096-PSS / SHA-512).");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Signature Verification FAILED: Invalid signature or modified payload.");
            Console.ResetColor();
            return 1;
        }
    }

    private static int SignDefault(string privateKeyPath)
    {
        if (!File.Exists(privateKeyPath))
        {
            Console.WriteLine($"Private key file not found: {privateKeyPath}");
            return 1;
        }

        var defaultSet = SecureRuleEngine.GetDefaultEnterpriseRuleSet();
        byte[] payloadBytes = SecureRuleEngine.GetCanonicalPayloadBytes(defaultSet);

        using var rsa = RSA.Create();
        byte[] privKeyBytes = File.ReadAllBytes(privateKeyPath);
        rsa.ImportPkcs8PrivateKey(privKeyBytes, out _);

        byte[] signatureBytes = rsa.SignData(
            payloadBytes,
            HashAlgorithmName.SHA512,
            RSASignaturePadding.Pss);

        string signatureB64 = Convert.ToBase64String(signatureBytes);
        Console.WriteLine("Computed Signature for DefaultEnterpriseRuleSet:");
        Console.WriteLine($"\"{signatureB64}\"");
        return 0;
    }

    private static int UpgradeCatalog(string rulesPath, string privateKeyPath)
    {
        if (!File.Exists(rulesPath))
        {
            Console.WriteLine($"Rules file not found: {rulesPath}");
            return 1;
        }

        string json = File.ReadAllText(rulesPath);
        var ruleSet = JsonSerializer.Deserialize(json, DwsJsonContext.Default.RuleSet);
        if (ruleSet == null)
        {
            Console.WriteLine("Failed to deserialize RuleSet.");
            return 1;
        }

        foreach (var rule in ruleSet.Rules)
        {
            switch (rule.Id)
            {
                case "disable_diagtrack_service":
                    if (!rule.Operations.Any(o => o.Type == "service" && o.ServiceName == "DiagTrack"))
                    {
                        rule.Operations.Add(new RuleOperation { Type = "service", ServiceName = "DiagTrack", ServiceStartMode = "Disabled" });
                    }
                    if (!rule.Operations.Any(o => o.Type == "service" && o.ServiceName == "dmwappushservice"))
                    {
                        rule.Operations.Add(new RuleOperation { Type = "service", ServiceName = "dmwappushservice", ServiceStartMode = "Disabled" });
                    }
                    break;

                case "add_to_hosts":
                    if (!rule.Operations.Any(o => o.Type == "registry" && o.Name == "EnableAutoDoH"))
                    {
                        rule.Operations.Add(new RuleOperation
                        {
                            Type = "registry",
                            Hive = "HKLM",
                            Path = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters",
                            Name = "EnableAutoDoH",
                            Kind = "DWord",
                            Value = "0"
                        });
                    }
                    if (!rule.Operations.Any(o => o.Type == "hosts"))
                    {
                        rule.Operations.Add(new RuleOperation
                        {
                            Type = "hosts",
                            Domains = new List<string>
                            {
                                "v10.events.data.microsoft.com",
                                "v20.events.data.microsoft.com",
                                "settings-win.data.microsoft.com",
                                "telemetry.microsoft.com",
                                "watson.telemetry.microsoft.com",
                                "diagnostics.support.microsoft.com",
                                "feedback.windows.com",
                                "telecommand.telemetry.microsoft.com",
                                "sqm.telemetry.microsoft.com",
                                "oca.telemetry.microsoft.com",
                                "watson.microsoft.com",
                                "redir.metaservices.microsoft.com",
                                "choice.microsoft.com",
                                "df.telemetry.microsoft.com",
                                "reports.wes.df.telemetry.microsoft.com",
                                "services.wes.df.telemetry.microsoft.com",
                                "sqm.df.telemetry.microsoft.com",
                                "watson.ppe.telemetry.microsoft.com",
                                "telemetry.appex.bing.net",
                                "telemetry.urs.microsoft.com",
                                "settings-sandbox.data.microsoft.com",
                                "v10.vortex-win.data.microsoft.com",
                                "v20.vortex-win.data.microsoft.com",
                                "activity.windows.com",
                                "browser.pipe.aria.microsoft.com"
                            }
                        });
                    }
                    break;

                case "app_copilot":
                    AddAppxIfMissing(rule, "Microsoft.Copilot");
                    AddAppxIfMissing(rule, "Microsoft.Windows.AIHub");
                    AddAppxIfMissing(rule, "Microsoft.Windows.Ai.Copilot.Provider");
                    break;

                case "app_teams":
                    AddAppxIfMissing(rule, "MicrosoftTeams");
                    AddAppxIfMissing(rule, "MSTeams");
                    break;

                case "app_outlook":
                    AddAppxIfMissing(rule, "Microsoft.OutlookForWindows");
                    break;

                case "app_clipchamp":
                    AddAppxIfMissing(rule, "Clipchamp.Clipchamp");
                    break;

                case "app_xbox":
                    AddServiceIfMissing(rule, "XblAuthManager");
                    AddServiceIfMissing(rule, "XblGameSave");
                    AddServiceIfMissing(rule, "XboxGipSvc");
                    AddServiceIfMissing(rule, "XboxNetApiSvc");
                    AddAppxIfMissing(rule, "Microsoft.XboxApp");
                    AddAppxIfMissing(rule, "Microsoft.GamingApp");
                    AddAppxIfMissing(rule, "Microsoft.XboxGameOverlay");
                    AddAppxIfMissing(rule, "Microsoft.XboxGamingOverlay");
                    break;

                case "app_solitaire":
                    AddAppxIfMissing(rule, "Microsoft.MicrosoftSolitaireCollection");
                    break;

                case "app_bing":
                    AddAppxIfMissing(rule, "Microsoft.BingNews");
                    AddAppxIfMissing(rule, "Microsoft.BingWeather");
                    AddAppxIfMissing(rule, "Microsoft.BingFinance");
                    AddAppxIfMissing(rule, "Microsoft.BingSports");
                    AddAppxIfMissing(rule, "Microsoft.BingFoodAndDrink");
                    AddAppxIfMissing(rule, "Microsoft.BingHealthAndFitness");
                    AddAppxIfMissing(rule, "Microsoft.BingTravel");
                    AddAppxIfMissing(rule, "Microsoft.BingTranslator");
                    break;

                case "app_people_onenote":
                    AddAppxIfMissing(rule, "Microsoft.People");
                    AddAppxIfMissing(rule, "Microsoft.Office.OneNote");
                    AddAppxIfMissing(rule, "Microsoft.MicrosoftStickyNotes");
                    break;

                case "app_3d":
                    AddAppxIfMissing(rule, "Microsoft.3DBuilder");
                    AddAppxIfMissing(rule, "Microsoft.Microsoft3DViewer");
                    AddAppxIfMissing(rule, "Microsoft.Print3D");
                    break;

                case "app_zune":
                    AddAppxIfMissing(rule, "Microsoft.ZuneMusic");
                    AddAppxIfMissing(rule, "Microsoft.ZuneVideo");
                    break;

                case "app_win11_misc":
                    AddAppxIfMissing(rule, "Microsoft.PCManager");
                    AddAppxIfMissing(rule, "Microsoft.Getstarted");
                    AddAppxIfMissing(rule, "Microsoft.WindowsFeedbackHub");
                    AddAppxIfMissing(rule, "Microsoft.BingSearch");
                    AddAppxIfMissing(rule, "MicrosoftCorporationII.MicrosoftFamily");
                    AddAppxIfMissing(rule, "MicrosoftCorporationII.QuickAssist");
                    AddAppxIfMissing(rule, "Microsoft.PowerAutomateDesktop");
                    AddAppxIfMissing(rule, "Microsoft.MicrosoftOfficeHub");
                    break;
            }
        }

        if (!ruleSet.Rules.Any(r => r.Id == "app_widgets"))
        {
            ruleSet.Rules.Add(new Rule
            {
                Id = "app_widgets",
                Category = "Apps",
                DisplayName = "Eliminar Widgets y Web Experience Pack",
                Description = "Desinstala el feed de noticias y widgets de la barra de tareas y el paquete Web Experience.",
                MinBuild = 22000,
                RequiresTrustedInstaller = false,
                Operations = new List<RuleOperation>
                {
                    new()
                    {
                        Type = "registry",
                        Hive = "HKCU",
                        Path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Name = "TaskbarDa",
                        Kind = "DWord",
                        Value = "0"
                    },
                    new() { Type = "appx", PackagePattern = "Microsoft.WidgetsPlatformRuntime" },
                    new() { Type = "appx", PackagePattern = "Microsoft.StartExperiencesApp" },
                    new() { Type = "appx", PackagePattern = "MicrosoftWindows.Client.WebExperience" }
                }
            });
        }

        if (!ruleSet.Rules.Any(r => r.Id == "app_phone"))
        {
            ruleSet.Rules.Add(new Rule
            {
                Id = "app_phone",
                Category = "Apps",
                DisplayName = "Eliminar Enlace Móvil (Phone Link) y CrossDevice",
                Description = "Desinstala Phone Link y el servicio CrossDevice de sincronización de smartphone.",
                MinBuild = 19045,
                RequiresTrustedInstaller = false,
                Operations = new List<RuleOperation>
                {
                    new() { Type = "appx", PackagePattern = "Microsoft.YourPhone" },
                    new() { Type = "appx", PackagePattern = "MicrosoftWindows.CrossDevice" }
                }
            });
        }

        string updatedJson = JsonSerializer.Serialize(ruleSet, DwsJsonContext.Default.RuleSet);
        File.WriteAllText(rulesPath, updatedJson);

        return Sign(rulesPath, privateKeyPath);
    }

    private static void AddAppxIfMissing(Rule rule, string pattern)
    {
        if (!rule.Operations.Any(o => o.Type == "appx" && o.PackagePattern == pattern))
        {
            rule.Operations.Add(new RuleOperation { Type = "appx", PackagePattern = pattern });
        }
    }

    private static void AddServiceIfMissing(Rule rule, string svcName)
    {
        if (!rule.Operations.Any(o => o.Type == "service" && o.ServiceName == svcName))
        {
            rule.Operations.Add(new RuleOperation { Type = "service", ServiceName = svcName, ServiceStartMode = "Disabled" });
        }
    }
}
