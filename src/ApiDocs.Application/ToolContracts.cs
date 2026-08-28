namespace ApiDocs.Application;

/// <summary>
/// The MCP surface contract of ARCHITECTURE §5, copied verbatim. These strings are prompt code:
/// changing one means updating the design document and re-running the retrieval benchmark.
/// </summary>
internal static class ToolContracts
{
    public const string ListDomainsName = "list_domains";
    public const string SearchEndpointsName = "search_endpoints";
    public const string GetEndpointName = "get_endpoint";
    public const string SearchDocsName = "search_docs";

    public const string ListDomains =
        "List the API domains with their summary, endpoint count and the platform version. " +
        "Call once at the start of a session when you don't know which domain a question belongs to. " +
        "Cheap; result rarely changes.";

    public const string SearchEndpoints =
        "Find candidate endpoints for a task. Call this before get_endpoint. Several endpoints may " +
        "match one intent (e.g. search-by-criteria vs get-by-id): present them or ask the user which " +
        "one. Returns operationId, method, route and a one-line summary. Do not call more than 2 times " +
        "per question; refine the query instead.";

    public const string GetEndpoint =
        "Get the full documentation of one endpoint: description, parameters, response, and a code " +
        "example. Requires the exact operationId from search_endpoints. Pass the user's question as " +
        "query so the most relevant sections are returned first; pass language to get only the C# or " +
        "Python example.";

    public const string SearchDocs =
        "Full-text search across all documentation sections (domain overviews, workflows, " +
        "authentication, errors, endpoint descriptions). Use for cross-cutting questions (auth, " +
        "pagination, conventions, \"how do I…\") when no single endpoint is the answer. Returns ranked " +
        "sections with their breadcrumb.";

    public const string QueryParameter =
        "English search terms using API vocabulary: entity, action, parameter names. Translate the " +
        "user's request; never pass it verbatim. Good: \"folio search by criteria\", \"volatility " +
        "surface plug shift\". Bad: \"comment requêter les folios\", \"help\".";

    public const string DomainParameter = "Restrict to one domain id from list_domains.";

    public const string LimitParameter = "Maximum number of results.";

    public const string OperationIdParameter = "Exact operationId, case-sensitive, e.g. \"GetFolioById\".";

    public const string GetEndpointQueryParameter =
        "The user's question in English; used to rank sections. Omit to get the standard layout.";

    public const string LanguageParameter =
        "Return only this language's example. Omit for both.";

    /// <summary>ARCHITECTURE §5.1. <c>{0}</c> is the platform name, <c>{1}</c> the platform version.</summary>
    private const string ServerInstructionsTemplate =
        """
        This server exposes the internal documentation of the {0} API (version {1}).
        Documentation is in English. Users may ask in French: translate their intent into English API
        vocabulary before calling any tool (entity, action, parameter names).
        Workflow: (1) list_domains if you don't know which domain applies; (2) search_endpoints to find
        candidate operationIds — several may match one intent, present them or ask; (3) get_endpoint with
        the exact operationId to get parameters, response and a code example.
        Always cite the operationId, HTTP method and route in your answer. Never invent endpoints or
        parameters that are not in the returned documentation.
        """;

    public static string ServerInstructions(string platform, string platformVersion)
    {
        // "Federer API" would render as "the Federer API API": the template already carries the noun.
        var name = platform.EndsWith(" API", StringComparison.OrdinalIgnoreCase)
            ? platform[..^4]
            : platform;

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, ServerInstructionsTemplate, name, platformVersion);
    }
}
