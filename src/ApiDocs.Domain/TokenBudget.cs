namespace ApiDocs.Domain;

/// <summary>Maximum number of tokens a tool may return (ARCHITECTURE §4.5).</summary>
internal readonly record struct TokenBudget
{
    private TokenBudget(int tokens) => Tokens = tokens;

    public int Tokens { get; }

    public static TokenBudget Of(int tokens) =>
        tokens > 0
            ? new TokenBudget(tokens)
            : throw new ArgumentOutOfRangeException(nameof(tokens), tokens, "A token budget must be strictly positive.");

    /// <summary>Default budgets of ARCHITECTURE §4.5.</summary>
    public static TokenBudget ListDomains { get; } = new(600);

    public static TokenBudget SearchEndpoints { get; } = new(400);

    public static TokenBudget GetEndpoint { get; } = new(2500);

    public static TokenBudget SearchDocs { get; } = new(1500);
}
