using Lettra.Domain.Billing;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Exports.Tests.Portal;

/// <summary>Jeton des liens d'espace client, prestataires de paiement et de signature simulés, choix par la configuration.</summary>
public sealed class PortalProvidersTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private static CheckoutRequest Request() =>
        new(Dossier, Guid.CreateVersion7(), new InvoiceId(Guid.CreateVersion7()), "FAC-000001", 120m, "EUR", "Conseil Lettra", "https://retour");

    [Fact]
    public void Le_jeton_porte_le_dossier_et_seule_l_empreinte_du_secret_est_gardee()
    {
        (string token, string hash) = PortalToken.Create(Dossier);
        (string other, string otherHash) = PortalToken.Create(Dossier);

        token.Should().HaveLength(64).And.MatchRegex("^[A-Za-z0-9_-]+$", "base64url, sans remplissage, lisible dans une adresse");
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        PortalToken.Read(token).Should().Be((Dossier, hash));
        other.Should().NotBe(token);
        otherHash.Should().NotBe(hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas-un-jeton")]
    [InlineData("illisible")]
    [InlineData("jeton avec des espaces et des accents é")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Un_jeton_mal_forme_ou_sans_dossier_ne_se_lit_pas(string? token)
    {
        PortalToken.Read(token).Should().BeNull();
        PortalToken.Read(new string('a', 129)).Should().BeNull();
    }

    [Fact]
    public async Task Un_paiement_simule_accepte_se_reserve_une_seule_fois()
    {
        SimulatedPaymentProvider payments = new(new Clock(DateTimeOffset.UtcNow));
        Checkout checkout = (await payments.CreateCheckoutAsync(Request(), default)).Value;
        checkout.State.Should().Be(CheckoutState.Pending);
        checkout.RedirectUrl.Should().Be(SimulatedPaymentProvider.PagePath + checkout.Id);
        (await payments.TryClaimAsync(checkout.Id, default)).Should().BeFalse("rien n'est encore payé");

        payments.Pay(checkout.Id, "4242 4242 4242 4242").Should().Be(CheckoutState.Paid);

        Checkout paid = (await payments.FindAsync(checkout.Id, default))!;
        paid.State.Should().Be(CheckoutState.Paid);
        paid.ProviderReference.Should().StartWith("pi_sim_");
        payments.Pay(checkout.Id, SimulatedPaymentProvider.DeclinedCard).Should().BeNull("un paiement traité n'attend plus de carte");
        (await payments.TryClaimAsync(checkout.Id, default)).Should().BeTrue();
        (await payments.TryClaimAsync(checkout.Id, default)).Should().BeFalse();
        (await payments.FindAsync(checkout.Id, default))!.State.Should().Be(CheckoutState.Recorded);

        await payments.ReleaseAsync(checkout.Id, default);
        (await payments.FindAsync(checkout.Id, default))!.State.Should().Be(CheckoutState.Paid, "un encaissement en échec se reprend au retour suivant");
    }

    [Theory]
    [InlineData(SimulatedPaymentProvider.DeclinedCard)]
    [InlineData("4000 0000 0000 0002")]
    [InlineData("1234")]
    [InlineData(null)]
    public async Task La_carte_de_test_refusee_et_un_numero_impossible_sont_refuses(string? card)
    {
        SimulatedPaymentProvider payments = new(TimeProvider.System);
        Checkout checkout = (await payments.CreateCheckoutAsync(Request(), default)).Value;

        payments.Pay(checkout.Id, card).Should().Be(CheckoutState.Declined);
        (await payments.FindAsync(checkout.Id, default))!.State.Should().Be(CheckoutState.Pending, "le client peut réessayer");
    }

    [Fact]
    public async Task Apres_trois_refus_la_session_de_paiement_se_ferme()
    {
        SimulatedPaymentProvider payments = new(TimeProvider.System);
        Checkout checkout = (await payments.CreateCheckoutAsync(Request(), default)).Value;

        for (int i = 0; i < SimulatedPaymentProvider.MaxDeclines; i++)
        {
            payments.Pay(checkout.Id, SimulatedPaymentProvider.DeclinedCard).Should().Be(CheckoutState.Declined);
        }

        (await payments.FindAsync(checkout.Id, default))!.State.Should().Be(CheckoutState.Declined);
        payments.Pay(checkout.Id, "4242424242424242").Should().BeNull();
    }

    [Fact]
    public async Task Un_paiement_inconnu_ou_expire_n_existe_plus()
    {
        Clock clock = new(DateTimeOffset.UtcNow);
        SimulatedPaymentProvider payments = new(clock);
        Checkout checkout = (await payments.CreateCheckoutAsync(Request(), default)).Value;

        payments.Pay("cs_sim_inconnu", "4242424242424242").Should().BeNull();
        (await payments.FindAsync(null!, default)).Should().BeNull();

        clock.Advance(TimeSpan.FromHours(2));
        (await payments.FindAsync(checkout.Id, default)).Should().BeNull("la session de paiement a expiré");
    }

    [Fact]
    public async Task La_signature_simulee_est_horodatee_et_referencee()
    {
        DateTimeOffset now = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
        SimulatedSignatureProvider signer = new(new Clock(now));

        SignatureEvidence evidence = (await signer.SignAsync(new SignatureRequest("DEV-000001", new string('a', 64), "Marie Martin"), default)).Value;

        evidence.Should().BeEquivalentTo(new { Provider = SimulatedSignatureProvider.ProviderName, SignedAt = now });
        evidence.Reference.Should().StartWith("sig_sim_");
    }

    [Fact]
    public async Task Sans_prestataire_le_paiement_en_ligne_est_ferme()
    {
        IOnlinePaymentProvider none = Provider("None");

        none.IsAvailable.Should().BeFalse();
        (await none.CreateCheckoutAsync(Request(), default)).Errors[0].Code.Should().Be(PortalErrors.PaymentDeclinedCode);
        (await none.FindAsync("x", default)).Should().BeNull();
        (await none.TryClaimAsync("x", default)).Should().BeFalse();
        await none.ReleaseAsync("x", default);
    }

    [Fact]
    public void Le_paiement_simule_est_le_choix_par_defaut_et_un_choix_inconnu_est_refuse()
    {
        Provider(null).Should().BeOfType<SimulatedPaymentProvider>().Which.IsAvailable.Should().BeTrue();
        FluentActions.Invoking(() => Provider("Stripe")).Should().Throw<InvalidOperationException>().WithMessage("*Stripe*");
    }

    private static IOnlinePaymentProvider Provider(string? choice)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DependencyInjection.OnlinePaymentKey] = choice }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddExports(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOnlinePaymentProvider>();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan delay) => _now += delay;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
