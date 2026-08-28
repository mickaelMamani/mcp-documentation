namespace ApiDocs.Domain;

/// <summary>Language of an <see cref="SectionKind.Example"/> chunk.</summary>
internal enum CodeLanguage
{
    None = 0,
    CSharp,
    Python,
}

internal static class CodeLanguageExtensions
{
    public const string CSharpId = "csharp";
    public const string PythonId = "python";

    public static string? ToId(this CodeLanguage language) => language switch
    {
        CodeLanguage.CSharp => CSharpId,
        CodeLanguage.Python => PythonId,
        _ => null,
    };

    public static bool TryParse(string? value, out CodeLanguage language)
    {
        language = CodeLanguage.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case CSharpId:
            case "c#":
            case "cs":
                language = CodeLanguage.CSharp;
                return true;
            case PythonId:
            case "py":
                language = CodeLanguage.Python;
                return true;
            default:
                return false;
        }
    }
}
