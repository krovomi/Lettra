using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Behaviors;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Le pipeline applique la permission déclarée, sur la portée du cas d'usage (annexe F §4).</summary>
public sealed class AuthorizationBehaviorTests
{
    private readonly FakeUser _user = new();
    private readonly FakePermissions _permissions = new();
    private bool _handled;

    private Task<Result> Run<TRequest>(TRequest request)
        where TRequest : notnull =>
        new AuthorizationBehavior<TRequest, Result>(_user, _permissions).HandleAsync(request, () =>
        {
            _handled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

    private sealed record DossierCommand(DossierId Dossier) : ICommand<Result>, IRequirePermission
    {
        public Permission Permission => Permission.FactureValider;
    }

    private sealed record GroupCommand(GroupId Group) : ICommand<Result>, IRequireGroupPermission
    {
        public Permission Permission => Permission.GroupeGerer;
    }

    private sealed record CabinetCommand(OrganisationId Organisation) : ICommand<Result>, IRequireOrganisationPermission
    {
        public Permission Permission => Permission.UtilisateursGerer;
    }

    private sealed record ScopelessCommand : ICommand<Result>, ISecuredUseCase
    {
        public Permission Permission => Permission.PlateformeAdministrer;
    }

    [PublicUseCase]
    private sealed record PublicQuery : IQuery<Result>;

    private sealed record UndeclaredQuery : IQuery<Result>;

    [Fact]
    public async Task Un_cas_d_usage_de_dossier_est_verifie_sur_le_dossier()
    {
        var dossier = new DossierId(Guid.CreateVersion7());

        Result result = await Run(new DossierCommand(dossier));

        result.IsSuccess.Should().BeTrue();
        _handled.Should().BeTrue();
        _permissions.Asked.Should().Equal(AuthorizationScope.ForDossier(dossier));
    }

    [Fact]
    public async Task Un_cas_d_usage_de_groupe_est_verifie_sur_le_groupe()
    {
        var group = new GroupId(Guid.CreateVersion7());

        await Run(new GroupCommand(group));

        _permissions.Asked.Should().Equal(AuthorizationScope.ForGroup(group));
    }

    [Fact]
    public async Task Un_cas_d_usage_du_cabinet_est_verifie_sur_le_cabinet()
    {
        var cabinet = new OrganisationId(Guid.CreateVersion7());

        await Run(new CabinetCommand(cabinet));

        _permissions.Asked.Should().Equal(AuthorizationScope.ForCabinet(cabinet));
    }

    [Fact]
    public async Task Sans_la_permission_le_cas_d_usage_est_refuse_sans_etre_execute()
    {
        _permissions.Granted.Remove(Permission.FactureValider);

        Result result = await Run(new DossierCommand(new DossierId(Guid.CreateVersion7())));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _handled.Should().BeFalse();
    }

    [Fact]
    public async Task Un_utilisateur_non_authentifie_est_refuse_avant_tout_calcul_de_droits()
    {
        _user.IsAuthenticated = false;

        Result result = await Run(new CabinetCommand(new OrganisationId(Guid.CreateVersion7())));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.UnauthenticatedCode);
        _permissions.Asked.Should().BeEmpty();
        _handled.Should().BeFalse();
    }

    [Fact]
    public async Task Un_cas_d_usage_sans_portee_connue_est_refuse()
    {
        Result result = await Run(new ScopelessCommand());

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _handled.Should().BeFalse();
    }

    [Fact]
    public async Task Un_cas_d_usage_public_passe_meme_sans_session()
    {
        _user.IsAuthenticated = false;

        Result result = await Run(new PublicQuery());

        result.IsSuccess.Should().BeTrue();
        _handled.Should().BeTrue();
    }

    [Fact]
    public async Task Un_cas_d_usage_qui_ne_declare_rien_est_refuse()
    {
        Result result = await Run(new UndeclaredQuery());

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _handled.Should().BeFalse();
    }
}
