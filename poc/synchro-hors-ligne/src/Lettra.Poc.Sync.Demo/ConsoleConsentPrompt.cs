using Lettra.Poc.Sync.Client;

namespace Lettra.Poc.Sync.Demo;

/// <summary>Stand-in console du dialogue Blazor « Synchroniser maintenant / Plus tard / Voir le détail ».</summary>
public sealed class ConsoleConsentPrompt(string deviceName, bool auto) : ISyncConsentPrompt
{
    private bool _detailShown;

    public Task<SyncConsent> AskAsync(SyncSummary summary, NetworkKind network, CancellationToken ct)
    {
        Console.WriteLine($"""
              ┌ {deviceName} — réseau revenu ({network})
              │ À envoyer : {summary.Describe()} (~{summary.EstimatedBytes / 1024.0:N1} Ko)
              └ [M] Synchroniser maintenant   [P] Plus tard   [D] Voir le détail
            """);
        Console.Write("    Votre choix : ");
        string? key;
        if (auto)
        {
            key = _detailShown ? "M" : "D";   // scénario : voir le détail, puis synchroniser
            _detailShown = !_detailShown;
            Console.WriteLine(key);
        }
        else key = Console.ReadLine()?.Trim().ToUpperInvariant();

        return Task.FromResult(key switch { "P" => SyncConsent.Later, "D" => SyncConsent.ShowDetail, _ => SyncConsent.Now });
    }

    public Task ShowDetailAsync(IReadOnlyList<OutboxItemView> items, CancellationToken ct)
    {
        foreach (var item in items) Console.WriteLine($"      {item.Sequence,3}. {item.Description}");
        return Task.CompletedTask;
    }
}
