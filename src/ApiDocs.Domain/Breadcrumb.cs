namespace ApiDocs.Domain;

/// <summary>
/// Human readable position of a chunk: <c>Domain › OperationId › Section</c>.
/// Given back to the LLM and indexed with a boost (ARCHITECTURE §4.1).
/// </summary>
internal readonly record struct Breadcrumb(string Scope, string? Operation, string Section)
{
    public const string Separator = " › ";

    public static Breadcrumb ForEndpoint(string domainName, string operationId, string section) =>
        new(domainName, operationId, section);

    public static Breadcrumb ForDomain(string domainName, string section) =>
        new(domainName, null, section);

    public static Breadcrumb ForPlatform(string platformName, string section) =>
        new(platformName, null, section);

    public override string ToString() =>
        Operation is null
            ? string.Concat(Scope, Separator, Section)
            : string.Concat(Scope, Separator, Operation, Separator, Section);
}
