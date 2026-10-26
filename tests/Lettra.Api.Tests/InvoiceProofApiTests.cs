using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Preuve d'authenticité par l'API réelle (mode 2, SQLite) : signée par la clé de la chaîne du dossier, vérifiée
/// contre son registre et ses pièces, et vérifiable hors ligne avec la clé publique au format PEM.
/// </summary>
public sealed class InvoiceProofApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private HttpClient Manager()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static async Task<InvoiceProofCheckResponse> VerifyAsync(HttpClient client, string code)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Api("/api/v1/insights/invoice-proof/verification"), new VerifyInvoiceProofRequest { Code = code }, ContractsJsonContext.Default.VerifyInvoiceProofRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.InvoiceProofCheckResponse))!;
    }

    [Fact]
    public async Task Une_facture_validee_porte_une_preuve_que_le_dossier_et_openssl_reconnaissent()
    {
        using HttpClient manager = Manager();
        Guid id = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{id}"), ContractsJsonContext.Default.InvoiceResponse))!;

        using HttpResponseMessage read = await manager.GetAsync(Api($"/api/v1/insights/invoice-proof/{id}"));
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        InvoiceProofResponse proof = (await read.Content.ReadFromJsonAsync(ContractsJsonContext.Default.InvoiceProofResponse))!;

        proof.Number.Should().Be(invoice.Number);
        proof.Code.Should().StartWith("LETTRA-PREUVE-1;").And.Contain($";{invoice.Number};").And.Contain($";{proof.KeyId};");
        InvoiceProofCheckResponse check = await VerifyAsync(manager, proof.Code);
        check.Verdict.Should().Be(InvoiceProofVerdictDto.Authentic);
        check.InvoiceId.Should().Be(id);
        check.TotalInclTax.Should().Be(invoice.Totals.TotalInclTax);

        // Hors ligne : la clé publique du registre et la signature DER suffisent, sans Lettra.
        IReadOnlyList<InvoiceProofKeyResponse> keys = (await manager.GetFromJsonAsync(
            Api("/api/v1/insights/invoice-proof/keys"), ContractsJsonContext.Default.IReadOnlyListInvoiceProofKeyResponse))!;
        InvoiceProofKeyResponse key = keys.Single(k => k.KeyId == proof.KeyId);
        key.RetiredAt.Should().BeNull();
        using ECDsa verifier = ECDsa.Create();
        verifier.ImportFromPem(key.PublicKeyPem);
        string statement = proof.Code[..proof.Code.LastIndexOf(';')];
        string signature = proof.Code[(proof.Code.LastIndexOf(';') + 1)..].Replace('-', '+').Replace('_', '/');
        byte[] der = Convert.FromBase64String(signature + new string('=', (4 - (signature.Length % 4)) % 4));
        verifier.VerifyData(Encoding.UTF8.GetBytes(statement), der, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence).Should().BeTrue();
    }

    [Fact]
    public async Task Une_preuve_retouchee_ou_venue_d_ailleurs_est_refusee()
    {
        using HttpClient manager = Manager();
        Guid id = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceProofResponse proof = (await manager.GetFromJsonAsync(Api($"/api/v1/insights/invoice-proof/{id}"), ContractsJsonContext.Default.InvoiceProofResponse))!;
        string[] fields = proof.Code.Split(';');

        fields[4] = "1.00";
        (await VerifyAsync(manager, string.Join(';', fields))).Verdict.Should().Be(InvoiceProofVerdictDto.Forged);
        fields[5] = "p256-0000000000000000";
        (await VerifyAsync(manager, string.Join(';', fields))).Verdict.Should().Be(InvoiceProofVerdictDto.Foreign);
        (await VerifyAsync(manager, "BCD\n002")).Verdict.Should().Be(InvoiceProofVerdictDto.Unreadable);
    }

    [Fact]
    public async Task Une_piece_inconnue_n_a_pas_de_preuve()
    {
        using HttpClient manager = Manager();

        using HttpResponseMessage response = await manager.GetAsync(Api($"/api/v1/insights/invoice-proof/{Guid.CreateVersion7()}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
