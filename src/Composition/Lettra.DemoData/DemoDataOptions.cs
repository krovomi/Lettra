namespace Lettra.DemoData;

/// <summary>Réglages du jeu de démonstration (section <c>Lettra:Demo</c>).</summary>
public sealed class DemoDataOptions
{
    public const string SectionName = "Lettra:Demo";

    /// <summary>Charger le jeu au démarrage, si le dossier ouvert n'a encore aucune pièce.</summary>
    public bool Seed { get; set; }

    /// <summary>Utilisateur au nom duquel les pièces sont créées (serveur : le <c>sub</c> de l'utilisateur de démonstration).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Dossier à remplir ; par défaut le dossier ouvert (poste) ou le premier dossier accessible (serveur).</summary>
    public Guid? DossierId { get; set; }

    /// <summary>Graine du tirage : même graine et même jour, mêmes données.</summary>
    public int RandomSeed { get; set; } = 2026;

    /// <summary>Profondeur de l'historique, en mois avant aujourd'hui.</summary>
    public int Months { get; set; } = 12;

    /// <summary>Dossiers créés en plus du dossier ouvert (bâtiment, puis négoce), de 0 à 2.</summary>
    public int AdditionalDossiers { get; set; } = 2;
}
