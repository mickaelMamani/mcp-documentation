using ApiDocs.Application.Ports;
using ApiDocs.Application.Rendering;
using ApiDocs.Domain;

namespace ApiDocs.Application.UseCases;

/// <summary>ARCHITECTURE §5.2, <c>list_domains</c>.</summary>
internal sealed class ListDomainsUseCase(IIndexSnapshotProvider snapshots, ITokenCounter tokenCounter)
{
    public string Execute()
    {
        var snapshot = snapshots.Current;
        var budget = new OutputBudget(tokenCounter, TokenBudget.ListDomains, separator: "\n");

        budget.AddMandatory($"Platform: {snapshot.Platform.Display}");

        var domains = snapshot.Domains;
        for (var i = 0; i < domains.Length; i++)
        {
            var domain = domains[i];
            var count = snapshot.EndpointCountOf(domain.Id);
            var line = $"- {domain.Id} — {domain.Summary} ({count} endpoint{(count == 1 ? string.Empty : "s")})";
            budget.TryAdd(new OutputBlock(line, domain.Id, null));
        }

        budget.AppendOverflowFooter();
        return budget.ToString();
    }
}
