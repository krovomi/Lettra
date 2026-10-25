using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Éclairages du dossier : des lectures calculées sur les pièces et les encaissements, qui n'écrivent rien. Lecture :
/// <c>DossierLire</c>.
/// </summary>
public interface IInsightsGateway
{
    /// <summary>La météo des paiements : un bulletin par client et la prévision des encaissements.</summary>
    Task<OperationResult<PaymentWeatherResponse>> GetPaymentWeatherAsync(CancellationToken cancellationToken);
}
