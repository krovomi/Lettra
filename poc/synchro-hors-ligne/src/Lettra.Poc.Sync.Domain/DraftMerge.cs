using System.Text.Json;
using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Domain;

/// <summary>Lecture et écriture générique des champs d'un brouillon, en JSON.</summary>
public static class DraftFieldAccessor
{
    public static JsonElement Get(Invoice invoice, string field) => field switch
    {
        DraftFields.CustomerName => ToJson(invoice.CustomerName),
        DraftFields.DueDate => ToJson(invoice.DueDate),
        DraftFields.Notes => ToJson(invoice.Notes),
        DraftFields.Lines => ToJson(invoice.Lines),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Champ de brouillon inconnu."),
    };

    public static void Set(Invoice invoice, string field, JsonElement value)
    {
        switch (field)
        {
            case DraftFields.CustomerName: invoice.SetCustomerName(value.Deserialize<string>(SyncHttp.Json) ?? ""); break;
            case DraftFields.DueDate: invoice.SetDueDate(value.Deserialize<DateOnly?>(SyncHttp.Json)); break;
            case DraftFields.Notes: invoice.SetNotes(value.Deserialize<string?>(SyncHttp.Json)); break;
            case DraftFields.Lines: invoice.SetLines(value.Deserialize<List<InvoiceLineDto>>(SyncHttp.Json) ?? []); break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, "Champ de brouillon inconnu.");
        }
    }

    public static JsonElement ToJson<T>(T value) => JsonSerializer.SerializeToElement(value, SyncHttp.Json);
}

public sealed record MergeOutcome(IReadOnlyList<string> Applied, IReadOnlyList<FieldConflict> Conflicts);

/// <summary>
/// Fusion à trois voies, champ par champ. Pour chaque champ modifié hors ligne :
/// le serveur n'y a pas touché → on applique ; même valeur des deux côtés → rien à faire ;
/// valeurs divergentes → conflit sur ce champ seulement. Les autres champs passent.
/// </summary>
public static class DraftMerge
{
    public static MergeOutcome Apply(Invoice server, IReadOnlyDictionary<string, FieldChange> changes)
    {
        var applied = new List<string>();
        var conflicts = new List<FieldConflict>();

        foreach (var (field, change) in changes)
        {
            var current = DraftFieldAccessor.Get(server, field);
            if (JsonElement.DeepEquals(current, change.Mine)) continue;

            if (JsonElement.DeepEquals(current, change.Base))
            {
                DraftFieldAccessor.Set(server, field, change.Mine);
                applied.Add(field);
            }
            else
            {
                conflicts.Add(new FieldConflict(field, change.Base, change.Mine, current));
            }
        }
        return new MergeOutcome(applied, conflicts);
    }
}

/// <summary>
/// Empreinte du contenu d'un brouillon. Une validation différée (navigateur) porte l'empreinte
/// de ce que l'utilisateur a vu : le serveur ne valide que si le brouillon est resté identique.
/// Une version (ETag) ne suffit pas : les propres intentions du poste, rejouées juste avant,
/// font avancer la version sans que l'utilisateur ait rien manqué.
/// </summary>
public static class ContentFingerprint
{
    public static string Of(Invoice invoice)
    {
        var fields = DraftFields.All.ToDictionary(f => f, f => DraftFieldAccessor.Get(invoice, f));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(fields, SyncHttp.Json);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}
