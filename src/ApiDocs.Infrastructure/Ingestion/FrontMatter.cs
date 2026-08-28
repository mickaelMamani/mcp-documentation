using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ApiDocs.Infrastructure.Ingestion;

/// <summary>Front matter of an endpoint, domain or platform file (DOC-FORMAT §4).</summary>
internal sealed class FrontMatter
{
    public string? FormatVersion { get; set; }

    public string? Domain { get; set; }

    public string? OperationId { get; set; }

    public string? Method { get; set; }

    public string? Route { get; set; }

    public string? Version { get; set; }

    public string? Summary { get; set; }

    public string? Keywords { get; set; }

    public string? Platform { get; set; }

    public string? PlatformVersion { get; set; }

    public List<string> Tags { get; set; } = [];

    public List<string> Aliases { get; set; } = [];

    public List<string> Related { get; set; } = [];

    public List<string> Endpoints { get; set; } = [];

    public bool Deprecated { get; set; }
}

/// <summary>Splits a Markdown file into its YAML front matter and its body.</summary>
internal static class FrontMatterReader
{
    private const string Fence = "---";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Returns <see langword="false"/> when the file does not start with a closed <c>---</c> block:
    /// DOC-FORMAT §7 makes the front matter mandatory.
    /// </summary>
    public static bool TrySplit(string text, out string yaml, out string body, out int bodyStartLine)
    {
        yaml = string.Empty;
        body = text;
        bodyStartLine = 1;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith(Fence + "\n", StringComparison.Ordinal))
        {
            return false;
        }

        var end = normalized.IndexOf("\n" + Fence, Fence.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        yaml = normalized[(Fence.Length + 1)..(end + 1)];

        var afterFence = end + 1 + Fence.Length;
        body = afterFence < normalized.Length ? normalized[afterFence..].TrimStart('\n') : string.Empty;

        var consumed = normalized.Length - body.Length;
        bodyStartLine = 1;
        for (var i = 0; i < consumed && i < normalized.Length; i++)
        {
            if (normalized[i] == '\n')
            {
                bodyStartLine++;
            }
        }

        return true;
    }

    public static FrontMatter Parse(string yaml) =>
        Deserializer.Deserialize<FrontMatter>(yaml) ?? new FrontMatter();
}
