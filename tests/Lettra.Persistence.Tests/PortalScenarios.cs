using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Espace client sur les trois bases : lien créé, espace ouvert, paiement par carte refusé puis accepté (encaissement
/// imputé une seule fois), devis signé avec acompte validé, preuve relue, lien révoqué.
/// </summary>
internal static class PortalScenarios
{
    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", "732829320", "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: null);

    /// <summary>L'espace client ouvert, comme le fait l'API quand un compte de service lui est déclaré.</summary>
    private sealed class OpenPortal : ICustomerPortalSettings
    {
        public bool IsEnabled => true;
    }

    public static TenantHarness Harness(IDbProviderConfigurator configurator) =>
        new(configurator, configure: services => services.AddSingleton<ICustomerPortalSettings, OpenPortal>());

    private static DocumentLineRequest Line(string designation, decimal quantity, decimal price) =>
        new() { Designation = designation, Quantity = quantity, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };

    private static async Task<T> Send<T>(TenantHarness harness, DossierId dossier, ICommand<T> command)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(command, default);
    }

    private static async Task<T> Query<T>(TenantHarness harness, DossierId dossier, IQuery<T> query)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(query, default);
    }

    private static string Hash(PortalLinkResponse link) => PortalToken.Read(link.Token)!.Value.SecretHash;

    public static async Task CustomerPortalAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        Guid customer;
        QuoteId quote;
        QuoteId draftQuote;
        InvoiceId invoice;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            customer = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            quote = (await commands.SendAsync(new CreateQuoteCommand(dossier, new SaveQuoteRequest
            {
                CustomerId = customer,
                Subject = "Refonte du site",
                Lines = [Line("Conception", 5, 100m)],
            }), default)).Value;
            (await commands.SendAsync(new ValidateQuoteCommand(dossier, quote, 1), default)).IsSuccess.Should().BeTrue();
            draftQuote = (await commands.SendAsync(new CreateQuoteCommand(dossier, new SaveQuoteRequest { CustomerId = customer, Lines = [Line("Option", 1, 10m)] }), default)).Value;
            invoice = (await commands.SendAsync(new CreateInvoiceCommand(dossier, new SaveInvoiceRequest { CustomerId = customer, Lines = [Line("Conseil", 2, 50m)] }), default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, invoice, null), default)).IsSuccess.Should().BeTrue();
        }

        // Côté cabinet : refus d'abord, puis un lien d'espace complet et un lien de signature avec 30 % d'acompte.
        (await Send(harness, dossier, new CreatePortalLinkCommand(dossier, new CreatePortalLinkRequest { CustomerId = Guid.CreateVersion7() })))
            .Errors[0].Code.Should().Be(ErrorCodes.Customer.NotFound);
        (await Send(harness, dossier, new CreatePortalLinkCommand(dossier, new CreatePortalLinkRequest { CustomerId = customer, QuoteId = draftQuote.Value })))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.QuoteNotSignable);
        PortalLinkResponse space = (await Send(harness, dossier, new CreatePortalLinkCommand(dossier, new CreatePortalLinkRequest { CustomerId = customer }))).Value;
        PortalLinkResponse signing = (await Send(harness, dossier, new CreatePortalLinkCommand(dossier,
            new CreatePortalLinkRequest { CustomerId = customer, QuoteId = quote.Value, DownPaymentPercent = 30m }))).Value;
        space.Path.Should().Be("espace-client/" + space.Token);
        PortalToken.Read(space.Token)!.Value.Dossier.Should().Be(dossier);

        // Côté client : l'espace montre la facture due, le devis à signer, l'IBAN du cabinet.
        PortalView view = (await Send(harness, dossier, new OpenPortalCommand(dossier, Hash(space)))).Value;
        view.CompanyName.Should().Be("Conseil Lettra");
        view.CustomerName.Should().Be("Boulangerie Martin");
        view.Iban.Should().Be("FR7630006000011234567890189");
        view.CanPayOnline.Should().BeTrue();
        view.Invoices.Should().ContainSingle().Which.Outstanding.Should().Be(120m);
        view.Quotes.Should().ContainSingle("le brouillon n'est pas montré").Which.Should().BeEquivalentTo(new { CanSign = true, TotalInclTax = 600m, Subject = "Refonte du site" });

        // Paiement par carte : une session fermée après trois refus, puis une autre où la carte passe au second essai ; le
        // retour en double n'encaisse pas deux fois.
        SimulatedPaymentProvider payments = harness.Services.GetRequiredService<SimulatedPaymentProvider>();
        OnlineCheckoutResponse refused = (await Send(harness, dossier, new StartOnlinePaymentCommand(dossier, Hash(space), invoice.Value, "https://retour"))).Value;
        refused.Amount.Should().Be(120m);
        for (int i = 0; i < SimulatedPaymentProvider.MaxDeclines; i++)
        {
            payments.Pay(refused.CheckoutId, SimulatedPaymentProvider.DeclinedCard).Should().Be(CheckoutState.Declined);
        }

        (await Send(harness, dossier, new CompleteOnlinePaymentCommand(dossier, Hash(space), refused.CheckoutId)))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.PaymentDeclined);

        OnlineCheckoutResponse accepted = (await Send(harness, dossier, new StartOnlinePaymentCommand(dossier, Hash(space), invoice.Value, "https://retour"))).Value;
        (await Send(harness, dossier, new CompleteOnlinePaymentCommand(dossier, Hash(space), accepted.CheckoutId))).Value.Paid.Should().BeFalse("rien n'est encore payé");
        (await Send(harness, dossier, new CompleteOnlinePaymentCommand(dossier, Hash(signing), accepted.CheckoutId)))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.CheckoutNotFound, "un paiement ne se termine que par le lien qui l'a commencé");
        payments.Pay(accepted.CheckoutId, SimulatedPaymentProvider.DeclinedCard).Should().Be(CheckoutState.Declined);
        payments.Pay(accepted.CheckoutId, "4242 4242 4242 4242").Should().Be(CheckoutState.Paid);
        Result<OnlinePaymentResult>[] returns = await Task.WhenAll(
            Send(harness, dossier, new CompleteOnlinePaymentCommand(dossier, Hash(space), accepted.CheckoutId)),
            Send(harness, dossier, new CompleteOnlinePaymentCommand(dossier, Hash(space), accepted.CheckoutId)));
        returns.Should().OnlyContain(r => r.IsSuccess && r.Value.Paid && r.Value.Amount == 120m);
        returns.Count(r => r.Value.PaymentNumber != null).Should().Be(1, "deux retours simultanés n'encaissent qu'une fois");
        (await Send(harness, dossier, new CompleteOnlinePaymentCommand(dossier, Hash(space), accepted.CheckoutId))).Value.Paid.Should().BeTrue();
        (await Send(harness, dossier, new StartOnlinePaymentCommand(dossier, Hash(space), invoice.Value, "https://retour")))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.NothingToPay);
        InvoiceResponse settled = (await Query(harness, dossier, new GetInvoiceQuery(dossier, invoice))).Value;
        settled.PaidInclTax.Should().Be(120m, "un seul encaissement par carte, imputé sur la facture");

        // Signature : sans accord, refusée ; puis signée, devis accepté, acompte de 30 % validé et payable.
        (await Send(harness, dossier, new SignQuoteOnPortalCommand(dossier, Hash(signing), quote.Value, new SignQuoteRequest("Marie Martin", false))))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.ConsentRequired);
        (await Send(harness, dossier, new SignQuoteOnPortalCommand(dossier, Hash(signing), quote.Value, new SignQuoteRequest(" ", true))))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.SignerNameInvalid);
        SignQuoteResult signed = (await Send(harness, dossier, new SignQuoteOnPortalCommand(dossier, Hash(signing), quote.Value,
            new SignQuoteRequest("  Marie   Martin ", true)))).Value;
        signed.Signature.SignerName.Should().Be("Marie Martin");
        signed.Signature.Provider.Should().Be(SimulatedSignatureProvider.ProviderName);
        signed.Signature.DocumentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        signed.DownPaymentInvoiceId.Should().NotBeNull();
        (await Send(harness, dossier, new SignQuoteOnPortalCommand(dossier, Hash(signing), quote.Value, new SignQuoteRequest("Marie Martin", true))))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.AlreadySigned);

        QuoteResponse acceptedQuote = (await Query(harness, dossier, new GetQuoteQuery(dossier, quote))).Value;
        acceptedQuote.Status.Should().Be(QuoteStatusDto.Accepted);
        InvoiceResponse downPayment = (await Query(harness, dossier, new GetInvoiceQuery(dossier, new InvoiceId(signed.DownPaymentInvoiceId!.Value)))).Value;
        downPayment.Should().BeEquivalentTo(new { Kind = InvoiceKindDto.DownPayment, Status = InvoiceStatusDto.Validated });
        downPayment.Totals.TotalInclTax.Should().Be(180m);
        (await Query(harness, dossier, new GetQuoteSignatureQuery(dossier, quote.Value))).Value.DownPaymentInvoiceId.Should().Be(signed.DownPaymentInvoiceId);
        (await Query(harness, dossier, new GetQuoteSignatureQuery(dossier, draftQuote.Value))).Errors[0].Code.Should().Be(ErrorCodes.Portal.SignatureNotFound);

        PortalView afterSigning = (await Send(harness, dossier, new OpenPortalCommand(dossier, Hash(signing)))).Value;
        afterSigning.FocusQuoteId.Should().Be(quote.Value);
        afterSigning.DownPaymentPercent.Should().Be(30m);
        afterSigning.Quotes.Single().Should().BeEquivalentTo(new { CanSign = false, Status = QuoteStatusDto.Accepted });
        afterSigning.Quotes.Single().Signature!.SignerName.Should().Be("Marie Martin");
        afterSigning.Invoices.Select(i => (i.Number, i.Outstanding)).Should().Equal((downPayment.Number!, 180m), (settled.Number!, 0m));

        // Le cabinet voit ses liens ouverts, en révoque un : il ne s'ouvre plus.
        IReadOnlyList<PortalLinkSummary> links = (await Query(harness, dossier, new ListPortalLinksQuery(dossier, customer))).Value;
        links.Should().HaveCount(2).And.OnlyContain(l => l.IsActive && l.LastOpenedAt != null);
        (await Send(harness, dossier, new RevokePortalLinkCommand(dossier, space.Id))).IsSuccess.Should().BeTrue();
        (await Send(harness, dossier, new RevokePortalLinkCommand(dossier, space.Id))).Errors[0].Code.Should().Be(ErrorCodes.Portal.LinkRevoked);
        (await Send(harness, dossier, new RevokePortalLinkCommand(dossier, Guid.CreateVersion7()))).Errors[0].Code.Should().Be(ErrorCodes.Portal.LinkNotFound);
        (await Send(harness, dossier, new OpenPortalCommand(dossier, Hash(space)))).Errors[0].Code.Should().Be(ErrorCodes.Portal.LinkNotFound);
        (await Send(harness, dossier, new OpenPortalCommand(dossier, ""))).Errors[0].Code.Should().Be(ErrorCodes.Portal.LinkNotFound);
        (await Query(harness, dossier, new ListPortalLinksQuery(dossier, customer))).Value.Single(l => l.Id == space.Id).RevokedAt.Should().NotBeNull();
    }

    /// <summary>Sans compte de service déclaré (poste isolé, hôte non configuré), l'espace client reste fermé.</summary>
    public static async Task ClosedPortalAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        Guid customer = (await Send(harness, dossier, new CreateCustomerCommand(dossier, Martin))).Value.Value;

        (await Send(harness, dossier, new CreatePortalLinkCommand(dossier, new CreatePortalLinkRequest { CustomerId = customer })))
            .Errors[0].Code.Should().Be(ErrorCodes.Portal.Disabled);
        (await Send(harness, dossier, new OpenPortalCommand(dossier, new string('a', 64)))).Errors[0].Code.Should().Be(ErrorCodes.Portal.Disabled);
    }
}
