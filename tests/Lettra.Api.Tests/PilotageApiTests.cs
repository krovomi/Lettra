using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pilotage;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Tableaux de bord dans Lettra : chacun ne voit que les dossiers que ses droits lui ouvrent. « Mes dossiers » demande
/// de lire le dossier, « Administration » de l'administrer ; le dossier ouvert se lit avec le droit de lecture.
/// </summary>
public sealed class PilotageApiTests
{
    private static readonly Uri Dossier = new("/api/v1/pilotage", UriKind.Relative);
    private static readonly Uri Cabinet = new("/api/v1/dossiers/pilotage", UriKind.Relative);
    private static readonly Uri Administration = new("/api/v1/dossiers/pilotage/administration", UriKind.Relative);

    [Fact]
    public async Task Le_dossier_ouvert_se_lit_avec_le_droit_de_lecture_et_pas_depuis_le_portail()
    {
        await using LettraApiFactory api = new();

        DossierPilotageResponse seen = (await InDossier(api, LettraApiFactory.CollaborateurId)
            .GetFromJsonAsync(Dossier, ContractsJsonContext.Default.DossierPilotageResponse))!;
        seen.Dossier.DossierId.Should().Be(LettraApiFactory.DossierId);
        seen.Dossier.Months.Should().HaveCount(12, "douze mois glissants, même sans pièce");
        seen.Dossier.ActiveCustomers.Should().Be(0);
        seen.ToDoComputedAt.Should().BeNull("la vue cabinet n'a pas encore été calculée");

        using HttpResponseMessage portal = await InDossier(api, LettraApiFactory.PortailId).GetAsync(Dossier);
        portal.StatusCode.Should().Be(HttpStatusCode.Forbidden, "le client du portail ne voit pas le pilotage du cabinet");

        using HttpResponseMessage admin = await InDossier(api, LettraApiFactory.AdministrateurCabinetId).GetAsync(Dossier);
        admin.IsSuccessStatusCode.Should().BeFalse("administrer n'est pas lire les pièces");
    }

    [Fact]
    public async Task Mes_dossiers_ne_liste_que_les_dossiers_lisibles()
    {
        await using LettraApiFactory api = new();

        CabinetPilotageResponse manager = (await api.ClientFor(LettraApiFactory.ManagerId)
            .GetFromJsonAsync(Cabinet, ContractsJsonContext.Default.CabinetPilotageResponse))!;
        manager.Available.Should().BeTrue("le serveur collecte les indicateurs");
        manager.CollectedAt.Should().NotBeNull();
        manager.Dossiers.Should().ContainSingle(d => d.DossierId == LettraApiFactory.DossierId);

        (await api.ClientFor(LettraApiFactory.CollaborateurId).GetFromJsonAsync(Cabinet, ContractsJsonContext.Default.CabinetPilotageResponse))!
            .Dossiers.Select(d => d.DossierId).Should().Equal(LettraApiFactory.DossierId);

        (await api.ClientFor(LettraApiFactory.AutreCabinetAdminId).GetFromJsonAsync(Cabinet, ContractsJsonContext.Default.CabinetPilotageResponse))!
            .Dossiers.Should().BeEmpty("un autre cabinet ne voit pas ce dossier");

        (await api.ClientFor(LettraApiFactory.AdministrateurCabinetId).GetFromJsonAsync(Cabinet, ContractsJsonContext.Default.CabinetPilotageResponse))!
            .Dossiers.Should().BeEmpty("l'administrateur du cabinet ne lit pas les chiffres de gestion");
    }

    [Fact]
    public async Task L_administration_ne_liste_que_les_dossiers_administres()
    {
        await using LettraApiFactory api = new();

        AdministrationPilotageResponse admin = (await api.ClientFor(LettraApiFactory.AdministrateurCabinetId)
            .GetFromJsonAsync(Administration, ContractsJsonContext.Default.AdministrationPilotageResponse))!;
        admin.Available.Should().BeTrue();
        DossierOperationsDto row = admin.Dossiers.Should().ContainSingle(d => d.DossierId == LettraApiFactory.DossierId).Subject;
        row.ActiveDevices.Should().Be(0);

        (await api.ClientFor(LettraApiFactory.ManagerId).GetFromJsonAsync(Administration, ContractsJsonContext.Default.AdministrationPilotageResponse))!
            .Dossiers.Should().BeEmpty("le manager produit mais n'administre pas : l'onglet reste caché");
        (await api.ClientFor(LettraApiFactory.AutreCabinetAdminId).GetFromJsonAsync(Administration, ContractsJsonContext.Default.AdministrationPilotageResponse))!
            .Dossiers.Should().BeEmpty();

        (await api.ClientFor(LettraApiFactory.SuspenduId).GetFromJsonAsync(Administration, ContractsJsonContext.Default.AdministrationPilotageResponse))!
            .Dossiers.Should().BeEmpty("un compte suspendu ne voit plus aucun dossier");
    }

    private static HttpClient InDossier(LettraApiFactory api, Guid user)
    {
        HttpClient client = api.ClientFor(user);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }
}
