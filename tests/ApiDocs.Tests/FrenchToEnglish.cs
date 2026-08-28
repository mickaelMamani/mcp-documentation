using System.Globalization;
using System.Text;

namespace ApiDocs.Tests;

/// <summary>
/// The fixed FR → EN table of ARCHITECTURE §9. It simulates, deterministically, the reformulation
/// the client LLM is instructed to do before calling a tool: the server itself never translates.
/// Unmapped words are kept as they are, so English questions pass through untouched.
/// </summary>
internal static class FrenchToEnglish
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
    {
        // Entities
        ["folio"] = "folio",
        ["folios"] = "folios",
        ["portefeuille"] = "portfolio",
        ["portefeuilles"] = "portfolios",
        ["position"] = "position",
        ["positions"] = "positions",
        ["nappe"] = "surface",
        ["nappes"] = "surfaces",
        ["surface"] = "surface",
        ["volatilite"] = "volatility",
        ["grille"] = "grid",
        ["devise"] = "currency",
        ["identifiant"] = "id",
        ["jeton"] = "token",
        ["erreur"] = "error",
        ["erreurs"] = "errors",
        ["parametre"] = "parameter",
        ["parametres"] = "parameters",
        ["champ"] = "field",
        ["exemple"] = "example",
        ["appel"] = "call",
        ["desk"] = "desk",
        ["plateforme"] = "platform",
        ["scopes"] = "scopes",
        ["scope"] = "scope",
        ["format"] = "format",
        ["authentification"] = "authentication",
        ["pagination"] = "pagination",

        // Actions
        ["requeter"] = "query",
        ["rechercher"] = "search",
        ["recuperer"] = "retrieve",
        ["lire"] = "read",
        ["lister"] = "list",
        ["creer"] = "create",
        ["creation"] = "create",
        ["cloturer"] = "close",
        ["supprimer"] = "delete",
        ["annuler"] = "delete",
        ["appliquer"] = "apply",
        ["figer"] = "freeze",
        ["obtenir"] = "get",
        ["ecrire"] = "write",
        ["calibrer"] = "calibrate",
        ["recalibrer"] = "recalibrate",
        ["decaler"] = "shift",
        ["bumper"] = "bump",
        ["applique"] = "applied",

        // Function words the reformulation drops
        ["comment"] = string.Empty,
        ["quel"] = string.Empty,
        ["quelle"] = string.Empty,
        ["quels"] = string.Empty,
        ["quelles"] = string.Empty,
        ["est"] = string.Empty,
        ["le"] = string.Empty,
        ["la"] = string.Empty,
        ["les"] = string.Empty,
        ["un"] = string.Empty,
        ["une"] = string.Empty,
        ["des"] = string.Empty,
        ["du"] = string.Empty,
        ["de"] = string.Empty,
        ["d"] = string.Empty,
        ["l"] = string.Empty,
        ["pour"] = string.Empty,
        ["sur"] = string.Empty,
        ["avec"] = string.Empty,
        ["dans"] = string.Empty,
        ["et"] = string.Empty,
        ["ses"] = string.Empty,
        ["sa"] = string.Empty,
        ["son"] = string.Empty,
        ["a"] = string.Empty,
        ["en"] = string.Empty,
    };

    public static string Reformulate(string question)
    {
        var words = new List<string>(12);

        foreach (var raw in Split(question))
        {
            var key = Fold(raw);
            if (Table.TryGetValue(key, out var mapped))
            {
                if (mapped.Length > 0)
                {
                    words.Add(mapped);
                }

                continue;
            }

            words.Add(raw);
        }

        return string.Join(' ', words);
    }

    private static IEnumerable<string> Split(string question)
    {
        var start = -1;
        for (var i = 0; i <= question.Length; i++)
        {
            var isPart = i < question.Length && (char.IsLetterOrDigit(question[i]) || question[i] == '#');
            if (isPart)
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                yield return question[start..i];
                start = -1;
            }
        }
    }

    private static string Fold(string word)
    {
        var normalized = word.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
