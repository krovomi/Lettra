namespace Lettra.Supervision.Infrastructure;

/// <summary>Réglages du module (section <c>Lettra:Supervision</c>).</summary>
public sealed class SupervisionOptions
{
    public const string SectionName = "Lettra:Supervision";

    /// <summary>
    /// Jeton partagé avec l'Alertmanager de la cellule (secret Kubernetes). Sans jeton, le webhook n'est pas exposé.
    /// </summary>
    public string? WebhookToken { get; set; }

    /// <summary>Adresse publique du centre d'alertes, pour les liens des notifications.</summary>
    public Uri PublicBaseAddress { get; set; } = new("https://localhost/");

    /// <summary>Rotation d'astreinte par niveau d'escalade (identifiants de l'outil d'astreinte, pas des noms).</summary>
    public IList<string> OnCallLevels { get; } = ["astreinte-n1", "astreinte-n2", "responsable-technique"];
}
