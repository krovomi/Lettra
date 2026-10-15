using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lettra.Domain.Integrity;

/// <summary>
/// Forme canonique et empreintes SHA-256 de la chaîne anti-fraude. Chaque champ est préfixé par sa longueur
/// (<c>12:FAC-000001</c>) : aucune valeur ne peut en imiter une autre, quel que soit son contenu. Les décimaux sont
/// écrits avec quatre décimales et les instants en millisecondes UTC, pour que la relecture depuis n'importe quel
/// provider (SQLite, PostgreSQL, SQL Server) redonne exactement la même empreinte.
/// </summary>
public static class IntegrityHash
{
    /// <summary>Empreinte SHA-256, en hexadécimal minuscule, d'une suite de champs canoniques.</summary>
    public static string Of(params ReadOnlySpan<string?> fields) => Sha256Hex(Canonical(fields));

    /// <summary>Concatène les champs, chacun préfixé par sa longueur ; <c>null</c> s'écrit <c>-</c>.</summary>
    public static string Canonical(params ReadOnlySpan<string?> fields)
    {
        StringBuilder builder = new();
        foreach (string? field in fields)
        {
            if (field is null)
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field);
            }

            builder.Append(';');
        }

        return builder.ToString();
    }

    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string Field(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);

    public static string Field(decimal? value) => value is decimal d ? Field(d) : "-";

    public static string Field(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Field(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Field(Guid value) => value.ToString("D");

    public static string? Field(Guid? value) => value?.ToString("D");

    public static string Field(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? Field(DateOnly? value) => value is DateOnly d ? Field(d) : null;

    public static string Field(DateTimeOffset value) => value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>Ramène un instant à la milliseconde, en UTC : la précision commune aux trois providers.</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value) => DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());
}

/// <summary>
/// Empreinte du contenu figé d'une pièce (lignes, quantités, prix, taux, mentions), calculée par le module qui la
/// possède au moment de la validation, puis recalculée par le contrôle d'intégrité depuis la base : une ligne modifiée
/// directement en base donne une empreinte différente de celle du journal.
/// </summary>
public static class DocumentFingerprint
{
    public static string Compute(params ReadOnlySpan<string?> fields) => IntegrityHash.Of(fields);
}
