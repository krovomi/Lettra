using System.Text.Json.Serialization;

namespace Lettra.Contracts.Speech;

/// <summary>Ce que l'utilisateur fait de sa voix : dicter un texte dans un champ, ou donner un ordre à l'application.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpeechMode>))]
public enum SpeechMode
{
    /// <summary>Dictée : le texte, ponctuation dictée et montants mis en forme, va dans le champ qui a le focus.</summary>
    Dictation = 1,

    /// <summary>Commande vocale : la phrase est aussi lue par la grammaire déterministe (<see cref="VoiceCommand"/>).</summary>
    Command = 2,
}

/// <summary>
/// La reconnaissance vocale sur ce déploiement. Elle est désactivée par défaut ; quand elle est active, l'audio ne
/// quitte jamais le poste (MAUI) ou la cellule (navigateur) : aucun service vocal tiers (ADR-0011, ADR-0012).
/// </summary>
/// <param name="Enabled">Faux tant que le déploiement n'a pas choisi de moteur.</param>
/// <param name="Engine">Nom technique du moteur (<c>whisper-serveur</c>, <c>whisper-poste</c>), ou <c>null</c>.</param>
/// <param name="OnDevice">Vrai si la transcription se fait sur l'appareil, sans réseau.</param>
/// <param name="Languages">Langues acceptées (codes ISO 639-1), la première par défaut.</param>
/// <param name="MaxSeconds">Durée maximale d'un enregistrement.</param>
public sealed record SpeechStatusResponse(bool Enabled, string? Engine, bool OnDevice, IReadOnlyList<string> Languages, int MaxSeconds)
{
    /// <summary>Format attendu : WAV PCM 16 bits, mono, 16 kHz (celui de Whisper).</summary>
    public const int SampleRate = 16_000;

    public static SpeechStatusResponse Disabled { get; } = new(false, null, false, [], 0);
}

/// <summary>
/// Résultat d'une transcription. <see cref="Text"/> est le texte mis en forme pour un champ ; la transcription brute
/// du moteur n'est ni renvoyée ni conservée.
/// </summary>
/// <param name="Text">Texte dicté, ponctuation et montants mis en forme (« cent vingt euros » → « 120,00 € »).</param>
/// <param name="Language">Langue de la transcription.</param>
/// <param name="DurationSeconds">Durée de l'audio reçu.</param>
/// <param name="Command">En mode commande : la commande reconnue, ou <c>null</c> si la phrase n'en est pas une.</param>
public sealed record TranscriptionResponse(string Text, string Language, double DurationSeconds, VoiceCommand? Command);

/// <summary>Les commandes que la voix sait donner. Aucune n'agit seule : l'écran les affiche avant de les exécuter.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VoiceCommandKind>))]
public enum VoiceCommandKind
{
    /// <summary>« ouvrir les factures en retard » : <see cref="VoiceCommand.Target"/> et <see cref="VoiceCommand.Filter"/>.</summary>
    Navigate = 1,

    /// <summary>« nouveau devis », « nouveau client » : écran de création de <see cref="VoiceCommand.Target"/>.</summary>
    Create = 2,

    /// <summary>« chercher le client Dupont » : <see cref="VoiceCommand.Term"/> dans <see cref="VoiceCommand.Target"/> (ou partout).</summary>
    Search = 3,

    /// <summary>« ajouter trois heures de conseil à cent vingt euros » : une ligne dans la pièce ouverte.</summary>
    AddLine = 4,

    /// <summary>« remise de dix pour cent » sur la pièce ouverte.</summary>
    ApplyDiscount = 5,

    /// <summary>« devis pour Dupont, trois heures de conseil à 120 € HT » : un <b>brouillon</b> de devis à relire.</summary>
    DraftQuote = 6,

    /// <summary>« valider » : irréversible pour une facture, donc toujours confirmé au clavier ou au clic.</summary>
    Validate = 7,

    /// <summary>« enregistrer » le formulaire ouvert.</summary>
    Save = 8,

    /// <summary>« annuler » : quitte le formulaire ou la saisie en cours sans rien enregistrer.</summary>
    Cancel = 9,

    /// <summary>« aide » : ce que la voix sait faire sur l'écran ouvert.</summary>
    Help = 10,
}

/// <summary>Écrans et entités que la voix sait nommer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VoiceTarget>))]
public enum VoiceTarget
{
    Home = 1,
    Quotes = 2,
    Invoices = 3,
    CreditNotes = 4,
    Customers = 5,
    Suppliers = 6,
    Catalog = 7,
    Fec = 8,
    Alerts = 9,
    Sync = 10,
    Settings = 11,
    Assistant = 12,
}

/// <summary>Filtres d'une liste, dits avec elle (« les devis acceptés », « les factures en retard »).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VoiceFilter>))]
public enum VoiceFilter
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Refused = 4,
    Expired = 5,
    Unpaid = 6,
    Overdue = 7,
    Paid = 8,
}

/// <summary>Une ligne de pièce dite à voix haute. Les montants sont en euros, à deux décimales au plus.</summary>
/// <param name="Quantity">Quantité (« trois », « 1,5 », « une heure et demie »).</param>
/// <param name="Unit">Code d'unité CEE-ONU (rec. 20) reconnu : <c>HUR</c> heure, <c>DAY</c> jour, <c>C62</c> unité, <c>LS</c> forfait… ; <c>null</c> si rien n'est dit.</param>
/// <param name="Designation">Désignation, telle que dite (« conseil », « pose de carrelage »).</param>
/// <param name="UnitPrice">Prix unitaire, ou <c>null</c> s'il n'est pas dit (le catalogue le fournira).</param>
/// <param name="PriceIncludesVat"><c>true</c> pour « TTC », <c>false</c> pour « HT », <c>null</c> si rien n'est dit.</param>
public sealed record VoiceLine(decimal Quantity, string? Unit, string Designation, decimal? UnitPrice, bool? PriceIncludesVat);

/// <summary>
/// Commande reconnue par la grammaire déterministe (aucun modèle de langage). L'écran l'affiche avant de l'exécuter ;
/// <see cref="RequiresConfirmation"/> impose en plus une confirmation au clavier ou au clic.
/// </summary>
public sealed record VoiceCommand
{
    public required VoiceCommandKind Kind { get; init; }

    /// <summary>La phrase reconnue, telle qu'elle sera affichée pour relecture.</summary>
    public required string Phrase { get; init; }

    public VoiceTarget? Target { get; init; }

    public VoiceFilter? Filter { get; init; }

    /// <summary>Texte cherché (<see cref="VoiceCommandKind.Search"/>).</summary>
    public string? Term { get; init; }

    /// <summary>Client nommé (« devis pour Dupont »), à retrouver parmi les clients du dossier.</summary>
    public string? Customer { get; init; }

    public IReadOnlyList<VoiceLine> Lines { get; init; } = [];

    /// <summary>Remise en pourcentage (« remise de 10 % »).</summary>
    public decimal? DiscountPercent { get; init; }

    public bool RequiresConfirmation { get; init; }
}
