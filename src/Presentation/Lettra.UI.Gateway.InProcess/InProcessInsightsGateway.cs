using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Exports.Application.Insights;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/insights</c> de l'API.</summary>
internal sealed class InProcessInsightsGateway(IServiceScopeFactory scopes) : IInsightsGateway
{
    public Task<OperationResult<PaymentWeatherResponse>> GetPaymentWeatherAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPaymentWeatherQuery(dossier), ct), cancellationToken);
}
