using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Serialization;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Réversibilité par l'API : l'administrateur du cabinet exporte un dossier dans un paquet chiffré, puis l'importe
/// sur un hôte qui ne l'a pas ; personne d'autre ne le peut.
/// </summary>
public sealed class DossierTransferApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string Passphrase = "cheval agrafe batterie correcte";

    private static Uri Export(Guid dossier) => new($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/dossiers/{dossier}/export", UriKind.Relative);

    private static readonly Uri Import = new($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/dossiers/import", UriKind.Relative);

    [Fact]
    public async Task Un_dossier_exporte_revient_par_l_import_avec_ses_donnees()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);
        DossierSummary created = (await (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/dossiers", UriKind.Relative),
                new CreateDossierRequest { Name = "Dossier à rapatrier" },
                ContractsJsonContext.Default.CreateDossierRequest)).Content
            .ReadFromJsonAsync(ContractsJsonContext.Default.DossierSummary))!;
        using (HttpClient inDossier = InDossier(factory.ClientFor(LettraApiFactory.ManagerId), created.Id))
        {
            (await inDossier.PostAsJsonAsync("/api/v1/customers", CustomerApiTests.Martin(), ContractsJsonContext.Default.CreateCustomerRequest))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using HttpResponseMessage exported = await ExportAsync(admin, created.Id, Passphrase);
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        exported.Content.Headers.ContentType!.MediaType.Should().Be(DossierPackageRules.ContentType);
        (exported.Content.Headers.ContentDisposition!.FileNameStar ?? exported.Content.Headers.ContentDisposition.FileName).Should().StartWith("Dossier-a-rapatrier-").And.EndWith(DossierPackageRules.FileExtension);
        byte[] package = await exported.Content.ReadAsByteArrayAsync();

        // Le dossier existe encore ici : l'import est refusé, sans rien créer.
        using (HttpResponseMessage duplicate = await ImportAsync(admin, package, Passphrase))
        {
            duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await duplicate.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Tenancy.DossierAlreadyExists);
        }

        // Le dossier quitte cet hôte (comme après un départ), puis revient par son paquet.
        await factory.Services.GetRequiredService<IDossierProvisioner>().DiscardAsync(new DossierId(created.Id), default);
        using (HttpResponseMessage wrong = await ImportAsync(admin, package, "une autre phrase secrète"))
        {
            wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await wrong.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Tenancy.PackageUnreadable);
        }

        using HttpResponseMessage imported = await ImportAsync(admin, package, Passphrase);
        imported.StatusCode.Should().Be(HttpStatusCode.Created);
        (await imported.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierSummary)).Should().Be(created);

        using HttpClient back = InDossier(factory.ClientFor(LettraApiFactory.ManagerId), created.Id);
        PagedResponse<CustomerSummary> customers = (await back.GetFromJsonAsync("/api/v1/customers", ContractsJsonContext.Default.PagedResponseCustomerSummary))!;
        customers.Items.Should().ContainSingle().Which.Name.Should().Be("Boulangerie Martin");
    }

    [Fact]
    public async Task Une_phrase_secrete_trop_courte_ou_un_formulaire_sans_paquet_sont_refuses()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

        using HttpResponseMessage shortPhrase = await ExportAsync(admin, LettraApiFactory.DossierId, "trop court");
        shortPhrase.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await shortPhrase.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Tenancy.PassphraseTooShort);

        using MultipartFormDataContent empty = new() { { new StringContent(Passphrase), DossierPackageRules.PassphraseField } };
        using HttpResponseMessage noFile = await admin.PostAsync(Import, empty);
        noFile.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noFile.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Tenancy.PackageUnreadable);

        using HttpResponseMessage notAPackage = await ImportAsync(admin, "ceci n'est pas un paquet"u8.ToArray(), Passphrase);
        notAPackage.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage unknown = await ExportAsync(admin, Guid.CreateVersion7(), Passphrase);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("collaborateur")]
    [InlineData("autre-cabinet")]
    [InlineData("suspendu")]
    public async Task Seul_l_administrateur_du_cabinet_exporte_ou_importe(string who)
    {
        Guid user = who switch
        {
            "manager" => LettraApiFactory.ManagerId,
            "collaborateur" => LettraApiFactory.CollaborateurId,
            "autre-cabinet" => LettraApiFactory.AutreCabinetAdminId,
            _ => LettraApiFactory.SuspenduId,
        };
        using HttpClient client = factory.ClientFor(user);

        (await ExportAsync(client, LettraApiFactory.DossierId, Passphrase)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ImportAsync(client, [1, 2, 3], Passphrase)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static HttpClient InDossier(HttpClient client, Guid dossier)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, dossier.ToString());
        return client;
    }

    private static Task<HttpResponseMessage> ExportAsync(HttpClient client, Guid dossier, string passphrase) =>
        client.PostAsJsonAsync(Export(dossier), new ExportDossierRequest { Passphrase = passphrase }, ContractsJsonContext.Default.ExportDossierRequest);

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[] package, string passphrase)
    {
        using ByteArrayContent file = new(package);
        file.Headers.ContentType = new MediaTypeHeaderValue(DossierPackageRules.ContentType);
        using MultipartFormDataContent form = new()
        {
            { file, DossierPackageRules.PackageField, "dossier" + DossierPackageRules.FileExtension },
            { new StringContent(passphrase), DossierPackageRules.PassphraseField },
        };
        return await client.PostAsync(Import, form);
    }
}
