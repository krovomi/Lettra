using Lettra.Application.Abstractions.Integrity;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Infrastructure.Persistence.Tenancy;

namespace Lettra.Infrastructure.Persistence.Security;

/// <summary>
/// Les pièces signées avec la version en vigueur du registre des clés du dossier, vérifiées avec n'importe quelle
/// version connue : une facture émise avant une rotation reste vérifiable toute la durée légale.
/// </summary>
internal sealed class IntegrityDocumentSigner(EnvelopedIntegrityKeyring keyring, TenantDbSession session) : IDocumentSigner
{
    public async Task<Result<DocumentSignature>> SignAsync(Func<string, string> statementFor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statementFor);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Result<IntegrityKeyMaterial> keys = await keyring.RecordedCurrentAsync(db, cancellationToken).ConfigureAwait(false);
        if (keys.IsFailure)
        {
            return Result<DocumentSignature>.Failure(keys.Errors);
        }

        string statement = statementFor(keys.Value.KeyId);
        return new DocumentSignature(keys.Value.KeyId, statement, keys.Value.SignStatement(statement));
    }

    public async Task<Result<DocumentSignatureCheck>> VerifyAsync(string keyId, string statement, byte[] signature, CancellationToken cancellationToken)
    {
        Result<IIntegrityKeyVerifier> verifier = await keyring.VerifierAsync(cancellationToken).ConfigureAwait(false);
        if (verifier.IsFailure)
        {
            return Result<DocumentSignatureCheck>.Failure(verifier.Errors);
        }

        return verifier.Value.PublicKeyOf(keyId) switch
        {
            null => DocumentSignatureCheck.UnknownKey,
            string publicKey when IntegrityKeyMaterial.VerifyStatement(publicKey, statement, signature) => DocumentSignatureCheck.Valid,
            _ => DocumentSignatureCheck.Invalid,
        };
    }
}
