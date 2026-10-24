using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Passe de fond des factures récurrentes, sans base : sans identité de service rien n'est créé, un dossier en échec
/// n'arrête pas les autres, un catalogue illisible ne fait rien, la passe se coupe par la configuration, et un hôte
/// sans identité (l'API sans Worker) fait agir la passe sous son propre compte de service.
/// </summary>
public sealed class RecurringInvoiceJobTests
{
    private static readonly TenantDescriptor First = Dossier();
    private static readonly TenantDescriptor Second = Dossier();

    private static TenantDescriptor Dossier() =>
        new(new DossierId(Guid.CreateVersion7()), new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "inutilisé");

    private sealed class Identity(bool authenticated) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;

        public Guid UserId { get; } = Guid.CreateVersion7();
    }

    /// <summary>Rend une réponse par dossier : un échec métier pour le premier, une exception pour un dossier inconnu.</summary>
    private sealed class Dispatcher(ITenantContext tenant) : ICommandDispatcher
    {
        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
        {
            object result = tenant.Current.DossierId == First.DossierId
                ? Result<RecurringRunResponse>.Failure([Error.Conflict("Security.Forbidden")])
                : tenant.Current.DossierId == Second.DossierId
                    ? Result<RecurringRunResponse>.Success(new RecurringRunResponse(
                        [new RecurringRunItem(Guid.CreateVersion7(), "Forfait", Guid.CreateVersion7(), new(2026, 10, 1), new(2026, 10, 31), true, "FAC-000001", null)],
                        [new RecurringRunFailure(Guid.CreateVersion7(), "Autre", "Billing.CustomerRequired")]))
                    : throw new InvalidOperationException("dossier injoignable");
            return Task.FromResult((TResult)result);
        }
    }

    private static RecurringInvoiceJob Job(bool authenticated, Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> dossiers, bool enabled = true)
    {
        ServiceCollection services = new();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ICurrentUser>(_ => new Identity(authenticated));
        services.AddScoped<ICommandDispatcher, Dispatcher>();
        ServiceProvider provider = services.BuildServiceProvider();
        return new RecurringInvoiceJob(provider, provider.GetRequiredService<IServiceScopeFactory>(), new RecurringDossierSource(dossiers), TimeProvider.System,
            Options.Create(new RecurringInvoiceJobOptions { Enabled = enabled, InitialDelay = TimeSpan.Zero, Interval = TimeSpan.FromHours(1) }),
            NullLogger<RecurringInvoiceJob>.Instance);
    }

    private static Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> Returning(params TenantDescriptor[] dossiers) =>
        (_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers);

    [Fact]
    public async Task Un_dossier_refuse_ou_injoignable_n_arrete_pas_la_passe()
    {
        (int created, int validated, int failed) = await Job(true, Returning(First, Dossier(), Second)).RunPassAsync(default);

        created.Should().Be(1);
        validated.Should().Be(1);
        failed.Should().Be(3, "un refus, une exception et un modèle en échec");
    }

    [Fact]
    public async Task Sans_identite_de_service_la_passe_laisse_la_main_a_l_ecran()
    {
        (await Job(false, Returning(Second)).RunPassAsync(default)).Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task Un_catalogue_illisible_ne_cree_rien()
    {
        (await Job(true, (_, _) => throw new InvalidOperationException("catalogue")).RunPassAsync(default)).Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task La_passe_coupee_par_la_configuration_ne_demarre_pas_et_l_enregistrement_lit_sa_section()
    {
        int calls = 0;
        using RecurringInvoiceJob disabled = Job(true, (_, _) => { calls++; return Task.FromResult<IReadOnlyList<TenantDescriptor>>([]); }, enabled: false);
        await disabled.StartAsync(default);
        await disabled.StopAsync(default);
        calls.Should().Be(0);

        using CancellationTokenSource stop = new();
        using RecurringInvoiceJob enabled = Job(true, (_, _) => { calls++; stop.Cancel(); return Task.FromResult<IReadOnlyList<TenantDescriptor>>([]); });
        await enabled.StartAsync(stop.Token);
        await Task.WhenAny(enabled.ExecuteTask ?? Task.CompletedTask, Task.Delay(TimeSpan.FromSeconds(10)));
        calls.Should().Be(1);

        ServiceCollection services = new();
        services.AddRecurringInvoicing(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:Worker:RecurringInvoices:Enabled"] = "false" }).Build(),
            Returning());
        await using ServiceProvider provider = services.AddLogging().BuildServiceProvider();
        provider.GetRequiredService<IOptions<RecurringInvoiceJobOptions>>().Value.Enabled.Should().BeFalse();
        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<RecurringInvoiceJob>();
    }

    /// <summary>Note l'identité vue par le cas d'usage, dans la portée de la passe.</summary>
    private sealed class RecordingDispatcher(ICurrentUser user, List<Guid> seen) : ICommandDispatcher
    {
        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
        {
            seen.Add(user.UserId);
            return Task.FromResult((TResult)(object)Result<RecurringRunResponse>.Success(new RecurringRunResponse([], [])));
        }
    }

    private static ServiceProvider ApiHost(Guid? serviceAccount, List<Guid> seen, Action<IServiceCollection>? hostIdentity = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        (hostIdentity ?? (s => s.AddScoped<ICurrentUser>(_ => new Identity(false))))(services);
        services.AddScoped<ICommandDispatcher>(sp => new RecordingDispatcher(sp.GetRequiredService<ICurrentUser>(), seen));
        Dictionary<string, string?> settings = new() { ["Lettra:Worker:RecurringInvoices:InitialDelay"] = "00:00:00" };
        if (serviceAccount is Guid account)
        {
            settings["Lettra:Worker:RecurringInvoices:ServiceAccountId"] = account.ToString();
        }

        services.AddRecurringInvoicing(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), Returning(First, Second));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Une_API_sans_Worker_cree_les_factures_sous_le_compte_de_service_de_la_passe()
    {
        Guid account = Guid.CreateVersion7();
        List<Guid> seen = [];
        await using ServiceProvider provider = ApiHost(account, seen);

        (int created, int validated, int failed) = await provider.GetRequiredService<RecurringInvoiceJob>().RunPassAsync(default);

        (created, validated, failed).Should().Be((0, 0, 0));
        seen.Should().Equal(account, account);
        await using AsyncServiceScope request = provider.CreateAsyncScope();
        request.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated.Should().BeFalse(
            "hors d'une passe, une requête reste sous l'identité de l'hôte");
    }

    [Fact]
    public async Task Sans_compte_de_service_la_passe_garde_l_identite_de_l_hote()
    {
        List<Guid> seen = [];
        Identity worker = new(true);
        await using ServiceProvider provider = ApiHost(null, seen, s => s.AddSingleton<ICurrentUser>(worker));

        await provider.GetRequiredService<RecurringInvoiceJob>().RunPassAsync(default);

        seen.Should().Equal(worker.UserId, worker.UserId);
        provider.GetService<RecurringJobActor>().Should().BeNull();
    }

    [Fact]
    public async Task L_identite_de_l_hote_enregistree_par_type_reste_celle_des_requetes()
    {
        List<Guid> seen = [];
        await using ServiceProvider provider = ApiHost(Guid.CreateVersion7(), seen, s => s.AddScoped<ICurrentUser, AnonymousHost>());
        await using AsyncServiceScope request = provider.CreateAsyncScope();

        ICurrentUser user = request.ServiceProvider.GetRequiredService<ICurrentUser>();

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().Be(AnonymousHost.Id);
    }

    [Fact]
    public void Un_compte_de_service_sans_identite_d_hote_enregistree_est_refuse()
    {
        Action register = () => new ServiceCollection().AddRecurringInvoicing(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lettra:Worker:RecurringInvoices:ServiceAccountId"] = Guid.CreateVersion7().ToString(),
            }).Build(),
            Returning());

        register.Should().Throw<InvalidOperationException>().WithMessage("*ICurrentUser*");
    }

    private sealed class AnonymousHost : ICurrentUser
    {
        public static readonly Guid Id = Guid.CreateVersion7();

        public bool IsAuthenticated => false;

        public Guid UserId => Id;
    }
}
