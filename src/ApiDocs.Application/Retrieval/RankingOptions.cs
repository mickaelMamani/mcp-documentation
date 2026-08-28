namespace ApiDocs.Application.Retrieval;

/// <summary>
/// Ranking policy applied on top of the BM25 score (ARCHITECTURE §4.2 step 4). Kept as data rather
/// than constants so that ADR #7 holds in practice: a boost is prompt code, and prompt code is
/// swept against the benchmark instead of being argued about.
///
/// Defaults come from the sweep of ADR #20, chosen as the least distorting configuration that still
/// reaches the measured ceiling. Do not change one by hand without re-running
/// <c>RetrievalTuningSweepTests</c>.
/// </summary>
internal sealed record RankingOptions
{
    public static RankingOptions Default { get; } = new();

    /// <summary>
    /// Weight of the share of distinct query tokens an endpoint actually accounts for.
    /// BM25 scores each term independently, so one rare term carried by a short field can outrank a
    /// candidate that answers the whole query; this term restores the coordination BM25 drops.
    /// 0 disables it.
    /// </summary>
    public double CoverageWeight { get; init; } = 2d;

    /// <summary>
    /// Weight of the affinity between the query and the identity of an endpoint's domain (id, name
    /// and domain keywords). Keeps a generic verb from dragging in an endpoint of the other domain.
    /// 0 disables it.
    /// </summary>
    public double DomainAffinityWeight { get; init; } = 2d;

    public override string ToString() =>
        $"coverage={CoverageWeight:0.##} affinity={DomainAffinityWeight:0.##}";
}
