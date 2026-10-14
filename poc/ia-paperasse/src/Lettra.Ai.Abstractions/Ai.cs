namespace Lettra.Ai.Abstractions;

/// <summary>Skills Paperasse embarqués (annexe B §1). Les autres skills (fiscaliste, notaire, syndic) ne sont pas embarqués.</summary>
public enum AiSkill { Comptable, ControleurFiscal, CommissaireAuxComptes }

/// <summary>
/// Pouvoir d'un outil exposé au modèle. Il n'existe volontairement aucune valeur « Valide », « Émet » ou « Supprime » :
/// un outil qui le ferait ne peut pas être déclaré (ADR-0006, ADR-0012).
/// </summary>
public enum AiToolRisk { ReadOnly, CreatesDraft }

/// <summary>Points d'IA activables un par un par le cabinet (annexe D, points numérotés).</summary>
public enum AiFeature
{
    /// <summary>Point 1 : saisie en langage naturel → brouillon de devis.</summary>
    DraftFromText = 1,
    /// <summary>Point 3 : « Pourquoi ? » sur une erreur de conformité.</summary>
    ExplainCompliance = 3,
    /// <summary>Point 4 : diagnostic e-facturation du dossier.</summary>
    EInvoicingDiagnosis = 4,
    /// <summary>Point 6 : questions TVA et réglementaires.</summary>
    RegulatoryQuestions = 6,
}

/// <summary>Moteur choisi par le cabinet (ADR-0012).</summary>
public enum AiEngineKind
{
    /// <summary>Pas d'IA : les règles de conformité restent actives, elles sont en C#.</summary>
    None,
    /// <summary>Claude en région européenne, sans conservation (API directe, Bedrock ou Vertex en UE).</summary>
    ClaudeEu,
    /// <summary>Modèle à poids ouverts hébergé en France ou sur le serveur du cabinet (API compatible OpenAI).</summary>
    Local,
    /// <summary>Moteur simulé déterministe, pour les tests et le banc d'évals sans clé d'API.</summary>
    Stub,
}

public static class AiPermissions
{
    /// <summary>Droit d'utiliser l'assistant (annexe F §10).</summary>
    public const string UseAssistant = "Ia.Utiliser";
    public const string ReadInvoices = "Factures.Lire";
    public const string DraftQuotes = "Devis.CreerBrouillon";
}

public static class AiAuditOrigin
{
    public const string Assistant = "AssistantIA";
}
