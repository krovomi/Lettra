using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>Devis à relancer et carnet de commandes, par les dispatchers, sur des dépôts en mémoire.</summary>
public sealed class QuoteFollowUpTests : IAsyncLifetime
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly Guid User = Guid.CreateVersion7();

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", null, "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", false, false, 30, null, 10m, "Pas d'escompte", null, null);

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly InMemoryBilling _billing = new();
    private readonly CapturingEmailSender _email = new();
    private readonly SettableClock _clock = new(Now);
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;
    private int _number;

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(new FakeUser { UserId = User });
        services.AddSingleton<IPermissionService>(new FakePermissions());
        InMemoryCustomers customers = new();
        services.AddSingleton<ICustomerRepository>(customers);
        services.AddSingleton<ICustomerReadStore>(customers);
        services.AddSingleton<IUnitOfWork>(customers);
        services.AddIntegrityFakes(new InMemoryIntegrity(Dossier));
        services.AddPartiesAndCatalogFakes(new InMemoryPartiesAndCatalog(), new FakeDirectories(), _billing);
        services.AddSingleton<IDossierProvisioner>(new RecordingDossierProvisioner());
        services.AddSingleton<IAuthorizationDirectory>(new EmptyAuthorizationDirectory());
        services.AddSingleton<IDossierDirectory>(new EmptyDossierDirectory());
        services.AddCabinetFakes();
        services.AddSingleton<IEmailSender>(_email);
        services.AddSingleton<TimeProvider>(_clock);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateAsyncScope();
        _billing.Profile = CompanyProfile.Create(Cabinet, Now).Value;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }

    private ICommandDispatcher Commands => _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

    private IQueryDispatcher Queries => _scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

    private InMemoryQuoteFollowUps Store => _root.GetRequiredService<InMemoryQuoteFollowUps>();

    /// <summary>Un devis validé il y a <paramref name="issuedDaysAgo"/> jours, valable <paramref name="validDays"/> jours.</summary>
    private Quote AddQuote(
        string customerName, int issuedDaysAgo, int validDays, decimal unitPrice = 1_000m, string? email = "contact@client.fr", string? language = null,
        bool? accepted = null, string? subject = "Refonte du site")
    {
        CustomerId customer = new(Guid.CreateVersion7());
        PartySnapshot party = new(customerName, "1 rue de la Paix", null, "75002", "Paris", "FR", null, null, null, null, null, null, IsBusiness: true);
        DateOnly issued = Today.AddDays(-issuedDaysAgo);
        LineDraft line = new(null, null, "Conseil", null, CatalogItemKind.Service, "HUR", 1m, unitPrice, 0m, "FR-20", null);
        Quote quote = Quote.Create(QuoteId.New(), new QuoteDraft(customer, party, issued.AddDays(validDays), VatRegime.Standard, subject, null, [line]), User, Now).Value;
        quote.Validate($"DEV{++_number:000000}", issued, party, User, Now).IsSuccess.Should().BeTrue();
        if (accepted is { } outcome)
        {
            quote.SetOutcome(outcome, User, Now).IsSuccess.Should().BeTrue();
        }

        _billing.Quotes[quote.Id] = quote;
        Store.Contacts[customer] = new QuoteCustomerContact(email, language);
        return quote;
    }

    private Task<Result<QuoteFollowUpOverviewResponse>> OverviewAsync() =>
        Queries.QueryAsync(new GetQuoteFollowUpOverviewQuery(Dossier), default);

    private Task<Result<QuoteFollowUpResponse>> SendAsync(Quote quote, QuoteFollowUpChannelDto channel, string? subject = null, string? body = null) =>
        Commands.SendAsync(new SendQuoteFollowUpCommand(Dossier, quote.Id, new SendQuoteFollowUpRequest { Channel = channel, Subject = subject, Body = body }), default);

    [Fact]
    public async Task Les_devis_sans_reponse_qui_approchent_de_leur_fin_sont_a_relancer_d_abord()
    {
        Quote soon = AddQuote("Boulangerie Martin", 27, 30);
        Quote today = AddQuote("Garage Dupont", 30, 30, 300m);
        AddQuote("Hôtel du Lac", 5, 30);
        AddQuote("Cave Bernard", 27, 30, accepted: true);
        AddQuote("Pressing Lune", 40, 30);

        QuoteFollowUpOverviewResponse overview = (await OverviewAsync()).Value;

        overview.Quotes.Select(q => (q.Number, q.DaysLeft, q.Due)).Should().Equal((today.Number!, 0, true), (soon.Number!, 3, true));
        (overview.DueCount, overview.DueExclTax).Should().Be((2, 1_300m));
        overview.Settings.Should().Be(new QuoteFollowUpSettingsResponse(5, true, 0));
        QuoteToFollowUpResponse first = overview.Quotes[1];
        (first.CustomerName, first.CustomerEmail, first.Language).Should().Be(("Boulangerie Martin", "contact@client.fr", "fr-FR"));
        (first.IssueDate, first.ValidUntil, first.DaysWithoutAnswer).Should().Be((Today.AddDays(-27), Today.AddDays(3), 27));
        (first.TotalExclTax, first.TotalInclTax).Should().Be((1_000m, 1_200m));
        (first.Probability, first.WeightedExclTax).Should().Be((0.5m, 500m), "sans historique, chaque devis compte pour une chance sur deux");
        (first.FollowUpCount, first.LastFollowUpAt, first.LastChannel).Should().Be((0, null, null));
    }

    [Fact]
    public async Task Sans_devis_dans_la_fenetre_la_liste_est_vide()
    {
        AddQuote("Hôtel du Lac", 5, 30);

        QuoteFollowUpOverviewResponse overview = (await OverviewAsync()).Value;

        overview.Quotes.Should().BeEmpty();
        (overview.DueCount, overview.DueExclTax).Should().Be((0, 0m));
    }

    [Fact]
    public async Task La_relance_proposee_est_ecrite_dans_la_langue_du_client()
    {
        Quote french = AddQuote("Boulangerie Martin", 27, 30);
        Quote english = AddQuote("London Bakery Ltd", 29, 30, language: "en-GB", subject: null);

        QuoteFollowUpPreviewResponse fr = (await Queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(Dossier, french.Id), default)).Value;
        QuoteFollowUpPreviewResponse en = (await Queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(Dossier, english.Id), default)).Value;

        (fr.QuoteNumber, fr.CustomerName, fr.Recipient, fr.Language, fr.DaysLeft, fr.EmailConfigured)
            .Should().Be((french.Number!, "Boulangerie Martin", "contact@client.fr", "fr-FR", 3, true));
        fr.Subject.Should().Be($"Votre devis {french.Number} : valable jusqu'au 6 novembre 2026");
        fr.Body.Should().StartWith("Bonjour,").And.Contain("« Refonte du site »").And.Contain("000,00\u00a0€ HT").And.Contain("encore 3 jours")
            .And.EndWith("Cordialement,\nConseil Lettra\n");
        en.Language.Should().Be("en-US");
        en.Subject.Should().Be($"Your quote {english.Number}: valid until 4 November 2026");
        en.Body.Should().Contain("valid until tomorrow").And.Contain("€1,000.00 excluding VAT").And.NotContain("(\"");
    }

    [Theory]
    [InlineData(0, "Ce devis arrive à échéance aujourd'hui.")]
    [InlineData(1, "Ce devis reste valable jusqu'à demain")]
    public async Task Le_texte_dit_quand_la_validite_se_termine(int daysLeft, string expected)
    {
        Quote quote = AddQuote("Boulangerie Martin", 30 - daysLeft, 30);

        QuoteFollowUpPreviewResponse preview = (await Queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(Dossier, quote.Id), default)).Value;

        preview.Body.Should().Contain(expected);
    }

    [Fact]
    public async Task Le_texte_anglais_dit_aussi_le_dernier_jour_et_les_jours_restants()
    {
        Quote lastDay = AddQuote("London Bakery Ltd", 30, 30, language: "en");
        Quote later = AddQuote("Bristol Tea Co", 26, 30, language: "en");

        string first = (await Queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(Dossier, lastDay.Id), default)).Value.Body;
        string second = (await Queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(Dossier, later.Id), default)).Value.Body;

        first.Should().Contain("This quote expires today.").And.Contain("(\"Refonte du site\")");
        second.Should().Contain("4 more days");
    }

    [Fact]
    public async Task Un_devis_inconnu_repondu_expire_ou_sans_fiche_entreprise_n_a_pas_de_relance()
    {
        Quote accepted = AddQuote("Cave Bernard", 27, 30, accepted: true);
        Quote expired = AddQuote("Pressing Lune", 40, 30);
        Quote pending = AddQuote("Boulangerie Martin", 27, 30);

        (await Queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(Dossier, QuoteId.New()), default)).Errors.Single().Code.Should().Be(QuoteErrors.NotFoundCode);
        (await Queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(Dossier, accepted.Id), default)).Errors.Single().Code.Should().Be(QuoteFollowUpErrors.QuoteNotPendingCode);
        (await SendAsync(expired, QuoteFollowUpChannelDto.Manual)).Errors.Single().Code.Should().Be(QuoteFollowUpErrors.QuoteNotPendingCode);
        _billing.Profile = null;
        (await SendAsync(pending, QuoteFollowUpChannelDto.Email)).Errors.Single().Code.Should().Be(QuoteFollowUpErrors.CompanyProfileMissingCode);
        Store.FollowUps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relancer_par_e_mail_envoie_le_texte_relu_et_le_garde_dans_l_historique()
    {
        Quote quote = AddQuote("Boulangerie Martin", 27, 30);

        Result<QuoteFollowUpResponse> sent = await SendAsync(quote, QuoteFollowUpChannelDto.Email, "  Notre devis  ", "Bonjour,\nOù en êtes-vous ?\n");

        sent.IsSuccess.Should().BeTrue(string.Join(", ", sent.Errors.Select(e => e.Code)));
        EmailMessage message = _email.Sent.Should().ContainSingle().Subject;
        (message.To.Value, message.Subject, message.TextBody, message.SenderName).Should().Be(("contact@client.fr", "Notre devis", "Bonjour,\nOù en êtes-vous ?", "Conseil Lettra"));
        sent.Value.Should().BeEquivalentTo(new
        {
            QuoteId = quote.Id.Value,
            QuoteNumber = quote.Number,
            CustomerName = "Boulangerie Martin",
            Channel = QuoteFollowUpChannelDto.Email,
            Outcome = QuoteFollowUpOutcomeDto.Sent,
            ErrorCode = (string?)null,
            Recipient = "contact@client.fr",
            Language = "fr-FR",
            Subject = "Notre devis",
            AmountExclTax = 1_000m,
            ValidUntil = Today.AddDays(3),
            OccurredAt = Now,
        });

        QuoteToFollowUpResponse row = (await OverviewAsync()).Value.Quotes.Single();
        (row.Due, row.FollowUpCount, row.LastFollowUpAt, row.LastChannel).Should().Be((false, 1, Now, QuoteFollowUpChannelDto.Email));
        PagedResponse<QuoteFollowUpResponse> history = (await Queries.QueryAsync(new ListQuoteFollowUpsQuery(Dossier, QuoteId: quote.Id.Value), default)).Value;
        history.Items.Should().ContainSingle().Which.Id.Should().Be(sent.Value.Id);
        history.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Sans_texte_relu_la_relance_proposee_part_telle_quelle()
    {
        Quote quote = AddQuote("Boulangerie Martin", 27, 30);

        (await SendAsync(quote, QuoteFollowUpChannelDto.Email)).IsSuccess.Should().BeTrue();

        _email.Sent.Single().Subject.Should().Be($"Votre devis {quote.Number} : valable jusqu'au 6 novembre 2026");
    }

    [Fact]
    public async Task Une_relance_notee_a_la_main_compte_sans_rien_envoyer()
    {
        Quote quote = AddQuote("Boulangerie Martin", 27, 30, email: null);

        QuoteFollowUpResponse noted = (await SendAsync(quote, QuoteFollowUpChannelDto.Manual)).Value;

        (noted.Channel, noted.Outcome, noted.Recipient).Should().Be((QuoteFollowUpChannelDto.Manual, QuoteFollowUpOutcomeDto.Sent, null));
        _email.Sent.Should().BeEmpty();
        (await OverviewAsync()).Value.DueCount.Should().Be(0);
    }

    [Fact]
    public async Task Un_client_sans_adresse_ou_une_installation_sans_envoi_ne_laissent_aucune_trace()
    {
        Quote withoutEmail = AddQuote("Boulangerie Martin", 27, 30, email: null);
        Quote invalidEmail = AddQuote("Garage Dupont", 27, 30, email: "pas une adresse");
        Quote quote = AddQuote("Hôtel du Lac", 27, 30);

        (await SendAsync(withoutEmail, QuoteFollowUpChannelDto.Email)).Errors.Single().Code.Should().Be(QuoteFollowUpErrors.CustomerEmailMissingCode);
        (await SendAsync(invalidEmail, QuoteFollowUpChannelDto.Email)).Errors.Single().Code.Should().Be(QuoteFollowUpErrors.CustomerEmailMissingCode);
        _email.IsConfigured = false;
        (await SendAsync(quote, QuoteFollowUpChannelDto.Email)).Errors.Single().Code.Should().Be(ErrorCodes.Email.NotConfigured);
        Store.FollowUps.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_envoi_refuse_est_garde_en_echec_et_le_devis_reste_a_relancer()
    {
        Quote quote = AddQuote("Boulangerie Martin", 27, 30);
        _email.Refuse = true;

        Result<QuoteFollowUpResponse> sent = await SendAsync(quote, QuoteFollowUpChannelDto.Email);

        sent.Errors.Single().Code.Should().Be(ErrorCodes.Email.SendFailed);
        QuoteFollowUp failed = Store.FollowUps.Should().ContainSingle().Subject;
        (failed.Outcome, failed.ErrorCode).Should().Be((QuoteFollowUpOutcome.Failed, ErrorCodes.Email.SendFailed));
        (await OverviewAsync()).Value.Quotes.Single().Should().Match<QuoteToFollowUpResponse>(q => q.Due && q.FollowUpCount == 0);
    }

    [Fact]
    public async Task Un_objet_vide_ou_un_texte_vide_sont_refuses()
    {
        Quote quote = AddQuote("Boulangerie Martin", 27, 30);

        Result<QuoteFollowUpResponse> refused = await SendAsync(quote, QuoteFollowUpChannelDto.Email, " ", "");

        refused.Errors.Select(e => (e.Code, e.Target)).Should().Equal(
            (QuoteFollowUpErrors.MessageInvalidCode, "Subject"), (QuoteFollowUpErrors.MessageInvalidCode, "Body"));
        Store.FollowUps.Should().BeEmpty();
    }

    [Fact]
    public async Task Elargir_la_fenetre_ajoute_les_devis_qui_finissent_plus_tard()
    {
        Quote later = AddQuote("Hôtel du Lac", 20, 30);

        Result<QuoteFollowUpSettingsResponse> saved = await Commands.SendAsync(
            new UpdateQuoteFollowUpSettingsCommand(Dossier, new UpdateQuoteFollowUpSettingsRequest { WindowDays = 10 }), default);
        Result<QuoteFollowUpSettingsResponse> again = await Commands.SendAsync(
            new UpdateQuoteFollowUpSettingsCommand(Dossier, new UpdateQuoteFollowUpSettingsRequest { WindowDays = 12 }), default);

        saved.Value.Should().Be(new QuoteFollowUpSettingsResponse(10, true, 1));
        again.Value.Version.Should().Be(2);
        QuoteFollowUpOverviewResponse overview = (await OverviewAsync()).Value;
        overview.Quotes.Single().Number.Should().Be(later.Number);
        overview.Settings.WindowDays.Should().Be(12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Une_fenetre_hors_de_1_a_30_jours_est_refusee(int days)
    {
        Result<QuoteFollowUpSettingsResponse> refused = await Commands.SendAsync(
            new UpdateQuoteFollowUpSettingsCommand(Dossier, new UpdateQuoteFollowUpSettingsRequest { WindowDays = days }), default);

        refused.IsFailure.Should().BeTrue();
        Store.Settings.Should().BeNull();
    }

    [Fact]
    public async Task L_historique_refuse_une_page_ou_une_recherche_hors_limites()
    {
        (await Queries.QueryAsync(new ListQuoteFollowUpsQuery(Dossier, Page: 0), default)).IsFailure.Should().BeTrue();
        (await Queries.QueryAsync(new ListQuoteFollowUpsQuery(Dossier, PageSize: ListQuoteFollowUpsQuery.MaxPageSize + 1), default)).IsFailure.Should().BeTrue();
        (await Queries.QueryAsync(new ListQuoteFollowUpsQuery(Dossier, Search: new string('a', 101)), default)).Errors
            .Should().ContainSingle().Which.Target.Should().Be("Search");
    }

    [Fact]
    public async Task L_historique_filtre_par_canal_et_recherche()
    {
        Quote martin = AddQuote("Boulangerie Martin", 27, 30);
        Quote dupont = AddQuote("Garage Dupont", 27, 30);
        (await SendAsync(martin, QuoteFollowUpChannelDto.Email)).IsSuccess.Should().BeTrue();
        (await SendAsync(dupont, QuoteFollowUpChannelDto.Manual)).IsSuccess.Should().BeTrue();

        PagedResponse<QuoteFollowUpResponse> manual = (await Queries.QueryAsync(
            new ListQuoteFollowUpsQuery(Dossier, Channel: QuoteFollowUpChannelDto.Manual, Search: "  " + dupont.Number + " "), default)).Value;

        manual.Items.Should().ContainSingle().Which.QuoteNumber.Should().Be(dupont.Number);
    }

    [Fact]
    public async Task Le_carnet_de_commandes_pondere_les_devis_en_attente_par_les_taux_passes()
    {
        AddQuote("Boulangerie Martin", 60, 30, accepted: true);
        AddQuote("Boulangerie Martin", 60, 30, accepted: true);
        AddQuote("Boulangerie Martin", 60, 30, accepted: false);
        AddQuote("Garage Dupont", 70, 30);
        Quote open = AddQuote("Hôtel du Lac", 10, 30, 2_000m);

        OrderBookResponse book = (await Queries.QueryAsync(new GetOrderBookQuery(Dossier), default)).Value;

        (book.AsOf, book.HistoryFrom, book.HistoryMonths, book.MinimumSample).Should().Be((Today, Today.AddMonths(-24), 24, 3));
        (book.Accepted, book.Closed, book.GlobalRate, book.HasEnoughHistory).Should().Be((2, 4, 0.5m, true));
        book.ByAmount.Select(b => (b.Band, b.Accepted, b.Closed, b.Rate)).Should().Equal(
            (QuoteAmountBandDto.Small, 0, 0, null), (QuoteAmountBandDto.Medium, 2, 4, 0.5m), (QuoteAmountBandDto.Large, 0, 0, null), (QuoteAmountBandDto.VeryLarge, 0, 0, null));
        book.ByAge.Should().HaveCount(4);
        OrderBookQuoteResponse quote = book.Quotes.Should().ContainSingle().Subject;
        quote.Should().BeEquivalentTo(new
        {
            QuoteId = open.Id.Value,
            open.Number,
            CustomerName = "Hôtel du Lac",
            AgeDays = 10,
            TotalExclTax = 2_000m,
            TotalInclTax = 2_400m,
            AmountBand = QuoteAmountBandDto.Medium,
            AgeBand = QuoteAgeBandDto.SecondWeek,
            Customer = new CriterionRateDto(0, 0, 0.5m, true),
            Amount = new CriterionRateDto(2, 4, 0.5m, false),
        });
        (book.OpenExclTax, book.WeightedExclTax, book.WeightedInclTax).Should().Be((2_000m, quote.WeightedExclTax, quote.WeightedInclTax));
    }

    [Fact]
    public async Task Un_client_oppose_au_profilage_sort_du_carnet_et_n_a_pas_de_probabilite()
    {
        Quote opposed = AddQuote("Boulangerie Martin", 26, 30, 1_500m);
        Quote other = AddQuote("Garage Dupont", 27, 30, 1_000m);
        Store.Objecting.Add(opposed.CustomerId);

        OrderBookResponse book = (await Queries.QueryAsync(new GetOrderBookQuery(Dossier), default)).Value;
        QuoteFollowUpOverviewResponse overview = (await OverviewAsync()).Value;

        book.Quotes.Should().ContainSingle().Which.QuoteId.Should().Be(other.Id.Value);
        (book.ExcludedCount, book.ExcludedExclTax, book.OpenExclTax).Should().Be((1, 1_500m, 1_000m));
        QuoteToFollowUpResponse row = overview.Quotes.Single(q => q.QuoteId == opposed.Id.Value);
        (row.Due, row.Probability, row.WeightedExclTax).Should().Be((true, null, null));
        overview.Quotes.Single(q => q.QuoteId == other.Id.Value).Probability.Should().Be(0.5m);
    }

    private IDecisionRule FollowUpRule =>
        _scope.ServiceProvider.GetServices<IDecisionRule>().Single(r => r.Kind == QuoteFollowUpDecisionRule.RuleKind);

    private IDecisionActionHandler FollowUpAction =>
        _scope.ServiceProvider.GetServices<IDecisionActionHandler>().Single(a => a.ActionKind == SendQuoteFollowUpDecisionAction.ActionKindName);

    [Fact]
    public async Task Un_devis_a_relancer_devient_une_decision_avec_l_envoi_prepare()
    {
        Quote lastDay = AddQuote("Boulangerie Martin", 29, 30, 1_500m);
        Quote soon = AddQuote("Garage Dupont", 27, 30, 1_000m, email: null);
        AddQuote("Hôtel du Lac", 5, 30);
        Quote followedUp = AddQuote("Fleurs Petit", 27, 30);
        (await SendAsync(followedUp, QuoteFollowUpChannelDto.Manual)).IsSuccess.Should().BeTrue();

        IReadOnlyList<DecisionProposal> proposals = await FollowUpRule.ProposeAsync(Today, default);

        FollowUpRule.Permission.Should().Be(Permission.DevisCreer);
        proposals.Select(p => p.Subject).Should().Equal($"{lastDay.Id.Value}:{Today.AddDays(1):yyyy-MM-dd}", $"{soon.Id.Value}:{Today.AddDays(3):yyyy-MM-dd}");
        DecisionProposal first = proposals[0];
        (first.Kind, first.Audience, first.Urgency, first.Impact, first.DueOn, first.Link).Should().Be(
            (QuoteFollowUpDecisionRule.RuleKind, DecisionAudience.Entreprise, DecisionUrgency.High, 1_500m, Today.AddDays(1), "quote-follow-ups"));
        first.Facts.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["quote"] = lastDay.Number!,
            ["customer"] = "Boulangerie Martin",
            ["validUntil"] = $"{Today.AddDays(1):yyyy-MM-dd}",
        });
        first.Action.Should().Be(new DecisionAction(SendQuoteFollowUpDecisionAction.ActionKindName, lastDay.Id.Value.ToString()));
        (proposals[1].Urgency, proposals[1].Action).Should().Be((DecisionUrgency.Normal, null));
    }

    [Fact]
    public async Task Sans_serveur_d_envoi_la_decision_mene_a_l_ecran()
    {
        AddQuote("Boulangerie Martin", 27, 30);
        _email.IsConfigured = false;

        (await FollowUpRule.ProposeAsync(Today, default)).Should().ContainSingle().Which.Action.Should().BeNull();
    }

    [Fact]
    public async Task Accepter_la_decision_envoie_la_relance_proposee()
    {
        Quote quote = AddQuote("Boulangerie Martin", 27, 30);

        Result sent = await FollowUpAction.ExecuteAsync(Dossier, quote.Id.Value.ToString(), default);

        sent.IsSuccess.Should().BeTrue();
        FollowUpAction.Reversible.Should().BeFalse();
        _email.Sent.Should().ContainSingle().Which.To.Value.Should().Be("contact@client.fr");
        Store.FollowUps.Should().ContainSingle().Which.Channel.Should().Be(QuoteFollowUpChannel.Email);
        (await FollowUpRule.ProposeAsync(Today, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task L_action_refuse_une_charge_illisible_et_remonte_le_refus_de_l_envoi()
    {
        Quote withoutEmail = AddQuote("Garage Dupont", 27, 30, email: null);

        (await FollowUpAction.ExecuteAsync(Dossier, "pas-un-devis", default)).Errors.Single().Should().Be(DecisionErrors.ActionUnavailable);
        (await FollowUpAction.ExecuteAsync(Dossier, withoutEmail.Id.Value.ToString(), default)).Errors.Single().Code.Should().Be(QuoteFollowUpErrors.CustomerEmailMissingCode);
    }

    [Fact]
    public void La_regle_refuse_un_constat_absent() =>
        FluentActions.Invoking(() => QuoteFollowUpDecisionRule.Proposals(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Le_service_refuse_des_reglages_absents()
    {
        QuoteFollowUpService service = _scope.ServiceProvider.GetRequiredService<QuoteFollowUpService>();

        FluentActions.Invoking(() => service.Describe(null!)).Should().Throw<ArgumentNullException>();
        service.Today.Should().Be(Today);
    }
}
