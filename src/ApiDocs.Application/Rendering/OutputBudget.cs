using System.Text;
using ApiDocs.Application.Ports;
using ApiDocs.Domain;

namespace ApiDocs.Application.Rendering;

/// <summary>One candidate block of the answer, kept whole or dropped (ARCHITECTURE §4.5).</summary>
internal readonly record struct OutputBlock(string Text, string Label, string? OperationId);

/// <summary>
/// Assembles an answer under a token budget: whole blocks only, in the order given, then a footer
/// naming what did not fit. A truncated code block is worse than a missing one.
/// </summary>
internal sealed class OutputBudget(ITokenCounter tokenCounter, TokenBudget budget, string? separator = null)
{
    private const string DefaultSeparator = "\n\n";

    private readonly StringBuilder _builder = new();
    private readonly List<OutputBlock> _skipped = [];
    private readonly string _separator = separator ?? DefaultSeparator;
    private int _used;

    public int UsedTokens => _used;

    public int SkippedCount => _skipped.Count;

    /// <summary>Adds a block that is always kept and always counted (headers, footers).</summary>
    public void AddMandatory(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Append(text);
        _used += tokenCounter.Count(text);
    }

    /// <summary>Adds a block if it fits whole; records it as skipped otherwise.</summary>
    public bool TryAdd(in OutputBlock block)
    {
        var cost = tokenCounter.Count(block.Text);
        if (_used + cost > budget.Tokens && _builder.Length > 0)
        {
            _skipped.Add(block);
            return false;
        }

        Append(block.Text);
        _used += cost;
        return true;
    }

    /// <summary>ARCHITECTURE §4.5 footer: what was left out and how to get it.</summary>
    public void AppendOverflowFooter()
    {
        if (_skipped.Count == 0)
        {
            return;
        }

        var labels = new string[_skipped.Count];
        var operations = new List<string>(_skipped.Count);
        for (var i = 0; i < _skipped.Count; i++)
        {
            labels[i] = _skipped[i].Label;
            var operationId = _skipped[i].OperationId;
            if (operationId is not null && !operations.Contains(operationId, StringComparer.Ordinal))
            {
                operations.Add(operationId);
            }
        }

        var footer = new StringBuilder();
        footer.Append(_skipped.Count)
            .Append(" more relevant sections not included: ")
            .Append(string.Join(", ", labels))
            .Append('.');

        if (operations.Count > 0)
        {
            footer.Append(" Refine `query` or call get_endpoint with one of: ")
                .Append(string.Join(", ", operations))
                .Append('.');
        }
        else
        {
            footer.Append(" Refine `query`.");
        }

        AddMandatory(footer.ToString());
    }

    private void Append(string text)
    {
        if (_builder.Length > 0)
        {
            _builder.Append(_separator);
        }

        _builder.Append(text.TrimEnd());
    }

    public override string ToString() => _builder.ToString();
}
