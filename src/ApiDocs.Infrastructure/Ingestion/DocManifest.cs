using System.Text.Json.Serialization;

namespace ApiDocs.Infrastructure.Ingestion;

/// <summary>DOC-FORMAT §3, <c>manifest.json</c>. Source of truth for the endpoint catalogue.</summary>
internal sealed class DocManifest
{
    [JsonPropertyName("formatVersion")]
    public string? FormatVersion { get; init; }

    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    [JsonPropertyName("platformVersion")]
    public string? PlatformVersion { get; init; }

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset? GeneratedAt { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("domains")]
    public List<ManifestDomain> Domains { get; init; } = [];
}

internal sealed class ManifestDomain
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("file")]
    public string? File { get; init; }

    [JsonPropertyName("endpoints")]
    public List<ManifestEndpoint> Endpoints { get; init; } = [];
}

internal sealed class ManifestEndpoint
{
    [JsonPropertyName("operationId")]
    public string? OperationId { get; init; }

    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("route")]
    public string? Route { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("file")]
    public string? File { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    [JsonPropertyName("deprecated")]
    public bool Deprecated { get; init; }
}
