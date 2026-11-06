using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Champs sensibles chiffrés par la DEK du dossier (mode 5) : IBAN et e-mails illisibles en base, relus en clair par
/// l'application, valeurs d'avant le chiffrement toujours lisibles, et dossier fermé quand sa clé est détruite.
/// </summary>
internal static class SensitiveFieldScenarios
{
    private const string Iban = "FR7630006000011234567890189";
    private const string SupplierEmail = "factures@dupuis.fr";
    private const string CustomerEmail = "compta@martin.fr";

    public static readonly Supplier.Draft Dupuis = new(
        "Minoterie Dupuis", null, "73282932000074", "FR44732829320", SupplierEmail, Iban,
        "2 chemin du Moulin", "Zone artisanale", "45000", "Orléans", "FR");

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", null, "73282932000074", "FR44732829320", CustomerEmail,
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    public static TenantHarness Harness(IDbProviderConfigurator configurator, DossierFieldKeys keys) =>
        new(configurator, configure: s => s.Replace(ServiceDescriptor.Singleton<IDossierFieldKeys>(keys)));

    /// <summary>Écrits chiffrés, relus en clair ; deux valeurs égales donnent le même chiffré, deux dossiers non.</summary>
    public static async Task EncryptedAtRestAsync(TenantHarness harness, DossierFieldKeys keys, DatabaseProvider provider, string first, string second)
    {
        DossierId dossier = harness.AddDossier(provider, first);
        DossierId other = harness.AddDossier(provider, second);
        keys.Give(dossier);
        keys.Give(other);
        SupplierId supplier = await CreateSupplierAsync(harness, dossier);
        _ = await CreateSupplierAsync(harness, dossier);
        _ = await CreateSupplierAsync(harness, other);
        CustomerId customer = await CreateCustomerAsync(harness, dossier);

        IReadOnlyList<string?[]> suppliers = await RawAsync(harness, dossier, provider, "Suppliers", "Iban", "Email");
        suppliers.Should().HaveCount(2);
        suppliers.SelectMany(r => r).Should().OnlyContain(v => v.StartsWith(SensitiveField.EncryptedPrefix, StringComparison.Ordinal));
        suppliers.SelectMany(r => r).Should().NotContain(v => v.Contains("FR76", StringComparison.Ordinal) || v.Contains("dupuis", StringComparison.Ordinal));
        suppliers[0].Should().Equal(suppliers[1], "le chiffrement déterministe garde les égalités");
        (await RawAsync(harness, other, provider, "Suppliers", "Iban", "Email")).Single()
            .Should().NotIntersectWith(suppliers[0], "chaque dossier a sa propre clé");
        (await RawAsync(harness, dossier, provider, "Customers", "Email")).Single().Single()
            .Should().StartWith(SensitiveField.EncryptedPrefix).And.NotContain("martin");

        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
        SupplierResponse read = (await queries.QueryAsync(new GetSupplierQuery(dossier, supplier), default)).Value;
        read.Iban.Should().Be(Iban);
        read.Email.Should().Be(SupplierEmail);
        (await queries.QueryAsync(new GetCustomerQuery(dossier, customer), default)).Value.Email.Should().Be(CustomerEmail);
    }

    /// <summary>
    /// Une valeur écrite en clair avant le chiffrement reste lisible ; sans DEK (poste, base déclarée sans clé) rien n'est
    /// chiffré ; et une DEK détruite rend illisibles les champs chiffrés : l'effacement cryptographique du dossier.
    /// </summary>
    public static async Task LegacyPlaintextAndDestroyedKeyAsync(TenantHarness harness, DossierFieldKeys keys, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        SupplierId legacy = await CreateSupplierAsync(harness, dossier);
        (await RawAsync(harness, dossier, provider, "Suppliers", "Iban", "Email")).Single().Should().Equal(Iban, SupplierEmail);

        keys.Give(dossier);
        SupplierId encrypted = await CreateSupplierAsync(harness, dossier);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            foreach (SupplierId id in new[] { legacy, encrypted })
            {
                (await queries.QueryAsync(new GetSupplierQuery(dossier, id), default)).Value.Iban.Should().Be(Iban);
            }
        }

        keys.Destroy(dossier);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            Func<Task> read = () => queries.QueryAsync(new GetSupplierQuery(dossier, encrypted), default);
            await read.Should().ThrowAsync<CryptographicException>();
        }
    }

    private static async Task<SupplierId> CreateSupplierAsync(TenantHarness harness, DossierId dossier)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new CreateSupplierCommand(dossier, Dupuis), default)).Value;
    }

    private static async Task<CustomerId> CreateCustomerAsync(TenantHarness harness, DossierId dossier)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value;
    }

    /// <summary>Lit les colonnes telles qu'elles sont stockées, hors de l'intercepteur du contexte.</summary>
    private static async Task<IReadOnlyList<string?[]>> RawAsync(TenantHarness harness, DossierId dossier, DatabaseProvider provider, string table, params string[] columns)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return await RawAsync(await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default), provider, table, columns);
    }

    /// <summary>Lit les colonnes telles qu'elles sont stockées, par la connexion du contexte mais hors de son intercepteur.</summary>
    public static async Task<IReadOnlyList<string?[]>> RawAsync(LettraDbContext db, DatabaseProvider provider, string table, params string[] columns)
    {
        await db.Database.OpenConnectionAsync();
        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        string Quote(string name) => provider == DatabaseProvider.SqlServer ? $"[{name}]" : $"\"{name}\"";
#pragma warning disable CA2100 // Identifiants fixes du test, aucune donnée externe.
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote(table)} ORDER BY {Quote("Id")}";
#pragma warning restore CA2100
        List<string?[]> rows = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add([.. Enumerable.Range(0, columns.Length).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i))]);
        }

        return rows;
    }
}

/// <summary>Les DEK des dossiers de test : données, puis détruites pour fermer un dossier.</summary>
internal sealed class DossierFieldKeys : IDossierFieldKeys
{
    private readonly ConcurrentDictionary<DossierId, byte[]> _keys = new();

    public void Give(DossierId dossier) => _keys[dossier] = RandomNumberGenerator.GetBytes(32);

    public void Destroy(DossierId dossier) => _keys.TryRemove(dossier, out _);

    public ValueTask<byte[]?> FindAsync(TenantDescriptor tenant, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_keys.TryGetValue(tenant.DossierId, out byte[]? key) ? (byte[]?)[.. key] : null);
}
