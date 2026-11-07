using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>
/// Les dossiers du cabinet sur un hôte sans catalogue (poste isolé, serveur du cabinet) : listés tels quels, aucun ne
/// s'efface par l'API. Le catalogue est essayé à part, sur les trois bases.
/// </summary>
public sealed class CabinetDossiersTests
{
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());
    private static readonly DossierSummary Dossier = new(Guid.CreateVersion7(), "Boulangerie du Port");

    private readonly NoDossierEraser _eraser = new(new Directory());

    [Fact]
    public async Task Sans_catalogue_les_dossiers_sont_listes_sans_effacement_possible()
    {
        Result<IReadOnlyList<CabinetDossierResponse>> listed = await new ListCabinetDossiersHandler(_eraser)
            .HandleAsync(new ListCabinetDossiersQuery(Cabinet), CancellationToken.None);

        listed.Value.Should().Equal(new CabinetDossierResponse(Dossier.Id, Dossier.Name, CabinetDossierStateDto.Active, Erasable: false, ErasureDueAt: null));
        (await _eraser.ListAsync(new OrganisationId(Guid.CreateVersion7()), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_catalogue_l_effacement_est_refuse_et_rien_n_est_annulable()
    {
        (await _eraser.ScheduleAsync(Cabinet, new DossierId(Dossier.Id), Guid.CreateVersion7(), CancellationToken.None))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.DeclaredCode);
        (await _eraser.CancelAsync(Cabinet, new DossierId(Dossier.Id), CancellationToken.None))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.ErasureNotCancellableCode);
        (await _eraser.ListScheduledAsync(Cabinet, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public void La_liste_est_reservee_a_l_administrateur_du_cabinet() =>
        new ListCabinetDossiersQuery(Cabinet).Permission.Should().Be(Abstractions.Identity.Permission.CabinetAdministrer);

    private sealed class Directory : IDossierDirectory
    {
        public Task<IReadOnlyList<DossierSummary>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DossierSummary>>(organisation == Cabinet ? [Dossier] : []);
    }
}
