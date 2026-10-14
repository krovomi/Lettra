using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

internal sealed class FakeCustomersGateway : ICustomersGateway
{
    public List<CustomerSummary> Customers { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public CreateCustomerRequest? LastRequest { get; private set; }

    public Task<OperationResult<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (NextErrors is not null)
        {
            return Task.FromResult(OperationResult<CustomerResponse>.Failure(NextErrors));
        }

        CustomerResponse created = new(Guid.CreateVersion7(), request.Kind, request.Name, "732829320", request.Siret, null, null,
            request.AddressLine1, null, request.PostalCode, request.City, request.CountryCode, DateTimeOffset.UnixEpoch);
        Customers.Add(new CustomerSummary(created.Id, created.Kind, created.Name, created.Siren, created.City));
        return Task.FromResult(OperationResult<CustomerResponse>.Success(created));
    }

    public Task<OperationResult<PagedResponse<CustomerSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<PagedResponse<CustomerSummary>>.Success(new PagedResponse<CustomerSummary>([.. Customers], page, pageSize, Customers.Count)));
}

/// <summary>Les tests changent la culture du thread : on les sérialise.</summary>
[CollectionDefinition("culture", DisableParallelization = true)]
public sealed class CultureCollection;

[Collection("culture")]
public sealed class CustomersPageTests : BunitContext
{
    private readonly FakeCustomersGateway _gateway = new();

    public CustomersPageTests()
    {
        Services.AddLettraUi();
        Services.AddSingleton<ICustomersGateway>(_gateway);
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private static void Fill(IRenderedComponent<Customers> page, string id, string value) => page.Find($"#{id}").Change(value);

    [Fact]
    public void L_ecran_est_en_francais_par_defaut()
    {
        UseCulture(LettraCultures.Resolve("de-DE").Name);

        IRenderedComponent<Customers> page = Render<Customers>();

        page.Find("h1").TextContent.Should().Be("Clients");
        page.Find("button[type=submit]").TextContent.Should().Be("Créer le client");
        page.Markup.Should().Contain("Aucun client pour l'instant.");
    }

    [Fact]
    public void L_ecran_est_traduit_en_anglais()
    {
        UseCulture("en-US");

        IRenderedComponent<Customers> page = Render<Customers>();

        page.Find("button[type=submit]").TextContent.Should().Be("Create customer");
        page.Find("label[for=siret]").TextContent.Should().Be("SIRET");
        page.Find("label[for=zip]").TextContent.Should().Be("Postal code");
    }

    [Fact]
    public void La_validation_immediate_affiche_des_messages_traduits_sans_appeler_le_serveur()
    {
        UseCulture("fr-FR");
        IRenderedComponent<Customers> page = Render<Customers>();

        page.Find("form").Submit();

        _gateway.LastRequest.Should().BeNull();
        page.FindAll(".validation-message").Select(e => e.TextContent)
            .Should().Contain("Le nom du client est obligatoire.")
            .And.Contain("L'adresse est obligatoire.")
            .And.NotContain(m => m.Contains("Customer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Creer_un_client_l_ajoute_a_la_liste()
    {
        UseCulture("fr-FR");
        IRenderedComponent<Customers> page = Render<Customers>();

        Fill(page, "name", "Boulangerie Martin");
        Fill(page, "siret", "73282932000074");
        Fill(page, "line1", "1 rue de la Paix");
        Fill(page, "zip", "75002");
        Fill(page, "city", "Paris");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Be("Client « Boulangerie Martin » créé."));
        page.Find("tbody").TextContent.Should().Contain("Boulangerie Martin").And.Contain("732829320");
    }

    [Fact]
    public void Les_erreurs_du_serveur_s_affichent_sous_le_bon_champ_dans_la_langue_de_l_utilisateur()
    {
        UseCulture("en-US");
        _gateway.NextErrors = [new ErrorDetail(ErrorCodes.Customer.SirenRequired, "Siren"), new ErrorDetail(ErrorCodes.Security.Forbidden)];
        IRenderedComponent<Customers> page = Render<Customers>();

        Fill(page, "name", "Acme");
        Fill(page, "line1", "1 Main Street");
        Fill(page, "zip", "75002");
        Fill(page, "city", "Paris");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Markup.Should().Contain("A French company must have a SIREN (or a SIRET)."));
        page.Find("#siren ~ .validation-message").TextContent.Should().Be("A French company must have a SIREN (or a SIRET).");
        page.Markup.Should().Contain("You do not have permission for this action in this file.");
    }
}
