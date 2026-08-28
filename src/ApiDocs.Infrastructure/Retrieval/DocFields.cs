namespace ApiDocs.Infrastructure.Retrieval;

/// <summary>Lucene field names and their query boosts (ARCHITECTURE §4.1).</summary>
internal static class DocFields
{
    public const string Id = "id";
    public const string Ordinal = "ord";
    public const string Kind = "kind";
    public const string OperationId = "operationId";
    public const string Domain = "domain";
    public const string Language = "language";
    public const string Deprecated = "deprecated";

    public const string Title = "title";
    public const string Breadcrumb = "breadcrumb";
    public const string Summary = "summary";
    public const string Keywords = "keywords";
    public const string Body = "body";

    public const float TitleBoost = 4f;
    public const float BreadcrumbBoost = 2f;
    public const float SummaryBoost = 3f;
    public const float KeywordsBoost = 3f;
    public const float BodyBoost = 1f;

    /// <summary>An exact operationId in the query means the caller named the endpoint.</summary>
    public const float ExactOperationIdBoost = 10f;

    /// <summary>Fields that are not stemmed, where a prefix or a fuzzy term still makes sense.</summary>
    public static readonly string[] LiteralFields = [Title, Breadcrumb, Keywords];

    public static readonly (string Field, float Boost)[] SearchableFields =
    [
        (Title, TitleBoost),
        (Breadcrumb, BreadcrumbBoost),
        (Summary, SummaryBoost),
        (Keywords, KeywordsBoost),
        (Body, BodyBoost),
    ];
}
