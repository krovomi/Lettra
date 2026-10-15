using System.Security.Cryptography;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Identity;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Oidc.Offline;

/// <summary>
/// Signe les instantanés avec la clé EC P-256 du serveur et publie les clés publiques (courante et précédentes)
/// pour que les postes les vérifient hors ligne.
/// </summary>
public sealed class EcdsaRightsSnapshotIssuer : IRightsSnapshotIssuer, IDisposable
{
    private readonly RightsSnapshotOptions _options;
    private readonly ECDsa? _signingKey;
    private readonly Dictionary<string, ECDsa> _verificationKeys = new(StringComparer.Ordinal);

    public EcdsaRightsSnapshotIssuer(IOptions<RightsSnapshotOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        if (_options.Validity < RightsSnapshotOptions.MinValidity || _options.Validity > RightsSnapshotOptions.MaxValidity)
        {
            throw new InvalidOperationException($"{RightsSnapshotOptions.SectionName}:Validity doit être compris entre 1 heure et 30 jours.");
        }

        if (!string.IsNullOrWhiteSpace(_options.SigningKeyPem))
        {
            _signingKey = ECDsa.Create();
            _signingKey.ImportFromPem(_options.SigningKeyPem);
        }
        else if (_options.AllowEphemeralKey)
        {
            _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }

        if (_signingKey is not null)
        {
            if (_signingKey.KeySize != 256)
            {
                throw new InvalidOperationException($"{RightsSnapshotOptions.SectionName}:SigningKeyPem doit être une clé EC P-256.");
            }

            _verificationKeys[_options.KeyId] = _signingKey;
        }

        foreach (RightsSnapshotPublicKeyOptions previous in _options.PreviousKeys)
        {
            ECDsa key = ECDsa.Create();
            key.ImportFromPem(previous.PublicKeyPem);
            _verificationKeys.TryAdd(previous.KeyId, key);
        }
    }

    public bool IsAvailable => _signingKey is not null;

    public TimeSpan Validity => _options.Validity;

    public string Sign(RightsSnapshot snapshot)
    {
        ECDsa key = _signingKey ?? throw new InvalidOperationException("Aucune clé de signature des instantanés de droits.");
        return RightsSnapshotJws.Sign(snapshot, _options.Issuer, _options.KeyId, key);
    }

    /// <summary>Clés publiques au format JWK, à publier sans authentification.</summary>
    public RightsSnapshotKeysResponse PublicKeys() => new(_verificationKeys
        .Select(k => RightsSnapshotKeys.ToJwk(k.Key, k.Value))
        .ToList());

    public void Dispose()
    {
        foreach (ECDsa key in _verificationKeys.Values.Append(_signingKey).OfType<ECDsa>().Distinct())
        {
            key.Dispose();
        }
    }
}

/// <summary>Conversion entre JWK (EC P-256) et clés .NET, côté serveur comme côté poste.</summary>
public static class RightsSnapshotKeys
{
    public static RightsSnapshotKey ToJwk(string keyId, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        return new RightsSnapshotKey(
            keyId,
            "EC",
            "P-256",
            System.Buffers.Text.Base64Url.EncodeToString(parameters.Q.X),
            System.Buffers.Text.Base64Url.EncodeToString(parameters.Q.Y),
            RightsSnapshotJws.Algorithm,
            "sig");
    }

    /// <summary>Clés de vérification d'un jeu JWKS ; une clé d'un autre type ou d'une autre courbe est ignorée.</summary>
    public static Dictionary<string, ECDsa> FromJwks(RightsSnapshotKeysResponse jwks)
    {
        ArgumentNullException.ThrowIfNull(jwks);
        Dictionary<string, ECDsa> keys = new(StringComparer.Ordinal);
        foreach (RightsSnapshotKey jwk in jwks.Keys)
        {
            if (jwk is not { Kty: "EC", Crv: "P-256", Alg: RightsSnapshotJws.Algorithm })
            {
                continue;
            }

            keys[jwk.Kid] = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = System.Buffers.Text.Base64Url.DecodeFromChars(jwk.X),
                    Y = System.Buffers.Text.Base64Url.DecodeFromChars(jwk.Y),
                },
            });
        }

        return keys;
    }
}
