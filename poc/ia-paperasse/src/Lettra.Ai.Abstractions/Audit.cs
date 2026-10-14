namespace Lettra.Ai.Abstractions;

public enum AiAuditKind { Request, EngineCall, ToolCall, ToolRejected, Draft, Answer, Refused, Unavailable }

/// <summary>Entrée du journal IA, stocké dans la base du dossier (chiffrement et rétention du dossier, annexe F §10).</summary>
public sealed record AiAuditEntry(
    DateTimeOffset At,
    Guid CabinetId,
    Guid DossierId,
    Guid UserId,
    AiAuditKind Kind,
    string Detail,
    string? Engine = null,
    string Origin = AiAuditOrigin.Assistant);

public interface IAiAuditLog
{
    Task AppendAsync(AiAuditEntry entry, CancellationToken ct = default);
}

/// <summary>Accès au contexte du dossier pour l'assistant.</summary>
public interface IDossierContextProvider
{
    /// <summary>company.json virtuel, généré depuis les paramètres du dossier (annexe B §3.2).</summary>
    Task<string> GetCompanyJsonAsync(Guid dossierId, CancellationToken ct = default);

    /// <summary>Identités connues du dossier à pseudonymiser avant tout envoi (émetteur, clients, IBAN…).</summary>
    Task<IReadOnlyList<KnownIdentity>> GetKnownIdentitiesAsync(Guid dossierId, CancellationToken ct = default);
}

public enum IdentityKind { Company, Client, Person, Siren, Siret, VatNumber, Iban, Email, Address, Phone }

public sealed record KnownIdentity(IdentityKind Kind, string Value);
