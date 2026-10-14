using System.Text.Json;
using Lettra.Ai.Abstractions;
using Lettra.Compliance.Invoices;
using Lettra.Poc.Application;

namespace Lettra.Ai.Tools;

/// <summary>Adapte le dossier du POC aux besoins de l'assistant : company.json virtuel et identités à pseudonymiser.</summary>
public sealed class DossierContextProvider(DossierUseCases useCases) : IDossierContextProvider
{
    private static readonly JsonSerializerOptions Json = new(PaperasseJson.Options)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Task<string> GetCompanyJsonAsync(Guid dossierId, CancellationToken ct = default) =>
        Task.FromResult(JsonSerializer.Serialize(useCases.GetCompany(dossierId), Json));

    public Task<IReadOnlyList<KnownIdentity>> GetKnownIdentitiesAsync(Guid dossierId, CancellationToken ct = default)
    {
        var c = useCases.GetCompany(dossierId);
        var ids = new List<KnownIdentity>
        {
            new(IdentityKind.Company, c.Name ?? ""),
            new(IdentityKind.Address, c.Address ?? ""),
            new(IdentityKind.Siren, c.Siren ?? ""),
            new(IdentityKind.Siret, c.Siret ?? ""),
            new(IdentityKind.VatNumber, c.TvaIntracom ?? ""),
            new(IdentityKind.Iban, c.Payment?.BankDetails?.Iban ?? ""),
        };
        foreach (var client in useCases.GetClients(dossierId))
        {
            ids.Add(new(IdentityKind.Client, client.Name));
            ids.Add(new(IdentityKind.Address, client.Address ?? ""));
            ids.Add(new(IdentityKind.Siren, client.Siren ?? ""));
            ids.Add(new(IdentityKind.VatNumber, client.VatNumber ?? ""));
            ids.Add(new(IdentityKind.Email, client.Email ?? ""));
        }
        return Task.FromResult<IReadOnlyList<KnownIdentity>>(ids.Where(i => i.Value.Length > 0).ToList());
    }
}
