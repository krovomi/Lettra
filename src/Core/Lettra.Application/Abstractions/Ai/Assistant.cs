using Lettra.Contracts.Assistant;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Ai;

/// <summary>
/// L'assistant comptable (annexe B §4.2). Appelé uniquement par <c>AskAssistantQuery</c>, après le pipeline
/// (dossier, permission <c>IaUtiliser</c>, validation) ; il revérifie la politique du cabinet par défense en profondeur.
/// </summary>
public interface IAccountingAssistant
{
    IAsyncEnumerable<AssistantEvent> AskAsync(AssistantRequest request, CancellationToken cancellationToken);
}

/// <summary><c>Permissions</c> : Permissions effectives de l'utilisateur sur le dossier : elles filtrent les outils proposés. <c>OpenDocumentId</c> : Document ouvert à l'écran : seul ce document peut être lu (minimisation). <c>ImportedContent</c> : Contenu importé (e-mail, OCR, pièce) : toujours traité comme des données hostiles.</summary>
public sealed record AssistantRequest(
    OrganisationId Organisation,
    DossierId Dossier,
    Guid UserId,
    IReadOnlySet<Identity.Permission> Permissions,
    AssistantFeature Feature,
    string Question,
    AiSkill Skill = AiSkill.Comptable,
    Guid? OpenDocumentId = null,
    string? ImportedContent = null);

public enum AiAuditKind
{
    Request = 1,
    EngineCall = 2,
    ToolCall = 3,
    ToolRejected = 4,
    Draft = 5,
    Answer = 6,
    Refused = 7,
    Unavailable = 8,
}

/// <summary>
/// Entrée du journal IA. Elle appartient au dossier (chiffrement et rétention du dossier, annexe F §10) et ne contient
/// que des textes pseudonymisés : les vraies valeurs ne sont rétablies qu'à l'affichage de la réponse.
/// </summary>
public sealed record AiAuditEntry(
    DateTimeOffset At,
    DossierId Dossier,
    Guid UserId,
    AiAuditKind Kind,
    string Detail,
    string? Engine = null);

public interface IAiAuditLog
{
    ValueTask AppendAsync(AiAuditEntry entry, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AiAuditEntry>> ReadAsync(DossierId dossier, int limit, CancellationToken cancellationToken);
}

/// <summary>Contexte du dossier fourni à l'assistant.</summary>
public interface IAiDossierContext
{
    /// <summary>company.json virtuel, généré depuis les paramètres du dossier (annexe B §3.2).</summary>
    Task<string> GetCompanyJsonAsync(DossierId dossier, CancellationToken cancellationToken);

    /// <summary>Identités connues du dossier à pseudonymiser avant tout envoi (clients, SIREN, IBAN, e-mails…).</summary>
    Task<IReadOnlyList<KnownIdentity>> GetKnownIdentitiesAsync(DossierId dossier, CancellationToken cancellationToken);
}

public enum IdentityKind
{
    Company = 1,
    Client = 2,
    Person = 3,
    Siren = 4,
    Siret = 5,
    VatNumber = 6,
    Iban = 7,
    Email = 8,
    Address = 9,
    Phone = 10,
}

public sealed record KnownIdentity(IdentityKind Kind, string Value);
