using Lettra.Application;
using Lettra.Application.Abstractions.External;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.External;
using Lettra.Infrastructure.External.Payees;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.External.Tests;

/// <summary>Banque simulée de la vérification du bénéficiaire : trois comptes de démonstration, et « correspond » pour les autres.</summary>
public sealed class PayeeVerificationTests
{
    private readonly SimulatedPayeeVerification _bank = new();

    private Task<Result<PayeeVerificationResult>> Verify(string iban, string name) => _bank.VerifyAsync(Iban.Create(iban).Value, name, default);

    [Fact]
    public async Task Un_compte_inconnu_du_registre_est_repute_au_nom_demande() =>
        (await Verify("FR7610107001011234567890129", "Boulangerie Martin")).Value.Should().Be(new PayeeVerificationResult(PayeeMatch.Match));

    [Fact]
    public async Task Le_compte_allemand_de_demonstration_n_est_pas_au_nom_du_fournisseur() =>
        (await Verify("DE89370400440532013000", "Assurance de la Vallée SA")).Value.Should().Be(new PayeeVerificationResult(PayeeMatch.NoMatch));

    [Fact]
    public async Task Un_nom_a_une_lettre_pres_correspond_presque_et_la_banque_donne_le_titulaire()
    {
        (await Verify("FR7630006000011234567890189", "Minoterie Dupuis")).Value.Should().Be(new PayeeVerificationResult(PayeeMatch.CloseMatch, "Minoterie Dupuy"));
        (await Verify("FR7630006000011234567890189", "Minoterie Dupuy SARL")).Value.Should().Be(new PayeeVerificationResult(PayeeMatch.Match));
    }

    [Fact]
    public async Task Le_compte_d_une_banque_qui_ne_repond_pas_est_impossible_a_verifier() =>
        (await Verify("FR1420041010050500013M02606", "Expert-comptable du Lac SAS")).Value.Result.Should().Be(PayeeMatch.NotPossible);

    [Fact]
    public void Les_services_externes_branchent_la_banque_simulee_meme_annuaires_coupes()
    {
        ServiceCollection services = new();
        services.AddApplication();
        services.AddExternalServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:External:Enabled"] = "false" }).Build());

        services.BuildServiceProvider().GetRequiredService<IPayeeVerification>().Should().BeOfType<SimulatedPayeeVerification>();
    }
}
