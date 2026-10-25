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
}
