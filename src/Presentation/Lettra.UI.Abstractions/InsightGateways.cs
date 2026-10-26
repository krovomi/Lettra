using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Éclairages du dossier : des lectures calculées sur les pièces et les encaissements, qui n'écrivent rien. Lecture :
/// <c>DossierLire</c>, sauf le contrôle fiscal d'entraînement (<c>FecExporter</c>, comme le FEC).
/// </summary>
public interface IInsightsGateway
{
    /// <summary>La météo des paiements : un bulletin par client et la prévision des encaissements.</summary>
    Task<OperationResult<PaymentWeatherResponse>> GetPaymentWeatherAsync(CancellationToken cancellationToken);

    /// <summary>Le contrôle fiscal d'entraînement du FEC que Lettra produit pour l'exercice (permission <c>FecExporter</c>).</summary>
    Task<OperationResult<PracticeAuditResponse>> GetPracticeAuditAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Le contrôle fiscal d'entraînement d'un FEC d'un autre logiciel, sur un exercice s'il est donné.</summary>
    Task<OperationResult<PracticeAuditResponse>> RunPracticeAuditAsync(string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    /// <summary>La preuve d'authenticité d'une pièce validée : le texte de son QR code signé.</summary>
    Task<OperationResult<InvoiceProofResponse>> GetInvoiceProofAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>La vérification d'une preuve lue sur une facture, contre le registre des clés et les pièces du dossier.</summary>
    Task<OperationResult<InvoiceProofCheckResponse>> VerifyInvoiceProofAsync(string code, CancellationToken cancellationToken);

    /// <summary>Le registre des clés publiques du dossier, au format PEM.</summary>
    Task<OperationResult<IReadOnlyList<InvoiceProofKeyResponse>>> ListInvoiceProofKeysAsync(CancellationToken cancellationToken);

    /// <summary>Le dossier tel qu'il était à un instant passé, rejoué depuis le journal inaltérable.</summary>
    Task<OperationResult<DossierAsOfResponse>> GetDossierAsOfAsync(DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>« Pourquoi ce chiffre ? » : les pièces qui composent un chiffre de l'accueil, un mois donné s'il y a lieu.</summary>
    Task<OperationResult<FigureExplanationResponse>> ExplainFigureAsync(ExplainedFigureDto figure, DateOnly? month, CancellationToken cancellationToken);

    /// <summary>La fiche à lire avant d'appeler un client en retard.</summary>
    Task<OperationResult<CallSheetResponse>> GetCallSheetAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Le score de préparation du dossier à la facture électronique et ce qu'il reste à corriger.</summary>
    Task<OperationResult<EInvoicingReadinessResponse>> GetEInvoicingReadinessAsync(CancellationToken cancellationToken);

    /// <summary>Le simulateur « et si ? » : un scénario essayé sur la trésorerie attendue, sans rien écrire.</summary>
    Task<OperationResult<WhatIfResponse>> SimulateWhatIfAsync(WhatIfRequest scenario, CancellationToken cancellationToken);
}
