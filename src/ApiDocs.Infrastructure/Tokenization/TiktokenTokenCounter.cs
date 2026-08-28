using ApiDocs.Application.Ports;
using Microsoft.ML.Tokenizers;

namespace ApiDocs.Infrastructure.Tokenization;

/// <summary>
/// Counts tokens with the <c>cl100k_base</c> encoding (ARCHITECTURE §3), so that the output budgets
/// match what the client model actually pays for. The vocabulary is embedded in the
/// <c>Microsoft.ML.Tokenizers.Data.Cl100kBase</c> package: no network call at runtime.
/// </summary>
internal sealed class TiktokenTokenCounter : ITokenCounter
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    public int Count(string text) => string.IsNullOrEmpty(text) ? 0 : _tokenizer.CountTokens(text);
}
