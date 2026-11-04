using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.Mail;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Devis à relancer et carnet de commandes par l'API réelle (mode 2, SQLite), avec un port e-mail qui garde les messages.
/// Le devis est créé avec une fin de validité proche : il entre aussitôt dans la fenêtre de relance.
/// </summary>
public sealed class QuoteFollowUpApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client, Guid? user = null)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        if (user is Guid id)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(id));
        }

        return client;
    }

    private WebApplicationFactory<Program> WithMail(CapturedMail mail) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(mail)));

    /// <summary>Fiche entreprise, client Martin et devis validé aujourd'hui, valable trois jours.</summary>
    private static async Task<Guid> QuoteEndingSoonAsync(HttpClient owned)
    {
        using HttpClient client = owned;
        using HttpRequestMessage profile = new(HttpMethod.Put, Api("/api/v1/company-profile"))
        {
            Content = JsonContent.Create(
                new SaveCompanyProfileRequest
                {
                    Name = "Conseil Lettra",
                    LegalForm = "SAS",
                    ShareCapital = 10_000m,
                    Siren = "552100554",
                    Rcs = "RCS Paris 552 100 554",
                    VatNumber = "FR96552100554",
                    AddressLine1 = "10 avenue de l'Opéra",
                    PostalCode = "75001",
                    City = "Paris",
                    LatePenaltyRate = 10m,
                    EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
                },
                ContractsJsonContext.Default.SaveCompanyProfileRequest),
        };
        profile.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        (await client.SendAsync(profile)).IsSuccessStatusCode.Should().BeTrue();

        using HttpResponseMessage customer = await client.PostAsJsonAsync(Api("/api/v1/customers"), CustomerApiTests.Martin(), ContractsJsonContext.Default.CreateCustomerRequest);
        Guid customerId = (await customer.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CustomerResponse))!.Id;
        using HttpResponseMessage draft = await client.PostAsJsonAsync(
            Api("/api/v1/quotes"),
            new SaveQuoteRequest
            {
                CustomerId = customerId,
                ValidUntil = LegalCalendar.DateOf(DateTimeOffset.UtcNow).AddDays(3),
                Subject = "Refonte du site",
                Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 2, UnitPriceExclTax = 600m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
            },
            ContractsJsonContext.Default.SaveQuoteRequest);
        draft.StatusCode.Should().Be(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync());
        Guid quote = (await draft.Content.ReadFromJsonAsync(ContractsJsonContext.Default.QuoteResponse))!.Id;

        using HttpRequestMessage validate = new(HttpMethod.Post, Api($"/api/v1/quotes/{quote}/validate"));
        validate.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        using HttpResponseMessage validated = await client.SendAsync(validate);
        validated.StatusCode.Should().Be(HttpStatusCode.OK, await validated.Content.ReadAsStringAsync());
        return quote;
    }

    [Fact]
    public async Task Un_devis_qui_approche_de_sa_fin_se_relance_par_e_mail_et_reste_dans_l_historique()
    {
        // Le devis est validé par l'hôte de la classe, qui garde la clé d'intégrité du dossier ; l'hôte au port e-mail
        // capturé ne fait que relancer.
        Guid quote = await QuoteEndingSoonAsync(InDossier(factory.CreateClient()));
        CapturedMail mail = new();
        using WebApplicationFactory<Program> host = WithMail(mail);
        using HttpClient manager = InDossier(host.CreateClient());

        QuoteFollowUpOverviewResponse overview = (await manager.GetFromJsonAsync(Api("/api/v1/quotes/follow-ups"), ContractsJsonContext.Default.QuoteFollowUpOverviewResponse))!;
        QuoteToFollowUpResponse row = overview.Quotes.Should().ContainSingle(q => q.QuoteId == quote).Subject;
        (row.DaysLeft, row.Due, row.CustomerEmail, row.TotalExclTax).Should().Be((3, true, "compta@martin.fr", 1_200m));
        overview.Settings.EmailConfigured.Should().BeTrue();

        QuoteFollowUpPreviewResponse preview = (await manager.GetFromJsonAsync(
            Api($"/api/v1/quotes/{quote}/follow-up/preview"), ContractsJsonContext.Default.QuoteFollowUpPreviewResponse))!;
        (preview.Language, preview.Recipient, preview.DaysLeft).Should().Be(("fr-FR", "compta@martin.fr", 3));
        preview.Subject.Should().StartWith("Votre devis ");

        using HttpResponseMessage sent = await manager.PostAsJsonAsync(
            Api($"/api/v1/quotes/{quote}/follow-up"),
            new SendQuoteFollowUpRequest { Channel = QuoteFollowUpChannelDto.Email, Subject = preview.Subject, Body = preview.Body + "\nÀ bientôt." },
            ContractsJsonContext.Default.SendQuoteFollowUpRequest);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        QuoteFollowUpResponse followUp = (await sent.Content.ReadFromJsonAsync(ContractsJsonContext.Default.QuoteFollowUpResponse))!;
        (followUp.Outcome, followUp.Recipient, followUp.CustomerName).Should().Be((QuoteFollowUpOutcomeDto.Sent, "compta@martin.fr", "Boulangerie Martin"));
        mail.Sent.Should().ContainSingle().Which.Should().Match<EmailMessage>(m =>
            m.To.Value == "compta@martin.fr" && m.Subject == preview.Subject && m.TextBody.EndsWith("À bientôt.", StringComparison.Ordinal) && m.SenderName == "Conseil Lettra");

        PagedResponse<QuoteFollowUpResponse> history = (await manager.GetFromJsonAsync(
            Api($"/api/v1/quotes/follow-ups/history?quoteId={quote}&customerId={followUp.CustomerId}&channel=Email&outcome=Sent&search=martin&sort=Customer&descending=false&page=1&pageSize=10"),
            ContractsJsonContext.Default.PagedResponseQuoteFollowUpResponse))!;
        history.Items.Should().ContainSingle().Which.Id.Should().Be(followUp.Id);

        QuoteFollowUpOverviewResponse after = (await manager.GetFromJsonAsync(Api("/api/v1/quotes/follow-ups"), ContractsJsonContext.Default.QuoteFollowUpOverviewResponse))!;
        after.Quotes.Single(q => q.QuoteId == quote).Should().Match<QuoteToFollowUpResponse>(q => !q.Due && q.FollowUpCount == 1);

        OrderBookResponse book = (await manager.GetFromJsonAsync(Api("/api/v1/quotes/order-book"), ContractsJsonContext.Default.OrderBookResponse))!;
        book.Quotes.Should().Contain(q => q.QuoteId == quote && q.TotalExclTax == 1_200m && q.Probability > 0m);
        book.AsOf.Should().Be(LegalCalendar.DateOf(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task La_fenetre_se_regle_et_une_fenetre_invalide_est_refusee()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        using HttpClient collaborateur = InDossier(factory.CreateClient(), LettraApiFactory.CollaborateurId);

        using HttpResponseMessage invalid = await manager.PutAsJsonAsync(
            Api("/api/v1/quotes/follow-ups/settings"), new UpdateQuoteFollowUpSettingsRequest { WindowDays = 31 }, ContractsJsonContext.Default.UpdateQuoteFollowUpSettingsRequest);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.QuoteFollowUps.WindowInvalid);

        using HttpResponseMessage saved = await manager.PutAsJsonAsync(
            Api("/api/v1/quotes/follow-ups/settings"), new UpdateQuoteFollowUpSettingsRequest { WindowDays = 7 }, ContractsJsonContext.Default.UpdateQuoteFollowUpSettingsRequest);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await saved.Content.ReadFromJsonAsync(ContractsJsonContext.Default.QuoteFollowUpSettingsResponse))!.WindowDays.Should().Be(7);
        (await manager.GetFromJsonAsync(Api("/api/v1/quotes/follow-ups"), ContractsJsonContext.Default.QuoteFollowUpOverviewResponse))!
            .Settings.WindowDays.Should().Be(7);

        // Un collaborateur relance et lit, mais ne règle pas la fenêtre.
        (await collaborateur.PutAsJsonAsync(
            Api("/api/v1/quotes/follow-ups/settings"), new UpdateQuoteFollowUpSettingsRequest { WindowDays = 5 }, ContractsJsonContext.Default.UpdateQuoteFollowUpSettingsRequest))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await collaborateur.GetAsync(Api("/api/v1/quotes/follow-ups"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_devis_inconnu_ou_un_client_sans_adresse_sont_refuses_sans_trace()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid quote = await QuoteEndingSoonAsync(InDossier(factory.CreateClient()));

        (await manager.GetAsync(Api($"/api/v1/quotes/{Guid.CreateVersion7()}/follow-up/preview"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Sans serveur d'envoi : refus explicite, rien n'est gardé ; la relance notée à la main passe.
        using HttpResponseMessage refused = await manager.PostAsJsonAsync(
            Api($"/api/v1/quotes/{quote}/follow-up"), new SendQuoteFollowUpRequest(), ContractsJsonContext.Default.SendQuoteFollowUpRequest);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Email.NotConfigured);
        using HttpResponseMessage tooLong = await manager.PostAsJsonAsync(
            Api($"/api/v1/quotes/{quote}/follow-up"), new SendQuoteFollowUpRequest { Subject = new string('a', 301) }, ContractsJsonContext.Default.SendQuoteFollowUpRequest);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using HttpResponseMessage noted = await manager.PostAsJsonAsync(
            Api($"/api/v1/quotes/{quote}/follow-up"), new SendQuoteFollowUpRequest { Channel = QuoteFollowUpChannelDto.Manual }, ContractsJsonContext.Default.SendQuoteFollowUpRequest);
        noted.StatusCode.Should().Be(HttpStatusCode.OK, await noted.Content.ReadAsStringAsync());

        (await manager.GetFromJsonAsync(Api($"/api/v1/quotes/follow-ups/history?quoteId={quote}"), ContractsJsonContext.Default.PagedResponseQuoteFollowUpResponse))!
            .Items.Should().ContainSingle().Which.Channel.Should().Be(QuoteFollowUpChannelDto.Manual);
        (await manager.GetAsync(Api("/api/v1/quotes/follow-ups/history?page=0"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class CapturedMail : IEmailSender
    {
        public bool IsConfigured => true;

        public ConcurrentQueue<EmailMessage> Sent { get; } = new();

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Enqueue(message);
            return Task.FromResult(Result.Success());
        }
    }
}
