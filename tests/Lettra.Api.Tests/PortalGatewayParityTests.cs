using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Espace client côté cabinet : sur un poste isolé comme sur un serveur sans compte de service, mêmes refus.</summary>
public sealed partial class GatewayParityTests
{
    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Espace_client_memes_refus_quand_il_est_ferme(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IPortalGateway portal = services.GetRequiredService<IPortalGateway>();
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;

        OperationResult<PortalLinkCreated> created = await portal.CreateLinkAsync(new CreatePortalLinkRequest { CustomerId = customer }, default);
        OperationResult<IReadOnlyList<PortalLinkSummary>> links = await portal.ListLinksAsync(customer, default);
        OperationResult<bool> revoked = await portal.RevokeLinkAsync(Guid.CreateVersion7(), default);
        OperationResult<QuoteSignatureResponse> signature = await portal.GetQuoteSignatureAsync(Guid.CreateVersion7(), default);

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Portal.Disabled);
        links.Value.Should().BeEmpty();
        revoked.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Portal.LinkNotFound);
        signature.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Portal.SignatureNotFound);
    }
}
