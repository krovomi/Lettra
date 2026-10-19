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
/// n'arrête pas les autres, un catalogue illisible ne fait rien, la passe se coupe par la configuration.
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
        return new RecurringInvoiceJob(provider, provider.GetRequiredService<IServiceScopeFactory>(), new RecurringDossierSource(dossiers),
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
}
