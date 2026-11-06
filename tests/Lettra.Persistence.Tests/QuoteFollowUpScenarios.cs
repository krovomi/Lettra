using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Devis à relancer et carnet de commandes sur les trois bases : devis conclus (jour de réponse gardé), devis qui
/// approchent de leur fin, relance par e-mail, notée ou en échec, historique filtré et trié, fenêtre relue dans une
/// nouvelle session, carnet pondéré par les taux passés.
/// </summary>
internal static class QuoteFollowUpScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly Customer.Draft Berlin = new(
        CustomerKind.Company, "Berlin Beratung GmbH", null, null, "DE123456789", "buchhaltung@berlin.example",
        "Unter den Linden 1", null, "10117", "Berlin", "DE", PreferredLanguage: "en-US");

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Individual, "Claire Martin", null, null, null, null, "8 rue Neuve", null, "75011", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: null, Bic: null);

    public static TenantHarness Harness(IDbProviderConfigurator configurator, ManualClock clock, CapturedEmails emails) =>
        new(configurator, clock, configure: s => s.AddSingleton<IEmailSender>(emails));

    public static async Task FollowUpsAsync(TenantHarness harness, ManualClock clock, CapturedEmails emails, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        DateOnly start = LegalCalendar.DateOf(clock.GetUtcNow());
        Guid durand, berlin, martin;
        QuoteId durandOpen, berlinOpen, martinOpen;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Ok(await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default));
            durand = Ok(await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value;
            berlin = Ok(await commands.SendAsync(new CreateCustomerCommand(dossier, Berlin), default)).Value;
            martin = Ok(await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value;

            // Historique : Durand a accepté deux devis et en a refusé un, le jour même.
            foreach (bool accepted in new[] { true, true, false })
            {
                QuoteId closed = await ValidatedQuoteAsync(commands, dossier, durand, start.AddDays(30), 1_500m);
                Ok(await commands.SendAsync(new SetQuoteOutcomeCommand(dossier, closed, accepted, null), default));
            }

            durandOpen = await ValidatedQuoteAsync(commands, dossier, durand, start.AddDays(30), 2_000m);
            berlinOpen = await ValidatedQuoteAsync(commands, dossier, berlin, start.AddDays(30), 3_000m);
            martinOpen = await ValidatedQuoteAsync(commands, dossier, martin, start.AddDays(40), 800m);
        }

        // 27 jours plus tard : les devis de Durand et Berlin finissent dans 3 jours, celui de Martin dans 13.
        clock.Now = clock.Now.AddDays(27);
        DateOnly today = start.AddDays(27);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            QuoteFollowUpOverviewResponse overview = Ok(await queries.QueryAsync(new GetQuoteFollowUpOverviewQuery(dossier), default));
            overview.Quotes.Select(q => q.QuoteId).Should().Equal(berlinOpen.Value, durandOpen.Value);
            overview.Quotes.Should().OnlyContain(q => q.Due && q.DaysLeft == 3 && q.DaysWithoutAnswer == 27);
            (overview.DueCount, overview.DueExclTax).Should().Be((2, 5_000m));
            QuoteToFollowUpResponse berlinRow = overview.Quotes.Single(q => q.CustomerId == berlin);
            (berlinRow.CustomerEmail, berlinRow.Language).Should().Be(("buchhaltung@berlin.example", "en-US"));

            QuoteFollowUpPreviewResponse preview = Ok(await queries.QueryAsync(new GetQuoteFollowUpPreviewQuery(dossier, berlinOpen), default));
            preview.Subject.Should().StartWith("Your quote");

            // Durand par e-mail ; Martin, sans adresse, noté à la main ; Berlin refusé par le serveur d'envoi.
            QuoteFollowUpResponse sent = Ok(await commands.SendAsync(new SendQuoteFollowUpCommand(dossier, durandOpen, new SendQuoteFollowUpRequest()), default));
            (sent.Outcome, sent.Recipient, sent.CustomerName).Should().Be((QuoteFollowUpOutcomeDto.Sent, "compta@durand.example", "Atelier Durand"));
            emails.Sent.Should().ContainSingle().Which.Should().Match<EmailMessage>(m => m.To.Value == "compta@durand.example" && m.SenderName == "Conseil Fictif");
            (await commands.SendAsync(new SendQuoteFollowUpCommand(dossier, martinOpen, new SendQuoteFollowUpRequest()), default))
                .Errors.Should().ContainSingle().Which.Code.Should().Be(QuoteFollowUpErrors.CustomerEmailMissingCode);
            Ok(await commands.SendAsync(new SendQuoteFollowUpCommand(dossier, martinOpen,
                new SendQuoteFollowUpRequest { Channel = QuoteFollowUpChannelDto.Manual, Subject = "Appel", Body = "Rappelée au téléphone." }), default));
            emails.Fail = true;
            (await commands.SendAsync(new SendQuoteFollowUpCommand(dossier, berlinOpen, new SendQuoteFollowUpRequest()), default))
                .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Email.SendFailed);
            emails.Fail = false;
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            QuoteFollowUpOverviewResponse after = Ok(await queries.QueryAsync(new GetQuoteFollowUpOverviewQuery(dossier), default));
            after.Quotes.Single(q => q.QuoteId == durandOpen.Value).Should().Match<QuoteToFollowUpResponse>(q =>
                !q.Due && q.FollowUpCount == 1 && q.LastChannel == QuoteFollowUpChannelDto.Email);
            after.Quotes.Single(q => q.QuoteId == berlinOpen.Value).Should().Match<QuoteToFollowUpResponse>(q => q.Due && q.FollowUpCount == 0);

            PagedResponse<QuoteFollowUpResponse> history = Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier), default));
            history.TotalCount.Should().Be(3);
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Outcome: QuoteFollowUpOutcomeDto.Failed), default))
                .Items.Should().ContainSingle().Which.Should().Match<QuoteFollowUpResponse>(f => f.ErrorCode == ErrorCodes.Email.SendFailed && f.Language == "en-US");
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Channel: QuoteFollowUpChannelDto.Manual), default))
                .Items.Should().ContainSingle().Which.Recipient.Should().BeNull();
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Search: "atelier dUrand"), default)).Items.Should().ContainSingle();
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Search: "téléphone"), default)).Items.Should().BeEmpty("la recherche porte sur l'objet, pas le texte");
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Search: "berlin.example"), default)).Items.Should().ContainSingle();
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, QuoteId: durandOpen.Value, CustomerId: durand), default)).TotalCount.Should().Be(1);
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, From: today, To: today), default)).TotalCount.Should().Be(3);
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, From: today.AddDays(1)), default)).TotalCount.Should().Be(0);
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, To: today.AddDays(-1)), default)).TotalCount.Should().Be(0);
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Sort: QuoteFollowUpSortDto.Customer, Descending: false), default))
                .Items.Select(f => f.CustomerName).Should().Equal("Atelier Durand", "Berlin Beratung GmbH", "Claire Martin");
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Sort: QuoteFollowUpSortDto.Amount), default))
                .Items.Select(f => f.AmountExclTax).Should().Equal(3_000m, 2_000m, 800m);
            Ok(await queries.QueryAsync(new ListQuoteFollowUpsQuery(dossier, Sort: QuoteFollowUpSortDto.Quote, Descending: false, PageSize: 2), default))
                .Should().Match<PagedResponse<QuoteFollowUpResponse>>(p => p.Items.Count == 2 && p.TotalCount == 3);

            // La fenêtre élargie à 15 jours fait entrer le devis de Martin, déjà relancé.
            Ok(await commands.SendAsync(new UpdateQuoteFollowUpSettingsCommand(dossier, new UpdateQuoteFollowUpSettingsRequest { WindowDays = 15 }), default))
                .Version.Should().Be(1);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            QuoteFollowUpOverviewResponse widened = Ok(await queries.QueryAsync(new GetQuoteFollowUpOverviewQuery(dossier), default));
            widened.Settings.WindowDays.Should().Be(15);
            widened.Quotes.Single(q => q.QuoteId == martinOpen.Value).Should().Match<QuoteToFollowUpResponse>(q =>
                !q.Due && q.CustomerEmail == null && q.LastChannel == QuoteFollowUpChannelDto.Manual);

            OrderBookResponse book = Ok(await queries.QueryAsync(new GetOrderBookQuery(dossier), default));
            (book.Accepted, book.Closed, book.HasEnoughHistory, book.GlobalRate).Should().Be((2, 3, true, 0.6667m));
            book.Quotes.Select(q => q.QuoteId).Should().BeEquivalentTo([durandOpen.Value, berlinOpen.Value, martinOpen.Value]);
            OrderBookQuoteResponse durandQuote = book.Quotes.Single(q => q.QuoteId == durandOpen.Value);
            durandQuote.Customer.Should().Be(new CriterionRateDto(2, 3, 0.6667m, false));
            durandQuote.Age.UsesGlobalRate.Should().BeTrue("les devis conclus l'ont été le jour même, aucun n'est resté 16 jours sans réponse");
            book.OpenExclTax.Should().Be(5_800m);

            // Le jour de la réponse est gardé ; un devis répondu avant qu'il le soit prend sa dernière modification.
            IQuotePipelineReader pipeline = scope.ServiceProvider.GetRequiredService<IQuotePipelineReader>();
            (await pipeline.ListAsync(start, default)).Where(q => q.Status is QuoteStatus.Accepted or QuoteStatus.Refused)
                .Should().HaveCount(3).And.OnlyContain(q => q.AnsweredOn == start);
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            await db.Set<Quote>().ExecuteUpdateAsync(u => u.SetProperty(q => q.RespondedOn, (DateOnly?)null));
            (await pipeline.ListAsync(start, default)).Where(q => q.Status is QuoteStatus.Accepted or QuoteStatus.Refused)
                .Should().HaveCount(3).And.OnlyContain(q => q.AnsweredOn == start);
            (await pipeline.ContactsAsync([new CustomerId(berlin), new CustomerId(martin)], default)).Should().BeEquivalentTo(
                new Dictionary<CustomerId, QuoteCustomerContact>
                {
                    [new CustomerId(berlin)] = new("buchhaltung@berlin.example", "en-US"),
                    [new CustomerId(martin)] = new(null, null),
                });

            // Un client opposé au profilage (noté depuis les relances) sort du carnet : ni taux, ni pondération.
            (await pipeline.ObjectingCustomersAsync(default)).Should().BeEmpty();
            db.Set<PaymentTargetingObjection>().Add(PaymentTargetingObjection.Record(new CustomerId(durand), Guid.CreateVersion7(), clock.GetUtcNow()));
            await db.SaveChangesAsync();
            (await pipeline.ObjectingCustomersAsync(default)).Should().Equal(new CustomerId(durand));
            OrderBookResponse withObjection = Ok(await queries.QueryAsync(new GetOrderBookQuery(dossier), default));
            withObjection.Quotes.Select(q => q.QuoteId).Should().BeEquivalentTo([berlinOpen.Value, martinOpen.Value]);
            withObjection.ExcludedCount.Should().Be(1);
            Ok(await queries.QueryAsync(new GetQuoteFollowUpOverviewQuery(dossier), default))
                .Quotes.Single(q => q.QuoteId == durandOpen.Value).Probability.Should().BeNull();
        }
    }

    private static async Task<QuoteId> ValidatedQuoteAsync(ICommandDispatcher commands, DossierId dossier, Guid customer, DateOnly validUntil, decimal price)
    {
        SaveQuoteRequest request = new()
        {
            CustomerId = customer,
            ValidUntil = validUntil,
            Subject = "Accompagnement",
            Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" }],
        };
        QuoteId quote = Ok(await commands.SendAsync(new CreateQuoteCommand(dossier, request), default));
        Ok(await commands.SendAsync(new ValidateQuoteCommand(dossier, quote, null), default));
        return quote;
    }

    private static T Ok<T>(Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Value;
    }

    private static void Ok(Result result) => result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
}
