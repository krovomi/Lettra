using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Identity.Oidc.Offline;

/// <summary>
/// Format de l'instantané : JWS compact, un seul algorithme accepté (ES256, signature IEEE P1363), type explicite.
/// L'en-tête et le contenu sont écrits à la main (pas de sérialisation par réflexion : l'app MAUI est trimée).
/// <code>
/// { "alg": "ES256", "typ": "lettra-rights+jws", "kid": "…" }
/// { "ver": 1, "iss": "lettra", "sub": "…", "dev": "…", "iat": 0, "exp": 0, "dos": { "&lt;dossier&gt;": [1, 2, 10] } }
/// </code>
/// Les permissions sont les valeurs de l'enum <see cref="Permission"/>, stables par construction.
/// </summary>
public static class RightsSnapshotJws
{
    public const string Algorithm = "ES256";
    public const string Type = "lettra-rights+jws";

    /// <summary>Taille maximale acceptée d'un instantané (500 dossiers et toutes leurs permissions tiennent largement).</summary>
    private const int MaxTokenLength = 256 * 1024;

    public static string Sign(RightsSnapshot snapshot, string issuer, string keyId, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(key);
        string header = Encode(Json(w =>
        {
            w.WriteString("alg", Algorithm);
            w.WriteString("typ", Type);
            w.WriteString("kid", keyId);
        }));
        string payload = Encode(Json(w =>
        {
            w.WriteNumber("ver", RightsSnapshot.FormatVersion);
            w.WriteString("iss", issuer);
            w.WriteString("sub", snapshot.UserId);
            w.WriteString("dev", snapshot.DeviceId);
            w.WriteNumber("iat", snapshot.IssuedAt.ToUnixTimeSeconds());
            w.WriteNumber("exp", snapshot.ExpiresAt.ToUnixTimeSeconds());
            w.WriteStartObject("dos");
            foreach ((DossierId dossier, IReadOnlySet<Permission> permissions) in snapshot.Dossiers.OrderBy(d => d.Key.Value))
            {
                w.WriteStartArray(dossier.Value.ToString("D", CultureInfo.InvariantCulture));
                foreach (Permission permission in permissions.Order())
                {
                    w.WriteNumberValue((int)permission);
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }));

        string signingInput = header + "." + payload;
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return signingInput + "." + Encode(signature);
    }

    /// <summary>
    /// Vérifie la signature avec la clé désignée par <c>kid</c> parmi <paramref name="keys"/>, puis lit le contenu.
    /// Tout écart (algorithme, type, clé inconnue, version, champ manquant) rend l'instantané invalide.
    /// </summary>
    public static Result<(RightsSnapshot Snapshot, string Issuer)> Read(string? token, IReadOnlyDictionary<string, ECDsa> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return RightsSnapshotErrors.Invalid;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return RightsSnapshotErrors.Invalid;
        }

        try
        {
            using JsonDocument header = JsonDocument.Parse(Decode(parts[0]));
            JsonElement h = header.RootElement;
            if (h.ValueKind != JsonValueKind.Object
                || !h.TryGetProperty("alg", out JsonElement alg) || alg.GetString() != Algorithm
                || !h.TryGetProperty("typ", out JsonElement typ) || typ.GetString() != Type
                || !h.TryGetProperty("kid", out JsonElement kid) || kid.GetString() is not string keyId
                || !keys.TryGetValue(keyId, out ECDsa? key))
            {
                return RightsSnapshotErrors.Invalid;
            }

            byte[] signature = Decode(parts[2]);
            if (signature.Length != 64
                || !key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return RightsSnapshotErrors.Invalid;
            }

            using JsonDocument payload = JsonDocument.Parse(Decode(parts[1]));
            JsonElement p = payload.RootElement;
            if (p.GetProperty("ver").GetInt32() != RightsSnapshot.FormatVersion)
            {
                return RightsSnapshotErrors.Invalid;
            }

            Dictionary<DossierId, IReadOnlySet<Permission>> dossiers = [];
            foreach (JsonProperty dossier in p.GetProperty("dos").EnumerateObject())
            {
                HashSet<Permission> permissions = [];
                foreach (JsonElement value in dossier.Value.EnumerateArray())
                {
                    int raw = value.GetInt32();
                    // Une valeur inconnue de ce poste (version plus récente) est ignorée : elle ne donne rien.
                    if (Enum.IsDefined(typeof(Permission), raw))
                    {
                        permissions.Add((Permission)raw);
                    }
                }

                dossiers[new DossierId(Guid.ParseExact(dossier.Name, "D"))] = permissions;
            }

            RightsSnapshot snapshot = new(
                p.GetProperty("sub").GetGuid(),
                p.GetProperty("dev").GetGuid(),
                DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("iat").GetInt64()),
                DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("exp").GetInt64()),
                dossiers);
            return (snapshot, p.GetProperty("iss").GetString() ?? string.Empty);
        }
        catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return RightsSnapshotErrors.Invalid;
        }
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);

    private static byte[] Decode(string text) => Base64Url.DecodeFromChars(text);
}
