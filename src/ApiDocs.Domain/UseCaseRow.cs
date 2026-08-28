namespace ApiDocs.Domain;

/// <summary>
/// One row of the <c>## Use cases</c> table of a domain file (DOC-FORMAT §4.2).
/// Parsed at ingestion so that <c>search_endpoints</c> can convert a matched intent into its
/// target endpoints without re-parsing Markdown per request (ARCHITECTURE §5.2).
/// </summary>
internal sealed record UseCaseRow(string Intent, OperationId[] Operations, string Notes);
