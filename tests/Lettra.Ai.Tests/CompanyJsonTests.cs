using System.Text.Json.Nodes;
using Lettra.Ai.Privacy;
using Lettra.Ai.Tools;
using Lettra.Ai.Tools.Company;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Ai.Tests;

/// <summary>company.json virtuel (annexe B §3.2) : construit depuis les paramètres du dossier, au format du skill comptable.</summary>
public sealed class CompanyJsonTests
{
    private static readonly CompanyFacts WebCraft = new()
    {
        Name = "WebCraft",
        LegalForm = "SAS",
        ShareCapital = 1000m,
        Siren = "912345678",
        Siret = "91234567800014",
        Rcs = "RCS Lyon",
        VatNumber = "FR40912345678",
        AddressLine1 = "12 rue du Commerce",
        PostalCode = "69003",
        City = "Lyon",
        CountryCode = "FR",
        VatRegime = "reel_normal",
        IncomeTaxRegime = "IS",
        VatOnDebits = true,
        PaymentDays = 30,
        EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
        Iban = "FR7630006000011234567890189",
        Bic = "AGRIFRPP",
        InvoicePrefix = "FAC-",
        QuotePrefix = "DEV-",
    };

    [Fact]
    public void Sans_parametres_renseignes_l_assistant_sait_qu_il_doit_les_demander()
    {
        JsonObject json = CompanyJsonBuilder.Build(null);

        json.Should().ContainSingle();
        json["_note"]!.GetValue<string>().Should().Be(CompanyJsonBuilder.NotFilledNote);
    }

    [Fact]
    public void La_fiche_complete_donne_les_champs_attendus_par_le_skill_comptable()
    {
        JsonObject json = CompanyJsonBuilder.Build(WebCraft);

        json["name"]!.GetValue<string>().Should().Be("WebCraft");
        json["legal_form"]!.GetValue<string>().Should().Be("SAS");
        json["capital"]!.GetValue<decimal>().Should().Be(1000m);
        json["address"]!.GetValue<string>().Should().Be("12 rue du Commerce, 69003 Lyon");
        json["siren"]!.GetValue<string>().Should().Be("912345678");
        json["siret"]!.GetValue<string>().Should().Be("91234567800014");
        json["rcs"]!.GetValue<string>().Should().Be("RCS Lyon");
        json["tva_intracom"]!.GetValue<string>().Should().Be("FR40912345678");
        json["tax"]!["regime_tva"]!.GetValue<string>().Should().Be("reel_normal");
        json["tax"]!["regime_is"]!.GetValue<string>().Should().Be("IS");
        json["tax"]!["option_debits"]!.GetValue<bool>().Should().BeTrue();
        json["payment"]!["default_terms"]!.GetValue<string>().Should().Be("net_30");
        json["payment"]!["default_terms_label"]!.GetValue<string>().Should().Be("30 jours date de facture");
        json["payment"]!["late_penalty_rate"]!.GetValue<string>().Should().Be("3x_legal");
        json["payment"]!["recovery_fee"]!.GetValue<decimal>().Should().Be(40m);
        json["payment"]!["escompte_label"]!.GetValue<string>().Should().Be("Pas d'escompte pour paiement anticipé");
        json["payment"]!["bank_details"]!["iban"]!.GetValue<string>().Should().Be("FR7630006000011234567890189");
        json["invoicing"]!["prefix"]!.GetValue<string>().Should().Be("FAC-");
        json["invoicing"]!["devis_prefix"]!.GetValue<string>().Should().Be("DEV-");
        json.ContainsKey("_a_completer").Should().BeFalse();
        json.ContainsKey("naf").Should().BeFalse(because: "un champ inconnu n'est pas inventé");
    }

    [Fact]
    public void Franchise_en_base_et_taux_de_penalite_fixe()
    {
        JsonObject json = CompanyJsonBuilder.Build(WebCraft with { VatFranchise = true, VatNumber = null, VatRegime = null, LatePenaltyRate = 10m, PaymentDays = 0 });

        json["tax"]!["regime_tva"]!.GetValue<string>().Should().Be("franchise");
        json["payment"]!["late_penalty_rate"]!.GetValue<decimal>().Should().Be(10m);
        json["payment"]!["default_terms"]!.GetValue<string>().Should().Be("net_0");
        json["payment"]!["default_terms_label"]!.GetValue<string>().Should().Be("À réception");
        json.ContainsKey("_a_completer").Should().BeFalse(because: "en franchise, ni numéro de TVA ni régime réel ne sont exigés");
    }

    [Fact]
    public void Les_mentions_obligatoires_manquantes_sont_signalees_au_moteur()
    {
        JsonObject json = CompanyJsonBuilder.Build(new CompanyFacts { Name = "WebCraft" });

        json["_a_completer"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().Equal("legal_form", "siren", "address", "tva_intracom", "tax.regime_tva");
    }

    [Fact]
    public async Task Le_company_json_envoye_au_moteur_ne_contient_aucune_valeur_reelle_de_l_entreprise()
    {
        AiDossierContext context = new(new NoTiers(), new FixedFacts(WebCraft));
        DossierId dossier = AssistantHarness.Dossier;

        string companyJson = await context.GetCompanyJsonAsync(dossier, CancellationToken.None);
        PseudonymizationSession privacy = new(await context.GetKnownIdentitiesAsync(dossier, CancellationToken.None));
        string sent = privacy.PseudonymizeJson(companyJson);

        foreach (string real in new[] { "WebCraft", "912345678", "91234567800014", "FR40912345678", "FR7630006000011234567890189", "12 rue du Commerce" })
        {
            sent.Should().NotContain(real);
        }

        sent.Should().Contain("reel_normal").And.Contain("net_30", because: "les choix fiscaux et les délais restent lisibles");
        privacy.Restore(sent).Should().Contain("WebCraft");
    }

    private sealed class FixedFacts(CompanyFacts facts) : ICompanyFactsSource
    {
        public Task<CompanyFacts?> GetAsync(DossierId dossier, CancellationToken cancellationToken) => Task.FromResult<CompanyFacts?>(facts);
    }

    private sealed class NoTiers : IQueryDispatcher
    {
        public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
        {
            object result = query is ListCustomersQuery
                ? Result<PagedResponse<CustomerSummary>>.Success(new([], 1, 200, 0))
                : Result<PagedResponse<SupplierSummary>>.Success(new([], 1, 200, 0));
            return Task.FromResult((TResult)result);
        }
    }
}
