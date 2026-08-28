namespace ApiDocs.Domain;

/// <summary>
/// Identifier of an API operation, exactly as written in the manifest and in the front matter.
/// Comparison is ordinal and case-sensitive: the MCP contract advertises a case-sensitive operationId.
/// </summary>
internal readonly record struct OperationId
{
    private readonly string? _value;

    private OperationId(string value) => _value = value;

    public string Value => _value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static OperationId Create(string value) =>
        TryCreate(value, out var id)
            ? id
            : throw new ArgumentException("An operationId must be a non-blank token without whitespace.", nameof(value));

    public static bool TryCreate(string? value, out OperationId operationId)
    {
        operationId = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        operationId = new OperationId(trimmed);
        return true;
    }

    public override string ToString() => Value;
}
