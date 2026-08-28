using System.Security.Cryptography;
using System.Text;

namespace ApiDocs.Domain;

/// <summary>
/// Stable identifier of a chunk across reindexations: hash of the file path and of the breadcrumb.
/// </summary>
internal readonly record struct ChunkId
{
    private readonly string? _value;

    private ChunkId(string value) => _value = value;

    public string Value => _value ?? string.Empty;

    public static ChunkId From(string filePath, Breadcrumb breadcrumb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var material = string.Concat(filePath.Replace('\\', '/'), "|", breadcrumb.ToString());
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        return new ChunkId(Convert.ToHexString(hash[..8]).ToLowerInvariant());
    }

    public override string ToString() => Value;
}
