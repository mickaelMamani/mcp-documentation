namespace ApiDocs.Domain;

/// <summary>Header of <c>manifest.json</c> (DOC-FORMAT §3), surfaced by <c>list_domains</c>.</summary>
internal sealed record PlatformInfo
{
    public required string FormatVersion { get; init; }

    public required string Platform { get; init; }

    public required string PlatformVersion { get; init; }

    public required string Language { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    /// <summary>Full raw Markdown of <c>_platform.md</c>, front matter excluded. Served by <c>doc://_platform</c>.</summary>
    public string RawBody { get; init; } = string.Empty;

    public string Display =>
        GeneratedAt is { } generated
            ? $"{Platform} v{PlatformVersion} (docs generated {generated:yyyy-MM-dd})"
            : $"{Platform} v{PlatformVersion}";
}
