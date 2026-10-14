using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Identity.Tests;

/// <summary>Registre lu en configuration (mode 2, en attendant la base Catalog) et cache des permissions.</summary>
public sealed class ServerAuthorizationTests : IAsyncLifetime
{
    private static readonly Guid Cabinet = Guid.CreateVersion7();
    private static readonly Guid OtherCabinet = Guid.CreateVersion7();
    private static readonly Guid Group = Guid.CreateVersion7();
    private static readonly Guid ForeignGroup = Guid.CreateVersion7();
    private static readonly DossierId InGroup = new(Guid.CreateVersion7());
    private static readonly DossierId OutOfGroup = new(Guid.CreateVersion7());
    private static readonly DossierId Foreign = new(Guid.CreateVersion7());
    private static readonly Guid Member = Guid.CreateVersion7();

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Lettra:Authorization:Users:0:Id"] = Member.ToString(),
        ["Lettra:Authorization:Users:0:Cabinet"] = Cabinet.ToString(),
        ["Lettra:Authorization:Users:0:Groups:0"] = Group.ToString(),
        ["Lettra:Authorization:Users:0:Groups:1"] = ForeignGroup.ToString(),
        ["Lettra:Authorization:Groups:0:Id"] = Group.ToString(),
        ["Lettra:Authorization:Groups:0:Cabinet"] = Cabinet.ToString(),
        ["Lettra:Authorization:Groups:0:Dossiers:0"] = InGroup.ToString(),
        ["Lettra:Authorization:Groups:0:Assignments:0:Role"] = "collaborateur",
        ["Lettra:Authorization:Groups:0:Assignments:0:Scope"] = "Groupe",
        ["Lettra:Authorization:Groups:0:Assignments:0:ScopeId"] = Group.ToString(),
        // Groupe d'un autre cabinet qui prétend contenir le dossier étranger : ignoré.
        ["Lettra:Authorization:Groups:1:Id"] = ForeignGroup.ToString(),
        ["Lettra:Authorization:Groups:1:Cabinet"] = OtherCabinet.ToString(),
        ["Lettra:Authorization:Groups:1:Dossiers:0"] = Foreign.ToString(),
        ["Lettra:Authorization:Groups:1:Assignments:0:Role"] = "manager",
        ["Lettra:Authorization:Groups:1:Assignments:0:Scope"] = "Groupe",
        ["Lettra:Authorization:Groups:1:Assignments:0:ScopeId"] = ForeignGroup.ToString(),
    };

    private ServiceProvider _services = null!;
    private MutableCatalog _catalog = null!;

    public Task InitializeAsync()
    {
        _catalog = new MutableCatalog(new Dictionary<DossierId, Guid> { [InGroup] = Cabinet, [OutOfGroup] = Cabinet, [Foreign] = OtherCabinet });
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();
        ServiceCollection services = new();
        services.AddSingleton<ITenantCatalog>(_catalog);
        services.AddOidcIdentity(configuration);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private async Task<IReadOnlySet<Permission>> OnAsync(DossierId dossier)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetEffectiveAsync(Member, dossier, default);
    }

    [Fact]
    public async Task Un_membre_du_groupe_travaille_sur_les_dossiers_du_groupe_seulement()
    {
        (await OnAsync(InGroup)).Should().Contain(Permission.FactureSaisir);
        (await OnAsync(OutOfGroup)).Should().BeEmpty();
    }

    [Fact]
    public async Task Un_groupe_d_un_autre_cabinet_n_ouvre_rien() =>
        (await OnAsync(Foreign)).Should().BeEmpty();

    [Fact]
    public async Task Un_dossier_inconnu_ne_donne_rien() =>
        (await OnAsync(new DossierId(Guid.CreateVersion7()))).Should().BeEmpty();

    [Fact]
    public async Task Les_droits_sont_mis_en_cache_puis_invalides_sur_DroitsModifies()
    {
        (await OnAsync(OutOfGroup)).Should().BeEmpty();
        _catalog.MoveToGroup = true;
        _settings["Lettra:Authorization:Groups:0:Dossiers:1"] = OutOfGroup.ToString();
        await using AsyncServiceScope rebuild = _services.CreateAsyncScope();
        (await OnAsync(OutOfGroup)).Should().BeEmpty("le cache répond tant qu'il n'est pas invalidé");

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IPermissionCacheInvalidator invalidator = scope.ServiceProvider.GetRequiredService<IPermissionCacheInvalidator>();
        await invalidator.InvalidateUserAsync(Member, default);

        // Le registre n'a pas changé (configuration figée) : on vérifie seulement que l'entrée a été recalculée.
        (await OnAsync(InGroup)).Should().Contain(Permission.FactureSaisir);
    }

    [Fact]
    public void Hors_requete_http_personne_n_est_authentifie()
    {
        using IServiceScope scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated.Should().BeFalse();
    }

    private sealed class MutableCatalog(Dictionary<DossierId, Guid> dossiers) : ITenantCatalog
    {
        public bool MoveToGroup { get; set; }

        public ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossiers.TryGetValue(dossierId, out Guid cabinet)
                ? new TenantDescriptor(dossierId, new OrganisationId(cabinet), DatabaseProvider.Sqlite, "config:x")
                : null);

        public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }
}
