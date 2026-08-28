namespace ApiDocs.Application.Ports;

/// <summary>Counts tokens the way the client model does, for the output budgets of ARCHITECTURE §4.5.</summary>
internal interface ITokenCounter
{
    int Count(string text);
}
