using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Customers;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Estampille, numérotation et schémas : scénarios joués à l'identique sur chaque provider.</summary>
internal static class TenancyScenarios
{
    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", null, "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    /// <summary>
    /// Constat 4 du POC : sous concurrence, chaque validation obtient un numéro distinct et la série reste continue.
    /// Une validation qui échoue ne consomme pas de numéro.
    /// </summary>
    public static async Task NumberingAsync(TenantHarness harness, DatabaseProvider provider, string connectionString, int parallel = 24)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope warmUp = await harness.OpenAsync(dossier))
        {
            // Première ouverture : migration et estampille (séries FAC et DEV créées).
            Result<string> failed = await warmUp.ServiceProvider.GetRequiredService<IDocumentNumbering>()
                .ExecuteWithNextNumberAsync<string>(NumberSeriesCodes.Invoices, (_, _) => Task.FromResult<Result<string>>(Error.Validation("Test.Refus")), default);
            failed.IsFailure.Should().BeTrue();
        }

        string[] numbers = await Task.WhenAll(Enumerable.Range(0, parallel).Select(async _ =>
        {
            await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
            Result<string> number = await scope.ServiceProvider.GetRequiredService<IDocumentNumbering>()
                .ExecuteWithNextNumberAsync(NumberSeriesCodes.Invoices, (n, _) => Task.FromResult<Result<string>>(n), default);
            number.IsSuccess.Should().BeTrue();
            return number.Value;
        }));

        numbers.Should().OnlyHaveUniqueItems();
        numbers.Order(StringComparer.Ordinal).Should().Equal(Enumerable.Range(1, parallel).Select(i => $"FAC-{i:D6}"));

        await using AsyncServiceScope check = await harness.OpenAsync(dossier);
        Result<string> missing = await check.ServiceProvider.GetRequiredService<IDocumentNumbering>()
            .ExecuteWithNextNumberAsync(NumberSeriesCodes.Quotes + "X", (n, _) => Task.FromResult<Result<string>>(n), default);
        missing.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Numbering.SeriesNotFound);
    }

    /// <summary>
    /// Une erreur de routage (le catalogue envoie le dossier B sur la base de A) est bloquée par l'estampille avant
    /// toute lecture ou écriture.
    /// </summary>
    public static async Task StampGuardAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId a = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope scope = await harness.OpenAsync(a))
        {
            (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new CreateCustomerCommand(a, Martin), default))
                .IsSuccess.Should().BeTrue();
        }

        DossierId misrouted = harness.AddDossier(provider, connectionString);
        await using AsyncServiceScope wrong = await harness.OpenAsync(misrouted);
        Func<Task> listing = () => wrong.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new ListCustomersQuery(misrouted), default);

        await listing.Should().ThrowAsync<DossierMismatchException>();
    }

    /// <summary>
    /// Constat 2 du POC : deux dossiers dans deux schémas d'une même base, dans le même processus. Sans clé de cache
    /// de modèle par schéma, EF réutiliserait le schéma du premier dossier pour le second.
    /// </summary>
    public static async Task SchemaPerDossierAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId a = harness.AddDossier(provider, connectionString, schema: "dossier_a");
        DossierId b = harness.AddDossier(provider, connectionString, schema: "dossier_b");

        await using (AsyncServiceScope scope = await harness.OpenAsync(a))
        {
            (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new CreateCustomerCommand(a, Martin), default))
                .IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(b))
        {
            Result<PagedResponse<CustomerSummary>> listB = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new ListCustomersQuery(b), default);
            listB.Value.TotalCount.Should().Be(0);
        }
    }
}
