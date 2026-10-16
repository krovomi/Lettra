using System.Net;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Lettra.Api.Tests;

/// <summary>
/// Navigateur hors ligne : les gateways HTTP répondent « serveur injoignable » sans envoyer de requête, donc sans erreur
/// réseau dans la console pour chaque appel ; en ligne, la requête part comme avant.
/// </summary>
public sealed class OfflineGatewayTests
{
    private sealed class Network(bool offline) : INetworkStatus
    {
        public ValueTask<bool> IsOfflineAsync(CancellationToken cancellationToken) => ValueTask.FromResult(offline);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public int Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"isAuthenticated":false,"mode":"Oidc","localAccountExists":false,"items":[],"page":1,"pageSize":50,"totalCount":0}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (ServiceProvider Services, Recorder Recorder) Gateways(bool offline)
    {
        Recorder recorder = new();
        ServiceCollection services = new();
        services.AddSingleton<IDossierSelection>(new FixedDossierSelection(Guid.Parse("0199e3a1-0000-7000-8000-00000000000a")));
        services.AddSingleton<INetworkStatus>(new Network(offline));
        services.AddHttpGateways(new Uri("http://lettra.test/"));
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = recorder));
        return (services.BuildServiceProvider(), recorder);
    }

    [Fact]
    public async Task Hors_ligne_aucune_requete_ne_part_et_le_serveur_est_injoignable()
    {
        (ServiceProvider services, Recorder recorder) = Gateways(offline: true);
        await using ServiceProvider _ = services;

        OperationResult<PagedResponse<CustomerSummary>> customers = await services.GetRequiredService<ICustomersGateway>().ListAsync(1, 50, CancellationToken.None);
        OperationResult<Contracts.Identity.SessionResponse> session = await services.GetRequiredService<ISessionGateway>().GetAsync(CancellationToken.None);

        recorder.Sent.Should().Be(0);
        customers.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Api.Unreachable);
        session.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Api.Unreachable);
    }

    [Fact]
    public async Task En_ligne_la_requete_part()
    {
        (ServiceProvider services, Recorder recorder) = Gateways(offline: false);
        await using ServiceProvider _ = services;

        OperationResult<PagedResponse<CustomerSummary>> customers = await services.GetRequiredService<ICustomersGateway>().ListAsync(1, 50, CancellationToken.None);

        recorder.Sent.Should().Be(1);
        customers.IsSuccess.Should().BeTrue();
    }
}
