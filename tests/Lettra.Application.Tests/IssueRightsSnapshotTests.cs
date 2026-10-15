using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Identity;
using Lettra.Contracts.Identity;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>L'instantané ne signe, dossier par dossier, que les permissions de données accordées par le registre.</summary>
public sealed class IssueRightsSnapshotTests
{
    private static readonly DossierId Ouvert = new(Guid.CreateVersion7());
    private static readonly DossierId Ferme = new(Guid.CreateVersion7());
    private static readonly DossierId AdministrationSeule = new(Guid.CreateVersion7());

    private readonly FakeUser _user = new();
    private readonly RecordingIssuer _issuer = new();

    private Task<Result<RightsSnapshotResponse>> Issue(IRightsSnapshotIssuer? issuer, params DossierId[] dossiers) =>
        new IssueRightsSnapshotHandler(_user, new PerDossierPermissions(), TimeProvider.System, issuer)
            .HandleAsync(new IssueRightsSnapshotCommand(Guid.CreateVersion7(), dossiers), CancellationToken.None);

    [Fact]
    public async Task Seuls_les_dossiers_avec_des_droits_de_donnees_entrent_dans_l_instantane()
    {
        Result<RightsSnapshotResponse> issued = await Issue(_issuer, Ouvert, Ferme, AdministrationSeule, Ouvert);

        issued.IsSuccess.Should().BeTrue();
        issued.Value.DossierCount.Should().Be(1);
        RightsSnapshot signed = _issuer.Signed!;
        signed.UserId.Should().Be(_user.UserId);
        signed.Dossiers.Keys.Should().Equal(Ouvert);
        signed.Dossiers[Ouvert].Should().BeEquivalentTo([Permission.TiersConsulter, Permission.FactureSaisir], "l'administration reste en ligne");
        signed.ExpiresAt.Should().Be(signed.IssuedAt + _issuer.Validity);
    }

    [Fact]
    public async Task Sans_session_aucun_instantane()
    {
        _user.IsAuthenticated = false;

        (await Issue(_issuer, Ouvert)).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.UnauthenticatedCode);
        _issuer.Signed.Should().BeNull();
    }

    [Fact]
    public async Task Sans_cle_de_signature_le_hors_ligne_est_indisponible()
    {
        (await Issue(null, Ouvert)).Errors.Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.UnavailableCode);
        (await Issue(new RecordingIssuer { IsAvailable = false }, Ouvert)).Errors.Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.UnavailableCode);
    }

    [Fact]
    public void Le_poste_et_le_nombre_de_dossiers_sont_valides()
    {
        IssueRightsSnapshotValidator validator = new();

        validator.Validate(new IssueRightsSnapshotCommand(Guid.Empty, [])).Select(e => e.Code)
            .Should().Equal(RightsSnapshotErrors.DeviceRequiredCode);
        validator.Validate(new IssueRightsSnapshotCommand(Guid.CreateVersion7(), Enumerable.Range(0, IssueRightsSnapshotRequest.MaxDossiers + 1).Select(_ => new DossierId(Guid.CreateVersion7())).ToList()))
            .Select(e => e.Code).Should().Equal(RightsSnapshotErrors.TooManyDossiersCode);
    }

    private sealed class PerDossierPermissions : IPermissionService
    {
        public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken)
        {
            IReadOnlySet<Permission> granted = scope.Id == Ouvert.Value
                ? new HashSet<Permission> { Permission.TiersConsulter, Permission.FactureSaisir, Permission.AlertesLire }
                : scope.Id == AdministrationSeule.Value
                    ? new HashSet<Permission> { Permission.DossierAdministrer }
                    : new HashSet<Permission>();
            return ValueTask.FromResult(granted);
        }
    }

    private sealed class RecordingIssuer : IRightsSnapshotIssuer
    {
        public bool IsAvailable { get; init; } = true;

        public TimeSpan Validity => TimeSpan.FromDays(7);

        public RightsSnapshot? Signed { get; private set; }

        public string Sign(RightsSnapshot snapshot)
        {
            Signed = snapshot;
            return "jws";
        }
    }
}
