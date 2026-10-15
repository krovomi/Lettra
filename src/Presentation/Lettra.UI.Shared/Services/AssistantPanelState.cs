using Lettra.Contracts.Assistant;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared.Services;

/// <summary>
/// État du panneau assistant, partagé par la mise en page et les écrans (bouton « Pourquoi ? » de la checklist).
/// Désactivé par défaut : sans gateway, sans droit ou sans réglage du cabinet, aucune fonction n'est offerte et rien
/// ne s'affiche. L'assistant propose ; le panneau n'a aucun bouton qui valide, émet ou envoie.
/// </summary>
public sealed class AssistantPanelState(IServiceProvider services)
{
    private readonly IAssistantGateway? _gateway = services.GetService<IAssistantGateway>();
    private Task? _loading;

    public event EventHandler? Changed;

    /// <summary>Fonctions ouvertes à l'utilisateur dans le dossier ouvert (vide tant que rien n'est chargé).</summary>
    public IReadOnlyList<AssistantFeature> Features { get; private set; } = [];

    public string? Engine { get; private set; }

    public bool IsAvailable => Features.Count > 0;

    public bool IsOpen { get; private set; }

    public AssistantFeature Feature { get; private set; } = AssistantFeature.RegulatoryQuestions;

    /// <summary>Document ouvert à l'écran : le seul que les outils de l'assistant peuvent lire.</summary>
    public Guid? DocumentId { get; private set; }

    /// <summary>Question à poser dès l'ouverture (bouton « Pourquoi ? »), consommée par le panneau.</summary>
    public string? PendingQuestion { get; private set; }

    internal IAssistantGateway? Gateway => _gateway;

    public bool Offers(AssistantFeature feature) => Features.Contains(feature);

    /// <summary>Charge les fonctions offertes une fois ; <paramref name="refresh"/> relit après un changement de dossier ou de réglage.</summary>
    public Task LoadAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (_gateway is null)
        {
            return Task.CompletedTask;
        }

        if (refresh || _loading is null || _loading.IsFaulted)
        {
            _loading = LoadCoreAsync(_gateway, cancellationToken);
        }

        return _loading;
    }

    public void Open(AssistantFeature feature, Guid? documentId = null, string? question = null)
    {
        Feature = feature;
        DocumentId = documentId;
        PendingQuestion = question;
        IsOpen = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        Open(Offers(Feature) ? Feature : Features.FirstOrDefault(AssistantFeature.RegulatoryQuestions), DocumentId);
    }

    public void Close()
    {
        IsOpen = false;
        PendingQuestion = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rend la question en attente une seule fois.</summary>
    internal string? TakePendingQuestion()
    {
        string? question = PendingQuestion;
        PendingQuestion = null;
        return question;
    }

    internal void SelectFeature(AssistantFeature feature) => Feature = feature;

    private async Task LoadCoreAsync(IAssistantGateway gateway, CancellationToken cancellationToken)
    {
        Contracts.Common.OperationResult<AssistantStatusResponse> status = await gateway.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        // Un refus (pas de droit IaUtiliser, pas de dossier, serveur injoignable) vaut « pas d'assistant ».
        Features = status.IsSuccess && status.Value is { } value ? value.Features : [];
        Engine = status.IsSuccess ? status.Value?.Engine : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
