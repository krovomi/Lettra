namespace Lettra.UI.Shared.Onboarding;

/// <summary>
/// État du panneau d'aide, partagé par ce qui l'ouvre : bouton « ? » de la barre du haut, « Aide » du menu, recherche
/// du menu sans résultat (la question tapée est reprise), palette Ctrl+K (une question fréquente s'y ouvre dépliée).
/// </summary>
public sealed class HelpPanelState
{
    public event EventHandler? Changed;

    public bool IsOpen { get; private set; }

    /// <summary>Question reprise à l'ouverture, consommée par le panneau.</summary>
    public string? PendingQuery { get; private set; }

    /// <summary>Question fréquente à montrer dépliée à l'ouverture.</summary>
    public string? PendingEntryId { get; private set; }

    public void Open(string? query = null, string? entryId = null)
    {
        IsOpen = true;
        PendingQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        PendingEntryId = entryId;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Le panneau a repris la question et la réponse à déplier.</summary>
    internal (string? Query, string? EntryId) TakePending()
    {
        (string? query, string? entryId) = (PendingQuery, PendingEntryId);
        (PendingQuery, PendingEntryId) = (null, null);
        return (query, entryId);
    }
}
