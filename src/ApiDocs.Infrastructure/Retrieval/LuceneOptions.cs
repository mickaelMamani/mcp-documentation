namespace ApiDocs.Infrastructure.Retrieval;

/// <summary>
/// Index level knobs of ARCHITECTURE §4.2. Data rather than constants so the retrieval sweep can
/// measure them against the benchmark instead of relying on judgement (ADR #7).
/// </summary>
internal sealed record LuceneOptions
{
    public static LuceneOptions Default { get; } = new();

    /// <summary>
    /// Strips the plural mark on the non stemmed chains (<c>Title</c>, <c>Breadcrumb</c>,
    /// <c>Keywords</c>). Without it the query token "folios" cannot reach the "folio" token of the
    /// ×4 <c>Title</c> field at all. English and French share the -s plural, so this also helps the
    /// bilingual keyword field.
    /// </summary>
    public bool FoldPlurals { get; init; } = true;

    /// <summary>BM25 term frequency saturation. Lucene default 1.2.</summary>
    public float Bm25K1 { get; init; } = 1.2f;

    /// <summary>
    /// BM25 length normalisation. Lucene default 0.75. Summaries are all about one sentence long,
    /// so a high value mostly rewards whoever wrote the tersest summary rather than the best match.
    /// </summary>
    public float Bm25B { get; init; } = 0.75f;

    public override string ToString() =>
        $"plurals={(FoldPlurals ? "on" : "off")} k1={Bm25K1:0.##} b={Bm25B:0.##}";
}
