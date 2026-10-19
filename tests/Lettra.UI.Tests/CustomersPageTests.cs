using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Pages.Parties;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Consultation;
using Lettra.UI.Tests.Security;
using Lettra.UI.Shared.Typing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

internal sealed class FakeCustomersGateway : ICustomersGateway
{
    public List<CustomerSummary> Customers { get; } = [];

    /// <summary>Langue d'impression des clients qui en ont une.</summary>
    public Dictionary<Guid, string> Languages { get; } = [];

    /// <summary>Fiches complètes créées ou modifiées par les tests ; les autres sont déduites de la liste.</summary>
    public Dictionary<Guid, CustomerResponse> Records { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public CreateCustomerRequest? LastRequest { get; private set; }

    public List<(Guid Id, long ExpectedVersion, CreateCustomerRequest Request)> Updates { get; } = [];

    public List<string?> Searches { get; } = [];

    public Task<OperationResult<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (TakeErrors() is { } errors)
        {
            return Task.FromResult(OperationResult<CustomerResponse>.Failure(errors));
        }

        CustomerResponse created = new(Guid.CreateVersion7(), request.Kind, request.Name, "732829320", request.Siret, null, null,
            request.AddressLine1, null, request.PostalCode, request.City, request.CountryCode, DateTimeOffset.UnixEpoch)
        {
            PreferredLanguage = request.PreferredLanguage,
            Version = 1,
        };
        if (request.PreferredLanguage is { } language)
        {
            Languages[created.Id] = language;
        }

        Records[created.Id] = created;
        Customers.Add(new CustomerSummary(created.Id, created.Kind, created.Name, created.Siren, created.City));
        return Task.FromResult(OperationResult<CustomerResponse>.Success(created));
    }

    /// <summary>Comme l'API : une page au-delà de <see cref="CustomerListing.MaxPageSize"/> est refusée en entier.</summary>
    public Task<OperationResult<PagedResponse<CustomerSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(page < 1 || pageSize is < 1 or > CustomerListing.MaxPageSize
            ? OperationResult<PagedResponse<CustomerSummary>>.Failure([new ErrorDetail(ErrorCodes.Validation.Invalid, "PageSize")])
            : OperationResult<PagedResponse<CustomerSummary>>.Success(new PagedResponse<CustomerSummary>(
                [.. Customers.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, Customers.Count)));

    public List<PartyListQuery> Queries { get; } = [];

    public Task<OperationResult<PagedResponse<CustomerSummary>>> SearchAsync(PartyListQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        Searches.Add(query.Search);
        IEnumerable<CustomerSummary> found = Customers
            .Where(c => string.IsNullOrWhiteSpace(query.Search) || c.Name.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c => query.Kind is null || c.Kind == query.Kind);
        Func<CustomerSummary, string> key = query.Sort == PartySortDto.City ? c => c.City : c => c.Name;
        List<CustomerSummary> sorted = [.. query.Direction == SortDirectionDto.Descending
            ? found.OrderByDescending(key, StringComparer.Ordinal)
            : found.OrderBy(key, StringComparer.Ordinal)];
        List<CustomerSummary> items = sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Task.FromResult(OperationResult<PagedResponse<CustomerSummary>>.Success(new PagedResponse<CustomerSummary>(items, query.Page, query.PageSize, sorted.Count)));
    }

    public Task<OperationResult<CustomerResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        CustomerResponse? record = Find(id);
        return Task.FromResult(record is null
            ? OperationResult<CustomerResponse>.Failure(ErrorCodes.Customer.NotFound)
            : OperationResult<CustomerResponse>.Success(record));
    }

    public Task<OperationResult<CustomerResponse>> UpdateAsync(Guid id, long expectedVersion, CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        Updates.Add((id, expectedVersion, request with { }));
        if (TakeErrors() is { } errors)
        {
            return Task.FromResult(OperationResult<CustomerResponse>.Failure(errors));
        }

        CustomerResponse current = Find(id)!;
        if (current.Version != expectedVersion)
        {
            return Task.FromResult(OperationResult<CustomerResponse>.Stale(current));
        }

        CustomerResponse updated = current with
        {
            Kind = request.Kind,
            Name = request.Name,
            Siren = request.Siren,
            Siret = request.Siret,
            VatNumber = request.VatNumber,
            Email = request.Email,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            PostalCode = request.PostalCode,
            City = request.City,
            CountryCode = request.CountryCode,
            PreferredLanguage = request.PreferredLanguage,
            Version = current.Version + 1,
        };
        Records[id] = updated;
        int index = Customers.FindIndex(c => c.Id == id);
        Customers[index] = new CustomerSummary(id, updated.Kind, updated.Name, updated.Siren, updated.City);
        return Task.FromResult(OperationResult<CustomerResponse>.Success(updated));
    }

    public List<(Guid Id, long? ExpectedVersion)> Deletes { get; } = [];

    /// <summary>Comme le serveur : le client quitte la liste (sa fiche reste lisible, marquée supprimée).</summary>
    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        Deletes.Add((id, expectedVersion));
        if (TakeErrors() is { } errors)
        {
            return Task.FromResult(OperationResult<bool>.Failure(errors));
        }

        if (Find(id) is not { } current)
        {
            return Task.FromResult(OperationResult<bool>.Failure(ErrorCodes.Customer.NotFound));
        }

        Records[id] = current with { IsArchived = true, Version = current.Version + 1 };
        Customers.RemoveAll(c => c.Id == id);
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    /// <summary>Un collègue modifie la fiche pendant la saisie (ADR-0013) : la version du serveur avance.</summary>
    public void EditedByColleague(Guid id, Func<CustomerResponse, CustomerResponse> edit)
    {
        CustomerResponse current = Find(id)!;
        Records[id] = edit(current) with { Version = current.Version + 1 };
    }

    private CustomerResponse? Find(Guid id)
    {
        if (Records.TryGetValue(id, out CustomerResponse? record))
        {
            return record;
        }

        CustomerSummary? summary = Customers.Find(c => c.Id == id);
        return summary is null
            ? null
            : new CustomerResponse(summary.Id, summary.Kind, summary.Name, summary.Siren, null, null, null, "1 rue de la Paix", null, "69001", summary.City, "FR", DateTimeOffset.UnixEpoch)
            {
                PreferredLanguage = Languages.GetValueOrDefault(id),
                Version = 1,
            };
    }

    private IReadOnlyList<ErrorDetail>? TakeErrors()
    {
        IReadOnlyList<ErrorDetail>? errors = NextErrors;
        NextErrors = null;
        return errors;
    }
}

/// <summary>Les tests changent la culture du thread : on les sérialise.</summary>
[CollectionDefinition("culture", DisableParallelization = true)]
public sealed class CultureCollection;

[Collection("culture")]
public sealed class CustomersPageTests : BunitContext
{
    private readonly FakeCustomersGateway _gateway = new();
    private readonly FakeDirectoryGateway _directory = new();
    private readonly FakeAccountingGateway _accounting = new();
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeQuotesGateway _quotes = new();

    public CustomersPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        ExplorerSettings.SearchDelay = TimeSpan.FromMilliseconds(1);
        // Champs assistés (code postal, ville) : module chargé à la demande, sans effet sans navigateur.
        JSInterop.SetupModule(AssistModule.Path).Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ICustomersGateway>(_gateway);
        Services.AddSingleton<IDirectoryGateway>(_directory);
        Services.AddSingleton<IAccountingGateway>(_accounting);
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IQuotesGateway>(_quotes);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        UseCulture("fr-FR");
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private IRenderedComponent<CustomersPage> Open(string route)
    {
        Navigation.NavigateTo(route);
        IRenderedComponent<CustomersPage> page = Render<CustomersPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static void Fill(IRenderedComponent<CustomersPage> page, string id, string value) => page.Find($"#{id}").Change(value);

    private Guid AddCustomer(string name, string city = "Lyon")
    {
        Guid id = Guid.CreateVersion7();
        _gateway.Customers.Add(new CustomerSummary(id, CustomerKindDto.Company, name, "732829320", city));
        return id;
    }

    private static void FillAddress(IRenderedComponent<CustomersPage> page, string line1 = "1 rue de la Paix", string zip = "75002", string city = "Paris")
    {
        Fill(page, "line1", line1);
        Fill(page, "zip", zip);
        Fill(page, "city", city);
    }

    [Fact]
    public void La_liste_est_en_francais_par_defaut_et_invite_a_creer_le_premier_client()
    {
        UseCulture(LettraCultures.Resolve("de-DE").Name);

        IRenderedComponent<CustomersPage> page = Open("customers");

        page.Find("h1").TextContent.Should().Be("Clients");
        page.Find("#customers-empty").TextContent.Should().Contain("Aucun client pour l'instant.");
        page.Find("#customers-new").GetAttribute("href").Should().Be("customers/new");
        page.FindAll("form").Should().BeEmpty("on consulte d'abord, la saisie s'ouvre à la demande");
    }

    [Fact]
    public void La_saisie_est_traduite_en_anglais()
    {
        UseCulture("en-US");

        IRenderedComponent<CustomersPage> page = Open("customers/new");

        page.Find("#explorer-save").TextContent.Should().Contain("Create");
        page.Find("label[for=siret]").TextContent.Should().Be("SIRET");
        page.Find("label[for=zip]").TextContent.Should().Be("Postal code");
    }

    [Fact]
    public void La_validation_immediate_affiche_des_messages_traduits_sans_appeler_le_serveur()
    {
        IRenderedComponent<CustomersPage> page = Open("customers/new");

        page.Find("form").Submit();

        _gateway.LastRequest.Should().BeNull();
        page.FindAll(".validation-message").Select(e => e.TextContent)
            .Should().Contain("Le nom du client est obligatoire.")
            .And.Contain("L'adresse est obligatoire.")
            .And.NotContain(m => m.Contains("Customer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Creer_un_client_ouvre_sa_fiche_en_lecture_et_l_ajoute_a_la_liste()
    {
        IRenderedComponent<CustomersPage> page = Open("customers/new");

        Fill(page, "name", "Boulangerie Martin");
        Fill(page, "siret", "73282932000074");
        FillAddress(page);
        page.Find("form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Client « Boulangerie Martin » créé."));
        Guid id = _gateway.Customers.Single().Id;
        Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"customers/{id}");
        page.Find("#customer-sheet-title").TextContent.Should().Be("Boulangerie Martin");
        page.Find("#customers-list").TextContent.Should().Contain("Boulangerie Martin");
        page.Find($"#customers-option-{id}").GetAttribute("aria-selected").Should().Be("true");
        page.FindAll("form").Should().BeEmpty();
        _gateway.LastRequest!.PreferredLanguage.Should().BeNullOrEmpty("le français est la langue par défaut");
    }

    [Fact]
    public void Un_lien_direct_ouvre_la_fiche_avec_ses_coordonnees_et_son_activite()
    {
        Guid id = AddCustomer("Garage des Lilas", "Nantes");
        _accounting.Entries.Add(FakeAccountingGateway.Invoice("F2026-0001", new DateOnly(2026, 3, 2), id, "Garage des Lilas", 1000m, 200m));

        IRenderedComponent<CustomersPage> page = Open($"customers/{id}");

        page.Find("#customer-sheet-title").TextContent.Should().Be("Garage des Lilas");
        page.Find("#customer-details").TextContent.Should().Contain("1 rue de la Paix").And.Contain("Nantes");
        page.Find("#customer-kpi-invoiced").TextContent.Should().Contain(ExplorerText.Money(1200m));
        page.Find("#customer-kpi-balance a").GetAttribute("href").Should().Be($"accounting/accounts/411?customer={id}");
        _accounting.LedgerQueries.Should().ContainSingle(q => q.Account == "411" && q.CustomerId == id && q.From == new DateOnly(2026, 1, 1));
        page.Find("#customer-new-quote").GetAttribute("href").Should().Be($"quotes/new?customer={id}");
    }

    [Fact]
    public void Les_blocs_se_replient_et_restent_replies_d_une_fiche_a_l_autre()
    {
        Guid first = AddCustomer("Studio Nord");
        Guid second = AddCustomer("Atelier Coste");
        IRenderedComponent<CustomersPage> page = Open($"customers/{first}");
        page.Find("#customer-details .lt-collapsible-toggle").GetAttribute("aria-expanded").Should().Be("true");
        page.Find("#customer-quotes .lt-collapsible-toggle").GetAttribute("aria-expanded").Should().Be("false", "les devis sont repliés au départ pour alléger la fiche");

        page.Find("#customer-details .lt-collapsible-toggle").Click();
        page.Find("#customer-details-body").HasAttribute("hidden").Should().BeTrue();

        Navigation.NavigateTo($"customers/{second}");
        page.WaitForAssertion(() => page.Find("#customer-sheet-title").TextContent.Should().Be("Atelier Coste"));
        page.Find("#customer-details .lt-collapsible-toggle").GetAttribute("aria-expanded").Should().Be("false");

        page.Find("#customer-details .lt-collapsible-toggle").Click();
        page.Find("#customer-details-body").HasAttribute("hidden").Should().BeFalse();
    }

    [Fact]
    public void Modifier_passe_en_saisie_dans_la_meme_fiche_et_enregistre_avec_la_version()
    {
        Guid id = AddCustomer("Pharmacie du Port", "Lille");
        IRenderedComponent<CustomersPage> page = Open($"customers/{id}");

        page.Find("#customer-edit").GetAttribute("href").Should().Be($"customers/{id}/edit");
        Navigation.NavigateTo($"customers/{id}/edit");
        page.WaitForAssertion(() => page.Find("#name").GetAttribute("value").Should().Be("Pharmacie du Port"));
        page.FindAll("#explorer-dirty").Should().BeEmpty();

        Fill(page, "city", "Dunkerque");
        page.Find("#explorer-dirty").TextContent.Should().Be("Modifications non enregistrées");
        page.Find("form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Client « Pharmacie du Port » enregistré."));
        _gateway.Updates.Should().ContainSingle().Which.ExpectedVersion.Should().Be(1);
        _gateway.Updates[0].Request.City.Should().Be("Dunkerque");
        Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"customers/{id}");
        page.Find("#customer-details").TextContent.Should().Contain("Dunkerque");
    }

    [Fact]
    public void Annuler_une_saisie_modifiee_demande_avant_d_abandonner()
    {
        Guid id = AddCustomer("Cabinet Roux");
        IRenderedComponent<CustomersPage> page = Open($"customers/{id}/edit");

        Fill(page, "city", "Rennes");
        page.Find("#explorer-cancel").Click();

        page.Find("#explorer-discard").TextContent.Should().Contain("Les abandonner ?");
        page.Find("#explorer-discard-no").Click();
        page.FindAll("#explorer-discard").Should().BeEmpty();
        page.Find("#city").GetAttribute("value").Should().Be("Rennes", "la saisie continue sans rien perdre");

        page.Find("#explorer-cancel").Click();
        page.Find("#explorer-discard-yes").Click();

        page.WaitForAssertion(() => Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"customers/{id}"));
        page.WaitForAssertion(() => page.FindAll("form").Should().BeEmpty());
        _gateway.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Les_raccourcis_Echap_et_Inser_agissent_sur_la_fiche()
    {
        Guid id = AddCustomer("Cabinet Roux");
        IRenderedComponent<CustomersPage> page = Open($"customers/{id}/edit");
        ShortcutRegistry shortcuts = Services.GetRequiredService<ShortcutRegistry>();
        Fill(page, "city", "Rennes");

        await page.InvokeAsync(() => shortcuts.TryHandleAsync(KeyChord.Parse("Escape"), CommandContext.None));
        page.WaitForAssertion(() => page.Find("#explorer-discard").TextContent.Should().Contain("Les abandonner ?"));
        page.Find("#explorer-discard-yes").Click();
        page.WaitForAssertion(() => page.Find("#customer-sheet"));

        await page.InvokeAsync(() => shortcuts.TryHandleAsync(KeyChord.Parse("Escape"), CommandContext.None));
        page.WaitForAssertion(() => Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be("customers"));

        await page.InvokeAsync(() => shortcuts.TryHandleAsync(KeyChord.Parse("Insert"), CommandContext.None));
        page.WaitForAssertion(() => page.Find("#customer-form"));
        Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be("customers/new");
    }

    [Fact]
    public void Annuler_sans_modification_revient_a_la_lecture_sans_question()
    {
        Guid id = AddCustomer("Cabinet Roux");
        IRenderedComponent<CustomersPage> page = Open($"customers/{id}/edit");

        page.Find("#explorer-cancel").Click();

        page.WaitForAssertion(() => Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"customers/{id}"));
        page.FindAll("#explorer-discard").Should().BeEmpty();
    }

    [Fact]
    public void Une_modification_concurrente_propose_de_garder_ses_valeurs_ou_de_reprendre_les_siennes()
    {
        Guid id = AddCustomer("Studio Nord", "Lyon");
        IRenderedComponent<CustomersPage> page = Open($"customers/{id}/edit");
        Fill(page, "city", "Villeurbanne");
        _gateway.EditedByColleague(id, c => c with { City = "Bron" });

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("#explorer-stale").TextContent.Should().Contain("« Studio Nord » a été modifié par quelqu'un d'autre"));
        page.Find("#explorer-take-theirs").Click();

        page.Find("#city").GetAttribute("value").Should().Be("Bron");
        page.FindAll("#explorer-stale").Should().BeEmpty();
    }

    [Fact]
    public void Garder_ses_valeurs_enregistre_par_dessus_la_version_actuelle()
    {
        Guid id = AddCustomer("Studio Nord", "Lyon");
        IRenderedComponent<CustomersPage> page = Open($"customers/{id}/edit");
        Fill(page, "city", "Villeurbanne");
        _gateway.EditedByColleague(id, c => c with { City = "Bron" });
        page.Find("form").Submit();
        page.WaitForAssertion(() => page.Find("#explorer-overwrite"));

        page.Find("#explorer-overwrite").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Client « Studio Nord » enregistré."));
        _gateway.Updates.Select(u => u.ExpectedVersion).Should().Equal(1, 2);
        _gateway.Records[id].City.Should().Be("Villeurbanne");
    }

    [Fact]
    public void La_recherche_filtre_la_liste_et_propose_de_creer_le_client_introuvable()
    {
        AddCustomer("Boulangerie Martin");
        AddCustomer("Garage des Lilas");
        IRenderedComponent<CustomersPage> page = Open("customers");
        page.FindAll("#customers-list [role=option]").Should().HaveCount(2);

        page.Find("#customers-search").Input("garage");
        page.WaitForAssertion(() => page.FindAll("#customers-list [role=option]").Should().ContainSingle().Which.TextContent.Should().Contain("Garage des Lilas"));

        page.Find("#customers-search").Input("Dupont");
        page.WaitForAssertion(() => page.Find("#customers-create-from-search").GetAttribute("href").Should().Be("customers/new?name=Dupont"));

        Navigation.NavigateTo("customers/new?name=Dupont");
        page.WaitForAssertion(() => page.Find("#name").GetAttribute("value").Should().Be("Dupont"));
    }

    [Fact]
    public void Les_fleches_parcourent_la_liste_et_la_fiche_suit()
    {
        Guid first = AddCustomer("Atelier Coste");
        Guid second = AddCustomer("Boulangerie Martin");
        IRenderedComponent<CustomersPage> page = Open("customers");

        page.Find("#customers-list").KeyDown("ArrowDown");
        page.WaitForAssertion(() => Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"customers/{first}"));
        page.Find("#customers-list").KeyDown("ArrowDown");
        page.WaitForAssertion(() => page.Find("#customer-sheet-title").TextContent.Should().Be("Boulangerie Martin"));
        page.Find("#customers-list").GetAttribute("aria-activedescendant").Should().Be($"customers-option-{second}");

        page.Find("#customers-list").KeyDown("Enter");
        page.WaitForAssertion(() => Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"customers/{second}/edit"));
    }

    [Fact]
    public void La_langue_des_pieces_imprimees_part_avec_le_client()
    {
        IRenderedComponent<CustomersPage> page = Open("customers/new");

        Fill(page, "name", "Acme Ltd");
        FillAddress(page, "1 Main Street", "SW1A 1AA", "London");
        Fill(page, "country", "GB");
        page.Find("#language").Change("en-US");
        page.Find("form").Submit();

        page.WaitForAssertion(() => _gateway.LastRequest!.PreferredLanguage.Should().Be("en-US"));
    }

    [Fact]
    public void Les_erreurs_du_serveur_s_affichent_sous_le_bon_champ_dans_la_langue_de_l_utilisateur()
    {
        UseCulture("en-US");
        _gateway.NextErrors = [new ErrorDetail(ErrorCodes.Customer.SirenRequired, "Siren"), new ErrorDetail(ErrorCodes.Security.Forbidden)];
        IRenderedComponent<CustomersPage> page = Open("customers/new");

        Fill(page, "name", "Acme");
        FillAddress(page);
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Markup.Should().Contain("A French company must have a SIREN (or a SIRET)."));
        page.Find("#siren ~ .validation-message").TextContent.Should().Be("A French company must have a SIREN (or a SIRET).");
        this.Toasts().Select(t => t.Message).Should().Contain("You do not have permission for this action in this file.");
        Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be("customers/new", "la saisie reste ouverte pour corriger");
    }

    [Fact]
    public void L_annuaire_pre_remplit_une_entreprise_cliente()
    {
        _directory.Companies.Add(FakeDirectoryGateway.Danone);
        IRenderedComponent<CustomersPage> page = Open("customers/new");

        page.Find("#customer-lookup-query").Input("danone");
        page.Find("#customer-lookup-query").KeyDown("Enter");
        page.Find("#customer-lookup-query").KeyDown("Enter");

        page.Find("#name").GetAttribute("value").Should().Be("DANONE");
        page.Find("#siret").GetAttribute("value").Should().Be("55203253400646");
        page.Find("#vat").GetAttribute("value").Should().Be("FR27552032534");
        page.Find("#zip").GetAttribute("value").Should().Be("75009");
    }

    [Fact]
    public void L_annuaire_n_est_propose_que_pour_une_entreprise()
    {
        IRenderedComponent<CustomersPage> page = Open("customers/new");

        page.FindAll("#customer-lookup-query").Should().HaveCount(1);
        page.FindAll("input[type=radio]")[1].Change(CustomerKindDto.Individual.ToString());

        page.FindAll("#customer-lookup-query").Should().BeEmpty();
    }

    [Fact]
    public void Une_fiche_introuvable_affiche_l_erreur_traduite()
    {
        IRenderedComponent<CustomersPage> page = Open($"customers/{Guid.CreateVersion7()}");

        page.FindAll("#customer-sheet").Should().BeEmpty();
        page.Markup.Should().NotContain("Error.Customer.NotFound");
    }
}
