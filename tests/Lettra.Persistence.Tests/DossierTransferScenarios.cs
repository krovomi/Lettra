using System.Reflection;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Integrity;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Tenancy;
using Lettra.Infrastructure.Persistence.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Réversibilité : un dossier exporté dans un paquet chiffré, puis importé chez un autre cabinet, arrive entier (mêmes
/// lignes, mêmes pièces) et sa chaîne anti-fraude se vérifie avec les clés réenveloppées par la KEK d'arrivée.
/// </summary>
internal static class DossierTransferScenarios
{
    public const string Passphrase = "cheval agrafe batterie correcte";

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", "732829320", "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealSimplified);

    public static TenantHarness Harness(IDbProviderConfigurator configurator, ManualClock clock, DossierFieldKeys keys, Func<DossierId, string> provision) =>
        new(configurator, clock, provision: provision, configure: s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IDossierFieldKeys>(keys));
            s.AddDossierTransfer();
        });

    /// <summary>
    /// Devis, acompte, facture finale, avoir, fournisseur avec IBAN, journal IA, une clôture signée et une rotation de clé :
    /// tout passe dans le paquet et revient, d'un cabinet à l'autre, avec une DEK et une KEK différentes.
    /// </summary>
    public static async Task RoundTripAsync(TenantHarness harness, ManualClock clock, DossierFieldKeys keys, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        OrganisationId origin = harness.Descriptor(dossier).OrganisationId;
        keys.Give(dossier);
        InvoiceId final = await FillAsync(harness, clock, dossier);

        IntegrityReport before;
        IReadOnlyList<IntegrityKeyVersion> versions;
        InvoiceResponse invoice;
        Dictionary<string, int> rows;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            before = (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value;
            before.Findings.Should().BeEmpty();
            versions = await scope.ServiceProvider.GetRequiredService<IIntegrityKeyring>().ListAsync(default);
            versions.Should().HaveCount(2);
            invoice = (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetInvoiceQuery(dossier, final), default)).Value;
            await using LettraDbContext db = await scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>().CreateAsync(default);
            rows = await CountRowsAsync(db);
        }

        IDossierTransfer transfer = harness.Services.GetRequiredService<IDossierTransfer>();
        (await transfer.ExportAsync(new OrganisationId(Guid.CreateVersion7()), dossier, Passphrase, default)).Errors
            .Should().ContainSingle().Which.Code.Should().Be(DossierErrors.NotFoundCode, "un autre cabinet n'exporte pas ce dossier");
        byte[] package = await ExportAsync(transfer, origin, dossier);

        // Chez un autre cabinet, avec sa propre KEK : refusé tant que le dossier existe ici, puis importé.
        OrganisationId arrival = new(Guid.CreateVersion7());
        await harness.Keks.GetOrCreateAsync(arrival, default);
        (await ImportAsync(transfer, arrival, package)).Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.AlreadyExistsCode);
        await harness.Services.GetRequiredService<IDossierProvisioner>().DiscardAsync(dossier, default);
        keys.Destroy(dossier);
        keys.Give(dossier);
        (await ImportAsync(transfer, arrival, package, "une autre phrase secrète")).Errors
            .Should().ContainSingle().Which.Code.Should().Be(DossierErrors.PackageUnreadableCode);
        (await ImportAsync(transfer, arrival, package[..(package.Length / 2)])).Errors
            .Should().ContainSingle().Which.Code.Should().Be(DossierErrors.PackageUnreadableCode, "un paquet tronqué ne s'ouvre pas");

        Result<DossierSummary> imported = await ImportAsync(transfer, arrival, package);
        imported.Errors.Should().BeEmpty();
        imported.Value.Should().Be(new DossierSummary(dossier.Value, "Dossier à rapatrier"));
        harness.Descriptor(dossier).OrganisationId.Should().Be(arrival);

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>().CreateAsync(default);
            await using (db)
            {
                (await CountRowsAsync(db)).Should().Equal(rows);
                (await SensitiveFieldScenarios.RawAsync(db, provider, "Suppliers", "Iban")).Single().Single()
                    .Should().StartWith(SensitiveField.EncryptedPrefix, "l'IBAN est rechiffré par la DEK du dossier d'arrivée");
            }

            IntegrityReport after = (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value;
            after.Findings.Should().BeEmpty("les clés de la chaîne ont suivi, réenveloppées par la KEK du cabinet d'arrivée");
            after.EntriesChecked.Should().Be(before.EntriesChecked);
            after.ClosingsChecked.Should().Be(before.ClosingsChecked);
            (await scope.ServiceProvider.GetRequiredService<IIntegrityKeyring>().ListAsync(default))
                .Select(v => (v.Version, v.KeyId, v.PublicKey)).Should().Equal(versions.Select(v => (v.Version, v.KeyId, v.PublicKey)));
            (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetInvoiceQuery(dossier, final), default)).Value
                .Should().BeEquivalentTo(invoice);
            (await scope.ServiceProvider.GetRequiredService<IAiAuditLog>().ReadAsync(dossier, 10, default)).Select(e => e.Detail)
                .Should().Equal("Deuxième question", "Première question");
        }

        // Le dossier importé continue : une nouvelle pièce s'ajoute à la chaîne, qui reste continue.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            InvoiceId credit = (await commands.SendAsync(
                new CreateCreditNoteCommand(dossier, final, new CreateCreditNoteRequest { Reason = "Geste commercial", Lines = [new CreditNoteLineRequest(2, 1m)] }),
                default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, credit, null), default)).IsSuccess.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<IAiAuditLog>().ReadAsync(dossier, 10, default)).Should().HaveCount(2);
        }

        await using AsyncServiceScope check = await harness.OpenAsync(dossier);
        IntegrityReport continued = (await check.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value;
        continued.Findings.Should().BeEmpty();
        continued.EntriesChecked.Should().Be(before.EntriesChecked + 1);
    }

    /// <summary>
    /// Un journal d'avant le versionnage des clés (clé dérivée de la KEK, aucune ligne de clé) : sa clé part dans le paquet,
    /// et le dossier importé chez un autre cabinet se vérifie avec elle.
    /// </summary>
    public static async Task LegacyChainAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        OrganisationId origin = harness.Descriptor(dossier).OrganisationId;
        IntegrityKeyMaterial legacy = LegacyIntegrityKeys.Derive(await harness.Keks.GetOrCreateAsync(origin, default), origin);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrityStore>().ListClosingsAsync(default);
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>().CreateAsync(default);
            await using (db)
            {
                JournalEntry e = JournalEntry.ForDocument(IntegrityScenarios.Invoice("FA-1", 100m), 1, JournalEntry.GenesisHash(dossier), DateTimeOffset.UtcNow).Value;
                db.IntegrityJournal.Add(JournalEntry.Restore(e.Sequence, e.OccurredAt, e.Kind, e.DocumentKind, e.DocumentId, e.SeriesCode, e.Number,
                    e.IssueDate, e.CustomerId, (e.TotalExclTax, e.TotalVat, e.TotalInclTax), e.Currency, e.ContentHash, e.RelatedDocumentId,
                    (e.PreviousHash, e.Hash, legacy.Seal(e.Hash)), keyId: null));
                await db.SaveChangesAsync();
                await db.IntegrityChainHead.ExecuteUpdateAsync(s => s.SetProperty(h => h.LastSequence, 1L).SetProperty(h => h.LastHash, e.Hash));
            }
        }

        IDossierTransfer transfer = harness.Services.GetRequiredService<IDossierTransfer>();
        byte[] package = await ExportAsync(transfer, origin, dossier);
        await harness.Services.GetRequiredService<IDossierProvisioner>().DiscardAsync(dossier, default);
        OrganisationId arrival = new(Guid.CreateVersion7());
        await harness.Keks.GetOrCreateAsync(arrival, default);
        (await ImportAsync(transfer, arrival, package)).Errors.Should().BeEmpty();

        await using AsyncServiceScope check = await harness.OpenAsync(dossier);
        (await check.ServiceProvider.GetRequiredService<IIntegrityKeyring>().ListAsync(default)).Should().ContainSingle()
            .Which.KeyId.Should().Be(legacy.KeyId, "la clé dérivée de l'ancien cabinet devient la version 1, enveloppée par le nouveau");
        IntegrityReport report = (await check.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value;
        report.EntriesChecked.Should().Be(1);
        report.Findings.Where(f => f.Kind != IntegrityFindingKind.NumberingGap).Should().BeEmpty();
    }

    private static async Task<InvoiceId> FillAsync(TenantHarness harness, ManualClock clock, DossierId dossier)
    {
        clock.Now = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);
        InvoiceId final;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Guid customer = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new CreateSupplierCommand(dossier, SensitiveFieldScenarios.Dupuis), default)).IsSuccess.Should().BeTrue();
            SaveQuoteRequest request = new()
            {
                CustomerId = customer,
                Subject = "Mission d'accompagnement",
                Lines =
                [
                    new() { Designation = "Audit", Quantity = 4, UnitPriceExclTax = 100m, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" },
                    new() { Designation = "Livre « Gestion »", Quantity = 2, UnitPriceExclTax = 19.99m, VatRateCode = "FR-5.5", Kind = CatalogItemKindDto.Good, UnitCode = "C62" },
                ],
            };
            QuoteId quote = (await commands.SendAsync(new CreateQuoteCommand(dossier, request), default)).Value;
            long version = (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetQuoteQuery(dossier, quote), default)).Value.Version;
            (await commands.SendAsync(new ValidateQuoteCommand(dossier, quote, version), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new SetQuoteOutcomeCommand(dossier, quote, true, null), default)).IsSuccess.Should().BeTrue();
            InvoiceId downPayment = (await commands.SendAsync(new CreateDownPaymentCommand(dossier, quote, new CreateDownPaymentRequest { Percent = 30m }), default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, downPayment, null), default)).IsSuccess.Should().BeTrue();
            final = (await commands.SendAsync(new CreateFinalInvoiceCommand(dossier, quote), default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, final, null), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new IssueInvoiceCommand(dossier, final, null), default)).IsSuccess.Should().BeTrue();

            IAiAuditLog ai = scope.ServiceProvider.GetRequiredService<IAiAuditLog>();
            await ai.AppendAsync(new AiAuditEntry(clock.GetUtcNow(), dossier, Guid.CreateVersion7(), AiAuditKind.Request, "Première question"), default);
            await ai.AppendAsync(new AiAuditEntry(clock.GetUtcNow(), dossier, Guid.CreateVersion7(), AiAuditKind.Request, "Deuxième question"), default);
        }

        // Août clôturé (signé avec la version 1), puis rotation : le paquet porte deux versions de clé.
        clock.Now = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().CloseDuePeriodsAsync(default)).Value.Should().ContainSingle();
            (await scope.ServiceProvider.GetRequiredService<IIntegrityStore>().RotateKeysAsync(default)).Value.Version.Should().Be(2);
        }

        harness.Rename(dossier, "Dossier à rapatrier");
        return final;
    }

    private static async Task<byte[]> ExportAsync(IDossierTransfer transfer, OrganisationId organisation, DossierId dossier)
    {
        Result<DossierPackage> exported = await transfer.ExportAsync(organisation, dossier, Passphrase, default);
        exported.Errors.Should().BeEmpty();
        exported.Value.FileName.Should().EndWith(DossierPackageRules.FileExtension);
        await using Stream content = exported.Value.Content;
        using MemoryStream copy = new();
        await content.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static async Task<Result<DossierSummary>> ImportAsync(IDossierTransfer transfer, OrganisationId organisation, byte[] package, string passphrase = Passphrase)
    {
        using MemoryStream stream = new(package);
        return await transfer.ImportAsync(organisation, stream, passphrase, default);
    }

    /// <summary>Lignes de chaque table copiée (agrégats) : renumérotées ou non, il en arrive autant qu'il en est parti.</summary>
    private static async Task<Dictionary<string, int>> CountRowsAsync(LettraDbContext db)
    {
        MethodInfo set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
        Dictionary<string, int> rows = new(StringComparer.Ordinal);
        foreach (IEntityType table in DossierCopier.Tables(db.Model))
        {
            IQueryable query = (IQueryable)set.MakeGenericMethod(table.ClrType).Invoke(db, null)!;
            rows[table.GetTableName()!] = await query.Cast<object>().CountAsync();
        }

        return rows;
    }
}
