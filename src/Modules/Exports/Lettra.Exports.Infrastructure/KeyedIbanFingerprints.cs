using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Banking;
using Lettra.Exports.Application.Banking;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Banking;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Clé des empreintes d'IBAN d'un dossier : dérivée (HKDF-SHA256) de la clé maîtresse du cabinet (ADR-0011), liée au
/// cabinet et au dossier, jamais stockée. Lire la base sans la clé maîtresse ne permet donc pas de confirmer un IBAN
/// deviné. Une rotation de la clé maîtresse devra reconvertir les empreintes (même principe que
/// <see cref="UpgradeLegacyAsync"/>, une empreinte se recalcule à partir de la précédente).
/// </summary>
internal sealed class KeyedIbanFingerprints(TenantDbSession session, ITenantContext tenant, IKekStore kekStore) : IIbanFingerprints
{
    private const int KeySize = 32;
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("lettra/banque/empreinte-iban/v1");

    private byte[]? _key;
    private bool _upgraded;

    public async ValueTask<string?> FingerprintAsync(string? iban, CancellationToken cancellationToken)
    {
        string? digest = BankText.IbanDigest(iban, tenant.Current.DossierId.Value);
        if (digest is null)
        {
            return null;
        }

        byte[]? key = await KeyAsync(cancellationToken).ConfigureAwait(false);
        return key is null ? null : BankText.KeyedIbanFingerprint(digest, key);
    }

    public async Task<int> UpgradeLegacyAsync(CancellationToken cancellationToken)
    {
        if (_upgraded)
        {
            return 0;
        }

        byte[]? key = await KeyAsync(cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return 0;
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        int converted = 0;

        // Opérations de relevé : mise à jour conditionnelle, ligne par ligne, sans toucher à leur version (l'empreinte
        // ne change pas de sens) ; deux conversions simultanées donnent la même valeur.
        var lines = await db.Set<BankStatementLine>().AsNoTracking()
            .Where(l => l.CounterpartyIbanHash != null && !l.CounterpartyIbanHash.StartsWith(BankText.KeyedIbanPrefix))
            .Select(l => new { l.Id, Hash = l.CounterpartyIbanHash! })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var line in lines)
        {
            string keyed = BankText.KeyedIbanFingerprint(line.Hash, key);
            converted += await db.Set<BankStatementLine>()
                .Where(l => l.Id == line.Id && l.CounterpartyIbanHash == line.Hash)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.CounterpartyIbanHash, keyed), cancellationToken).ConfigureAwait(false);
        }

        // IBAN connus : l'empreinte fait partie de la clé primaire, on remplace la ligne en gardant sa date d'apprentissage.
        List<KnownCustomerIbanRecord> known = await db.Set<KnownCustomerIbanRecord>().AsNoTracking()
            .Where(k => !k.IbanHash.StartsWith(BankText.KeyedIbanPrefix))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (KnownCustomerIbanRecord record in known)
        {
            string keyed = BankText.KeyedIbanFingerprint(record.IbanHash, key);
            bool present = await db.Set<KnownCustomerIbanRecord>().AsNoTracking()
                .AnyAsync(k => k.CustomerId == record.CustomerId && k.IbanHash == keyed, cancellationToken).ConfigureAwait(false);
            if (!present)
            {
                await db.Set<KnownCustomerIbanRecord>()
                    .Where(k => k.CustomerId == record.CustomerId && k.IbanHash == record.IbanHash)
                    .ExecuteUpdateAsync(u => u.SetProperty(k => k.IbanHash, keyed), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await db.Set<KnownCustomerIbanRecord>()
                    .Where(k => k.CustomerId == record.CustomerId && k.IbanHash == record.IbanHash)
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            converted++;
        }

        _upgraded = true;
        return converted;
    }

    private async ValueTask<byte[]?> KeyAsync(CancellationToken cancellationToken)
    {
        if (_key is not null)
        {
            return _key;
        }

        TenantDescriptor current = tenant.Current;
        byte[]? kek = await kekStore.FindAsync(current.OrganisationId, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return null;
        }

        byte[] salt = [.. current.OrganisationId.Value.ToByteArray(), .. current.DossierId.Value.ToByteArray()];
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, kek, KeySize, salt, Info);
        return _key;
    }
}
