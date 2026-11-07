using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Écran « Dossiers du cabinet » par la gateway : en HTTP, sur le catalogue de l'API (les dossiers créés en ligne
/// s'effacent) ; en direct, sur le poste isolé, dont les dossiers se créent mais ne s'effacent pas. Dans les deux cas,
/// un dossier exporté revient par l'import de son paquet.
/// </summary>
public sealed class CabinetDossiersGatewayTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    [Fact]
    public async Task En_ligne_un_dossier_se_cree_s_efface_puis_l_effacement_s_annule()
    {
        await using ServiceProvider services = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.AdministrateurCabinetId);
        ICabinetDossiersGateway gateway = services.GetRequiredService<ICabinetDossiersGateway>();
        Guid cabinet = LettraApiFactory.CabinetId;

        DossierSummary created = (await gateway.CreateAsync(cabinet, new CreateDossierRequest { Name = "Fromagerie des Halles" }, default)).Value!;
        DossierErasureResponse scheduled = (await gateway.ScheduleErasureAsync(cabinet, created.Id, default)).Value!;
        IReadOnlyList<CabinetDossierResponse> during = (await gateway.ListAsync(cabinet, default)).Value!;
        OperationResult<DossierSummary> cancelled = await gateway.CancelErasureAsync(cabinet, created.Id, default);
        IReadOnlyList<CabinetDossierResponse> after = (await gateway.ListAsync(cabinet, default)).Value!;

        during.Single(d => d.Id == created.Id).Should().Be(
            new CabinetDossierResponse(created.Id, "Fromagerie des Halles", CabinetDossierStateDto.ErasureScheduled, false, scheduled.ErasureDueAt));
        cancelled.Value.Should().Be(created);
        after.Single(d => d.Id == created.Id).State.Should().Be(CabinetDossierStateDto.Active);
        after.Single(d => d.Id == LettraApiFactory.DossierId).Erasable.Should().BeFalse("une base déclarée ne s'efface pas depuis Lettra");
        (await gateway.ScheduleErasureAsync(cabinet, LettraApiFactory.DossierId, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Tenancy.DossierDeclared);
    }

    [Fact]
    public async Task Sur_le_poste_isole_un_dossier_se_cree_mais_ne_s_efface_pas()
    {
        await using ServiceProvider services = _harness.InProcessGateway(null);
        Guid cabinet = (await services.GetRequiredService<IAdministrationGateway>().GetMyAccessAsync(null, default)).Value!.CabinetId!.Value;
        ICabinetDossiersGateway gateway = services.GetRequiredService<ICabinetDossiersGateway>();

        DossierSummary created = (await gateway.CreateAsync(cabinet, new CreateDossierRequest { Name = "Atelier du poste" }, default)).Value!;
        IReadOnlyList<CabinetDossierResponse> listed = (await gateway.ListAsync(cabinet, default)).Value!;

        listed.Should().Contain(new CabinetDossierResponse(created.Id, "Atelier du poste", CabinetDossierStateDto.Active, false, null));
        listed.Should().OnlyContain(d => !d.Erasable);
        (await gateway.ScheduleErasureAsync(cabinet, created.Id, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Tenancy.DossierDeclared);
        (await gateway.CancelErasureAsync(cabinet, created.Id, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Tenancy.ErasureNotCancellable);
    }

    [Fact]
    public async Task En_ligne_un_dossier_exporte_revient_par_l_import_de_son_paquet()
    {
        await using ServiceProvider services = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.AdministrateurCabinetId);
        ICabinetDossiersGateway gateway = services.GetRequiredService<ICabinetDossiersGateway>();
        Guid cabinet = LettraApiFactory.CabinetId;
        DossierSummary created = (await gateway.CreateAsync(cabinet, new CreateDossierRequest { Name = "Dossier à rapatrier" }, default)).Value!;

        (await gateway.ExportAsync(cabinet, created.Id, new ExportDossierRequest { Passphrase = "court" }, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Tenancy.PassphraseTooShort);
        DossierPackageFile package = (await gateway.ExportAsync(cabinet, created.Id, new ExportDossierRequest { Passphrase = Passphrase }, default)).Value!;
        package.FileName.Should().StartWith("Dossier-a-rapatrier-").And.EndWith(DossierPackageRules.FileExtension);

        await factory.Services.GetRequiredService<IDossierProvisioner>().DiscardAsync(new DossierId(created.Id), default);
        (await ImportAsync(gateway, cabinet, package, "une autre phrase secrète")).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Tenancy.PackageUnreadable);
        (await ImportAsync(gateway, cabinet, package, Passphrase)).Value.Should().Be(created);
        (await gateway.ListAsync(cabinet, default)).Value!.Should().Contain(d => d.Id == created.Id && d.State == CabinetDossierStateDto.Active);
    }

    [Fact]
    public async Task Sur_le_poste_isole_un_dossier_exporte_revient_par_l_import_de_son_paquet()
    {
        await using ServiceProvider services = _harness.InProcessGateway(null);
        Guid cabinet = (await services.GetRequiredService<IAdministrationGateway>().GetMyAccessAsync(null, default)).Value!.CabinetId!.Value;
        ICabinetDossiersGateway gateway = services.GetRequiredService<ICabinetDossiersGateway>();
        DossierSummary created = (await gateway.CreateAsync(cabinet, new CreateDossierRequest { Name = "Atelier à emporter" }, default)).Value!;

        DossierPackageFile package = (await gateway.ExportAsync(cabinet, created.Id, new ExportDossierRequest { Passphrase = Passphrase }, default)).Value!;
        (await ImportAsync(gateway, cabinet, package, Passphrase)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Tenancy.DossierAlreadyExists, "le dossier est encore sur ce poste");

        await services.GetRequiredService<IDossierProvisioner>().DiscardAsync(new DossierId(created.Id), default);
        (await gateway.ListAsync(cabinet, default)).Value!.Should().NotContain(d => d.Id == created.Id);
        (await ImportAsync(gateway, cabinet, package, Passphrase)).Value.Should().Be(created);
        (await gateway.ListAsync(cabinet, default)).Value!.Should().Contain(d => d.Id == created.Id);
    }

    [Fact]
    public async Task Dans_le_navigateur_le_paquet_part_du_champ_du_fichier_sans_passer_par_l_application()
    {
        ForwardingFileSender browser = new(factory.ClientFor(LettraApiFactory.AdministrateurCabinetId));
        await using ServiceProvider services = _harness.HttpGateway(
            LettraApiFactory.DossierId, LettraApiFactory.AdministrateurCabinetId, s => s.AddSingleton<IBrowserFileSender>(browser));
        ICabinetDossiersGateway gateway = services.GetRequiredService<ICabinetDossiersGateway>();
        Guid cabinet = LettraApiFactory.CabinetId;
        DossierSummary created = (await gateway.CreateAsync(cabinet, new CreateDossierRequest { Name = "Dossier du navigateur" }, default)).Value!;
        DossierPackageFile package = (await gateway.ExportAsync(cabinet, created.Id, new ExportDossierRequest { Passphrase = Passphrase }, default)).Value!;
        await factory.Services.GetRequiredService<IDossierProvisioner>().DiscardAsync(new DossierId(created.Id), default);
        browser.Inputs["champ-du-paquet"] = package.Content;
        ChosenFile chosen = new("paquet.lettrapaquet", package.Content.Length, () => throw new InvalidOperationException("le fichier ne doit pas être lu par l'application"), "champ-du-paquet");

        (await gateway.ImportAsync(cabinet, chosen, "une autre phrase secrète", default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Tenancy.PackageUnreadable, "le refus du serveur se lit comme par HttpClient");
        (await gateway.ImportAsync(cabinet, chosen, Passphrase, default)).Value.Should().Be(created);

        browser.Sent.Should().AllSatisfy(sent => sent.Should().Be(
            ($"http://localhost/api/v1/cabinets/{cabinet}/dossiers/import", DossierPackageRules.PackageField, DossierPackageRules.PassphraseField, "X-Lettra-Csrf")));
    }

    private const string Passphrase = "cheval agrafe batterie correcte";

    private static async Task<OperationResult<DossierSummary>> ImportAsync(ICabinetDossiersGateway gateway, Guid cabinet, DossierPackageFile package, string passphrase) =>
        await gateway.ImportAsync(cabinet, new ChosenFile("paquet.lettrapaquet", package.Content.Length, () => new MemoryStream(package.Content)), passphrase, default);

    /// <summary>Le navigateur simulé : le fichier du champ part en multipart vers l'API de test.</summary>
    private sealed class ForwardingFileSender(HttpClient api) : IBrowserFileSender
    {
        public Dictionary<string, byte[]> Inputs { get; } = [];

        public List<(string Url, string FileField, string Fields, string Headers)> Sent { get; } = [];

        public async Task<HttpResponseMessage> PostAsync(
            Uri url, string inputElementId, string fileField, IReadOnlyDictionary<string, string> fields, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            Sent.Add((url.AbsoluteUri, fileField, string.Join(',', fields.Keys), string.Join(',', headers.Keys)));
            using MultipartFormDataContent form = new() { { new ByteArrayContent(Inputs[inputElementId]), fileField, "paquet.lettrapaquet" } };
            foreach ((string name, string value) in fields)
            {
                form.Add(new StringContent(value), name);
            }

            return await api.PostAsync(new Uri(url.PathAndQuery, UriKind.Relative), form, cancellationToken);
        }
    }

    public void Dispose() => _harness.Dispose();
}
