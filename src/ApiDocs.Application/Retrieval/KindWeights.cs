using ApiDocs.Domain;

namespace ApiDocs.Application.Retrieval;

/// <summary>
/// Per-document boosts of ARCHITECTURE §4.2 step 4, applied to the BM25 score after retrieval so
/// that a tool can weight kinds differently without rebuilding the index.
/// </summary>
internal static class KindWeights
{
    public const double IntentBearing = 1.5;
    public const double ExampleInFreeSearch = 0.8;

    public static double ForEndpointSearch(SectionKind kind) => kind switch
    {
        SectionKind.DomainUseCases or SectionKind.Description => IntentBearing,
        _ => 1d,
    };

    public static double ForDocsSearch(SectionKind kind) => kind switch
    {
        SectionKind.DomainUseCases or SectionKind.Description => IntentBearing,
        SectionKind.Example => ExampleInFreeSearch,
        _ => 1d,
    };
}
