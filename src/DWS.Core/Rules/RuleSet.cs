namespace DWS.Core.Rules;

public class RuleSet
{
    public string SchemaVersion { get; set; } = "2.0.0";
    public List<Rule> Rules { get; set; } = new();
    public string? Signature { get; set; }
}
