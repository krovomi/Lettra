using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Lettra.Infrastructure.Persistence.Transfer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Persistence.Sqlite.Transfer;

/// <summary>
/// Export et import d'un dossier entier (réversibilité, plan du mode 5) sur tout hôte qui crée ses dossiers : poste isolé,
/// serveur du cabinet, PostgreSQL dédié, cellule SaaS. Le paquet porte une base SQLite ouverte, quel que soit le provider
/// de départ ou d'arrivée.
/// <para>
/// Export : photographie cohérente du dossier (une transaction de lecture), copiée dans la base du paquet ; les clés de la
/// chaîne sont ouvertes par la KEK du cabinet et rangées dans le paquet chiffré. Import : tout est vérifié avant de créer
/// quoi que ce soit (phrase, archive, schéma, clés, identifiant libre) ; le dossier est créé sous son identifiant d'origine,
/// rempli en une transaction, ses clés réenveloppées par la KEK du cabinet d'arrivée. Un import qui échoue est retiré.
/// Les fichiers de travail (base en clair) ne vivent que le temps de l'opération, dans le répertoire temporaire de l'hôte.
/// </para>
/// </summary>
internal sealed partial class DossierTransfer(
    ITenantCatalog catalog,
    IDossierDirectory directory,
    IDossierDbContextFactory factory,
    IDossierProvisioner provisioner,
    IKekStore keks,
    TimeProvider clock,
    ILogger<DossierTransfer> logger) : IDossierTransfer
{
    private const string PackageSecretRef = "paquet";

    public async Task<Result<DossierPackage>> ExportAsync(OrganisationId organisation, DossierId dossier, string passphrase, CancellationToken cancellationToken)
    {
        TenantDescriptor? tenant = await catalog.FindAsync(dossier, cancellationToken).ConfigureAwait(false);
        if (tenant is null || tenant.OrganisationId != organisation)
        {
            return DossierErrors.NotFound;
        }

        byte[]? kek = await keks.FindAsync(organisation, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return IntegrityErrors.KeyUnavailable;
        }

        string name = (await directory.ListAsync(organisation, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => d.Id == dossier.Value)?.Name ?? dossier.Value.ToString();
        string work = CreateWorkDirectory();
        try
        {
            string databaseFile = Path.Combine(work, DossierPackageFormat.DatabaseEntry);
            DateTimeOffset now = clock.GetUtcNow();
            DossierPackageManifest manifest;
            List<PackagedIntegrityKey> keys;
            LettraDbContext source = await factory.CreateForTransferAsync(tenant, cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                // Une seule transaction de lecture : toutes les tables du même instant, même si le dossier vit pendant
                // l'export. Rejouée en entier par la stratégie de reprise du provider si la connexion tombe.
                Result<(DossierPackageManifest Manifest, List<PackagedIntegrityKey> Keys)> snapshot = await source.Database.CreateExecutionStrategy().ExecuteAsync(
                    token => SnapshotAsync(source, kek, tenant, databaseFile, name, now, token), cancellationToken).ConfigureAwait(false);
                if (snapshot.IsFailure)
                {
                    return Result<DossierPackage>.Failure(snapshot.Errors);
                }

                (manifest, keys) = snapshot.Value;
            }

            string archive = Path.Combine(work, "paquet.zip");
            DossierPackageFormat.WriteArchive(archive, databaseFile, manifest, keys);
            FileStream output = new(
                Path.Combine(Path.GetTempPath(), $"lettra-paquet-{Guid.NewGuid():N}{DossierPackageRules.FileExtension}"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                FileStream zip = File.OpenRead(archive);
                await using (zip.ConfigureAwait(false))
                {
                    await DossierPackageFormat.EncryptAsync(zip, output, passphrase, cancellationToken).ConfigureAwait(false);
                }

                output.Position = 0;
            }
            catch
            {
                await output.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            LogExported(logger, dossier.Value, manifest.Tables.Values.Sum());
            return new DossierPackage(output, FileName(name, now));
        }
        finally
        {
            DeleteWorkDirectory(work);
        }
    }

    public async Task<Result<DossierSummary>> ImportAsync(OrganisationId organisation, Stream package, string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        byte[]? kek = await keks.FindAsync(organisation, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return IntegrityErrors.KeyUnavailable;
        }

        string work = CreateWorkDirectory();
        try
        {
            OpenedDossierPackage opened;
            try
            {
                opened = await OpenAsync(package, passphrase, work, cancellationToken).ConfigureAwait(false);
            }
            catch (DossierPackageException exception)
            {
                LogUnreadable(logger, exception.Message);
                return DossierErrors.PackageUnreadable;
            }

            DossierId id = new(opened.Manifest.DossierId);
            string name = opened.Manifest.Name.Trim();
            List<StoredIntegrityKey>? rows = Rewrap(kek, organisation, id, opened.Keys);
            if (rows is null || id.Value == Guid.Empty || name.Length is 0 or > CreateDossierRequest.MaxNameLength)
            {
                return DossierErrors.PackageUnreadable;
            }

            LettraDbContext source = OpenPackage(opened.DatabaseFile, PackageTenant(id, organisation));
            await using (source.ConfigureAwait(false))
            {
                Result schema = await PrepareSchemaAsync(source, cancellationToken).ConfigureAwait(false);
                if (schema.IsFailure)
                {
                    return Result<DossierSummary>.Failure(schema.Errors);
                }

                if (await catalog.FindAsync(id, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return DossierErrors.AlreadyExists;
                }

                Result<DossierId> created = await provisioner.CreateAsync(organisation, id, name, cancellationToken).ConfigureAwait(false);
                if (created.IsFailure)
                {
                    // Une base dont la création a échoué serait reprise vide par le Worker, et bloquerait un nouvel essai.
                    if (created.Errors.Any(e => e.Code == DossierErrors.ProvisioningFailedCode))
                    {
                        await provisioner.DiscardAsync(id, CancellationToken.None).ConfigureAwait(false);
                    }

                    return Result<DossierSummary>.Failure(created.Errors);
                }

                try
                {
                    await FillAsync(source, id, rows, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await provisioner.DiscardAsync(id, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogImportFailed(logger, exception, id.Value);
                    await provisioner.DiscardAsync(id, CancellationToken.None).ConfigureAwait(false);
                    return DossierErrors.ProvisioningFailed;
                }
            }

            LogImported(logger, id.Value, opened.Manifest.Tables.Values.Sum());
            return new DossierSummary(id.Value, name);
        }
        finally
        {
            DeleteWorkDirectory(work);
        }
    }

    private static async Task<Result<(DossierPackageManifest Manifest, List<PackagedIntegrityKey> Keys)>> SnapshotAsync(
        LettraDbContext source, byte[] kek, TenantDescriptor tenant, string databaseFile, string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        File.Delete(databaseFile);
        source.ChangeTracker.Clear();
        IDbContextTransaction transaction = await source.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            Result<List<PackagedIntegrityKey>> keys = await ExportKeysAsync(source, kek, tenant, cancellationToken).ConfigureAwait(false);
            if (keys.IsFailure)
            {
                return Result<(DossierPackageManifest, List<PackagedIntegrityKey>)>.Failure(keys.Errors);
            }

            LettraDbContext package = OpenPackage(databaseFile, PackageTenant(tenant.DossierId, tenant.OrganisationId));
            await using (package.ConfigureAwait(false))
            {
                await package.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                IReadOnlyDictionary<string, int> tables = await DossierCopier.CopyAsync(source, package, cancellationToken).ConfigureAwait(false);
                string migration = (await package.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Last();
                return (new DossierPackageManifest(DossierPackageFormat.Format, tenant.DossierId.Value, name, now, migration, tables), keys.Value);
            }
        }
    }

    /// <summary>Les clés de la chaîne, ouvertes ; un journal d'avant le versionnage a sa clé dérivée, rangée en version 1.</summary>
    private static async Task<Result<List<PackagedIntegrityKey>>> ExportKeysAsync(
        LettraDbContext source, byte[] kek, TenantDescriptor tenant, CancellationToken cancellationToken)
    {
        List<StoredIntegrityKey> rows = await source.IntegrityKeys.AsNoTracking().OrderBy(k => k.Version).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            bool history = await source.IntegrityJournal.AnyAsync(cancellationToken).ConfigureAwait(false)
                || await source.IntegrityClosings.AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!history)
            {
                return new List<PackagedIntegrityKey>();
            }

            DateTimeOffset since = await source.DossierStamp.AsNoTracking().Select(s => s.CreatedAt).SingleAsync(cancellationToken).ConfigureAwait(false);
            return new List<PackagedIntegrityKey> { Pack(1, LegacyIntegrityKeys.Derive(kek, tenant.OrganisationId), since) };
        }

        List<PackagedIntegrityKey> keys = [];
        foreach (StoredIntegrityKey row in rows)
        {
            if (IntegrityKeyEnvelope.Open(kek, tenant.OrganisationId, tenant.DossierId, row) is not { } material)
            {
                // Une version qui ne s'ouvre plus : le dossier ne sortirait pas vérifiable, l'export est refusé.
                return IntegrityErrors.KeyUnavailable;
            }

            keys.Add(Pack(row.Version, material, row.CreatedAt));
        }

        return keys;
    }

    private static PackagedIntegrityKey Pack(int version, IntegrityKeyMaterial material, DateTimeOffset createdAt)
    {
        byte[] secret = material.ExportSecret();
        try
        {
            return new PackagedIntegrityKey(version, material.KeyId, material.PublicKey, createdAt, Convert.ToBase64String(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>Chaque clé du paquet, vérifiée (sa clé publique doit être celle de sa clé privée) et réenveloppée ; rien si l'une est fausse.</summary>
    private List<StoredIntegrityKey>? Rewrap(byte[] kek, OrganisationId organisation, DossierId dossier, IReadOnlyList<PackagedIntegrityKey> keys)
    {
        List<StoredIntegrityKey> rows = [];
        foreach (PackagedIntegrityKey key in keys)
        {
            byte[] secret;
            try
            {
                secret = Convert.FromBase64String(key.Secret);
            }
            catch (FormatException)
            {
                return null;
            }

            if (secret.Length != 64)
            {
                return null;
            }

            try
            {
                IntegrityKeyMaterial material = IntegrityKeyMaterial.FromSecret(secret);
                if (material.KeyId != key.KeyId || material.PublicKey != key.PublicKey)
                {
                    return null;
                }

                rows.Add(IntegrityKeyEnvelope.Wrap(kek, organisation, dossier, key.Version, material, key.CreatedAt));
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                LogUnreadable(logger, exception.Message);
                return null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        return rows.Select(r => r.Version).Distinct().Count() == rows.Count ? rows : null;
    }

    private static async Task<OpenedDossierPackage> OpenAsync(Stream package, string passphrase, string work, CancellationToken cancellationToken)
    {
        string archive = Path.Combine(work, "paquet.zip");
        FileStream zip = new(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, FileOptions.Asynchronous);
        await using (zip.ConfigureAwait(false))
        {
            await DossierPackageFormat.DecryptAsync(package, zip, passphrase, cancellationToken).ConfigureAwait(false);
        }

        OpenedDossierPackage opened = DossierPackageFormat.ReadArchive(archive, work);
        File.Delete(archive);
        return opened.Manifest.Format == DossierPackageFormat.Format ? opened : throw new DossierPackageException("Format de manifeste inconnu.");
    }

    /// <summary>
    /// Le schéma de la base du paquet : une migration que cet hôte ne connaît pas vient d'une version plus récente (refus) ;
    /// une base plus ancienne est d'abord migrée, dans le paquet, au schéma de cet hôte.
    /// </summary>
    private static async Task<Result> PrepareSchemaAsync(LettraDbContext source, CancellationToken cancellationToken)
    {
        List<string> applied;
        try
        {
            applied = [.. await source.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)];
        }
        catch (SqliteException)
        {
            return DossierErrors.PackageUnreadable;
        }

        if (applied.Count == 0)
        {
            return DossierErrors.PackageUnreadable;
        }

        if (applied.Except(source.Database.GetMigrations(), StringComparer.Ordinal).Any())
        {
            return DossierErrors.PackageTooNew;
        }

        await source.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>Remplit le dossier créé en une transaction : tout le dossier, ou rien.</summary>
    private async Task FillAsync(LettraDbContext source, DossierId id, List<StoredIntegrityKey> keys, CancellationToken cancellationToken)
    {
        TenantDescriptor tenant = await catalog.FindAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Dossier importé introuvable juste après sa création.");
        LettraDbContext target = await factory.CreateForTransferAsync(tenant, cancellationToken).ConfigureAwait(false);
        await using (target.ConfigureAwait(false))
        {
            await target.Database.CreateExecutionStrategy().ExecuteAsync(
                async token =>
                {
                    target.ChangeTracker.Clear();
                    IDbContextTransaction transaction = await target.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                    await using (transaction.ConfigureAwait(false))
                    {
                        await DossierCopier.CopyAsync(source, target, token).ConfigureAwait(false);
                        target.IntegrityKeys.AddRange(keys);
                        await target.SaveChangesAsync(token).ConfigureAwait(false);
                        await transaction.CommitAsync(token).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static TenantDescriptor PackageTenant(DossierId dossier, OrganisationId organisation) =>
        new(dossier, organisation, DatabaseProvider.Sqlite, PackageSecretRef);

    /// <summary>La base du paquet : SQLite en clair (sans DEK), sans intercepteur de dossier ; les champs sensibles y sont lisibles.</summary>
    private static LettraDbContext OpenPackage(string file, TenantDescriptor tenant)
    {
        string connectionString = new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        new SqliteConfigurator().Configure(builder, connectionString, tenant);
        builder.AddInterceptors(new SensitiveFieldInterceptor(cipher: null));
        return new LettraDbContext(builder.Options, tenant);
    }

    /// <summary>Le nom proposé au téléchargement : le nom du dossier, réduit aux lettres, chiffres et tirets, et la date.</summary>
    internal static string FileName(string name, DateTimeOffset at)
    {
        string safe = new([.. name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_')
            .Select(c => c is ' ' or '_' ? '-' : c)]);
        safe = string.Join('-', safe.Split('-', StringSplitOptions.RemoveEmptyEntries));
        safe = safe.Length == 0 ? "dossier" : safe[..Math.Min(safe.Length, 60)];
        return $"{safe}-{at.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}{DossierPackageRules.FileExtension}";
    }

    private static string CreateWorkDirectory() => Directory.CreateTempSubdirectory("lettra-transfert-").FullName;

    private static void DeleteWorkDirectory(string work)
    {
        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (IOException)
        {
            // Un fichier encore ouvert (antivirus, indexation) : le répertoire temporaire du système le retirera.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dossier {DossierId} exporté dans un paquet de réversibilité ({Rows} lignes)")]
    private static partial void LogExported(ILogger logger, Guid dossierId, int rows);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dossier {DossierId} importé d'un paquet de réversibilité ({Rows} lignes)")]
    private static partial void LogImported(ILogger logger, Guid dossierId, int rows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Paquet de réversibilité refusé : {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import du dossier {DossierId} en échec : dossier retiré")]
    private static partial void LogImportFailed(ILogger logger, Exception exception, Guid dossierId);
}
