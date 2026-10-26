using Lettra.Contracts.Banking;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Exports.Tests.Banking;

/// <summary>Agrégateur bancaire simulé : consentement, opérations stables d'une lecture à l'autre, choix par la configuration.</summary>
public sealed class SimulatedBankAggregatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly CustomerId Martin = new(Guid.CreateVersion7());

    private static ReceivableDocument Invoice(int i, decimal paid = 0m, decimal credited = 0m, InvoiceKind kind = InvoiceKind.Invoice) => new(
        new InvoiceId(Guid.CreateVersion7()), kind, $"FAC-{i:D6}", Martin, "Boulangerie Martin", Today.AddDays(-40), Today.AddDays(-10),
        100m * i, credited, paid, null);

    private static SimulatedBankAggregator Aggregator(params ReceivableDocument[] documents) => new(new Documents(documents), new Clock(Now));

    [Fact]
    public async Task Le_consentement_donne_un_compte_fictif_valable_180_jours()
    {
        SimulatedBankAggregator aggregator = Aggregator();
        Guid connection = Guid.CreateVersion7();

        BankConsentStart start = await aggregator.StartConsentAsync(SimulatedBankAggregator.Institutions[0], connection, default);
        start.RedirectUrl.Should().Be($"bank/simulated-consent?connection={connection}&bank=Banque%20Lumi%C3%A8re%20%28simul%C3%A9e%29");

        BankConsentGrant grant = (await aggregator.CompleteConsentAsync(start.ProviderReference, BankConnectionRoutes.SimulatedGrantedCode, default)).Value;
        grant.ExpiresAt.Should().Be(Now.AddDays(SimulatedBankAggregator.ConsentDays));
        grant.AccountIban.Should().MatchRegex("^FR[0-9]{2}99999[0-9]{18}$").And.Be(SimulatedBankAggregator.AccountIban(start.ProviderReference));
        IbanIsValid(grant.AccountIban).Should().BeTrue("clé de contrôle juste, comme un vrai IBAN");

        (await aggregator.CompleteConsentAsync(start.ProviderReference, BankConnectionRoutes.SimulatedRefusedCode, default))
            .Errors[0].Code.Should().Be(BankConnectionErrors.ConsentRefusedCode);
        (await aggregator.CompleteConsentAsync("autre-agregateur", BankConnectionRoutes.SimulatedGrantedCode, default))
            .Errors[0].Code.Should().Be(BankConnectionErrors.ConsentRefusedCode);
        (await aggregator.ListInstitutionsAsync(default)).Should().HaveCount(3).And.OnlyContain(i => i.Name.Contains("simul", StringComparison.Ordinal));
        aggregator.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Les_operations_sont_les_memes_d_une_lecture_a_l_autre()
    {
        ReceivableDocument[] invoices = [.. Enumerable.Range(1, 30).Select(i => Invoice(i))];
        SimulatedBankAggregator aggregator = Aggregator(invoices);

        ParsedBankStatement first = (await aggregator.FetchAsync("sim-compte", Today.AddDays(-90), Today, default)).Value;
        ParsedBankStatement second = (await Aggregator(invoices).FetchAsync("sim-compte", Today.AddDays(-90), Today.AddDays(5), default)).Value;

        first.Format.Should().Be(BankStatementFormat.Connection);
        first.To.Should().Be(Today);
        second.Transactions.Should().Equal(first.Transactions, "même compte, mêmes dates et références : l'import écarte les doublons");
        List<BankTransaction> credits = [.. first.Transactions.Where(t => t.Amount > 0m)];
        credits.Count.Should().BeInRange(10, 29, "environ deux factures sur trois sont payées");
        credits.Should().OnlyContain(t => t.BookingDate > Today.AddDays(-40) && t.BookingDate <= Today && t.CounterpartyName == "Boulangerie Martin");
        credits.Should().OnlyContain(t => invoices.Any(d => d.TotalInclTax == t.Amount));
        credits.Select(t => t.BankReference).Should().OnlyHaveUniqueItems();
        credits.Should().Contain(t => t.Reference != null).And.Contain(t => t.Reference == null, "une sur deux cite le numéro de facture");
        first.Transactions.Where(t => t.Amount < 0m).Should().OnlyContain(t => t.Amount == -SimulatedBankAggregator.MonthlyFee && t.BookingDate.Day == 5)
            .And.HaveCount(3, "frais d'août, de septembre et d'octobre");
    }

    [Fact]
    public async Task Une_facture_reglee_creditee_ou_un_avoir_n_est_pas_payee_et_la_periode_est_respectee()
    {
        SimulatedBankAggregator aggregator = Aggregator(
            Invoice(1, paid: 10m), Invoice(2, credited: 20m), Invoice(3, kind: InvoiceKind.CreditNote));

        ParsedBankStatement statement = (await aggregator.FetchAsync("sim-compte", Today.AddDays(-25), Today, default)).Value;

        statement.Transactions.Should().ContainSingle("seuls les frais d'octobre tombent dans la période")
            .Which.Should().BeEquivalentTo(new { BookingDate = new DateOnly(2026, 10, 5), Amount = -9.90m, BankReference = "SIMFRAIS202610" });
    }

    [Fact]
    public async Task Sans_agregateur_rien_n_est_propose()
    {
        IBankAggregator none = Configured("None");

        none.IsAvailable.Should().BeFalse();
        (await none.ListInstitutionsAsync(default)).Should().BeEmpty();
        (await none.CompleteConsentAsync("x", "accorde", default)).Errors[0].Code.Should().Be(BankConnectionErrors.UnavailableCode);
        (await none.FetchAsync("x", Today, Today, default)).Errors[0].Code.Should().Be(BankConnectionErrors.UnavailableCode);
        await none.RevokeAsync("x", default);
        await FluentActions.Invoking(() => none.StartConsentAsync(SimulatedBankAggregator.Institutions[0], Guid.Empty, default)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void La_configuration_choisit_l_agregateur()
    {
        Configured(null).Should().BeOfType<SimulatedBankAggregator>();
        Configured("Simulated").Should().BeOfType<SimulatedBankAggregator>();
        FluentActions.Invoking(() => Configured("Bridge")).Should().Throw<InvalidOperationException>().WithMessage("*Bridge*");
    }

    private static IBankAggregator Configured(string? choice)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DependencyInjection.BankAggregatorKey] = choice }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddExports(configuration);
        services.AddSingleton<IReceivableDocumentReader>(new Documents([]));
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IBankAggregator>();
    }

    private static bool IbanIsValid(string iban)
    {
        string rearranged = iban[4..] + iban[..4];
        int remainder = 0;
        foreach (char c in rearranged)
        {
            string digits = char.IsAsciiLetter(c) ? (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : c.ToString();
            foreach (char d in digits)
            {
                remainder = ((remainder * 10) + (d - '0')) % 97;
            }
        }

        return remainder == 1;
    }

    private sealed class Documents(IReadOnlyList<ReceivableDocument> documents) : IReceivableDocumentReader
    {
        public Task<IReadOnlyList<ReceivableDocument>> ListAsync(CustomerId? customer, CancellationToken cancellationToken) => Task.FromResult(documents);

        public Task<IReadOnlyDictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>>> PendingVatAsync(
            IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>>>(new Dictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>>());
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
