using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Insights;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// La preuve d'authenticité : le texte du QR code se lit et se relit à l'identique, et la vérification distingue une
/// facture authentique, modifiée, signée par un autre dossier ou décrite autrement que dans le dossier.
/// </summary>
public sealed class InvoiceProofTests
{
    private const string KeyId = "p256-0123456789abcdef";
    private static readonly DateOnly Issued = new(2026, 10, 16);
    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: null, LatePenaltyRate: 10m, EarlyPaymentDiscount: null, Iban: null, Bic: null);

    private readonly Signer _signer = new();
    private readonly IssuedInvoices _issued = new();

    private string Code(string number = "FA-2026-0042", decimal total = 426.94m, string? siren = "552100554")
    {
        string statement = InvoiceProofCode.Statement(siren, number, Issued, total, KeyId);
        return InvoiceProofCode.Code(statement, _signer.Sign(statement));
    }

    private Task<Result<InvoiceProofCheckResponse>> VerifyAsync(string code) =>
        new VerifyInvoiceProofHandler(_signer, _issued, new Profiles(CompanyProfile.Create(Cabinet, DateTimeOffset.UtcNow).Value))
            .HandleAsync(new VerifyInvoiceProofQuery(DossierId.From(Guid.CreateVersion7()), code), default);

    [Fact]
    public void Le_texte_de_la_preuve_se_lit_champ_par_champ_meme_avec_un_point_virgule_dans_le_numero()
    {
        string code = Code("FA;2026%42");

        code.Should().StartWith("LETTRA-PREUVE-1;552100554;FA%3B2026%2542;20261016;426.94;p256-0123456789abcdef;");
        ParsedProof proof = InvoiceProofCode.Parse(code)!;
        proof.Number.Should().Be("FA;2026%42");
        proof.Siren.Should().Be("552100554");
        proof.IssueDate.Should().Be(Issued);
        proof.TotalInclTax.Should().Be(426.94m);
        proof.Statement.Should().Be(code[..code.LastIndexOf(';')]);
        InvoiceProofCode.Parse(Code(siren: null))!.Siren.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("BCD\n002\n1\nSCT")]
    [InlineData("LETTRA-PREUVE-2;552100554;F1;20261016;1.00;p256-0123456789abcdef;AAAA")]
    [InlineData("LETTRA-PREUVE-1;55210055;F1;20261016;1.00;p256-0123456789abcdef;AAAA")]
    [InlineData("LETTRA-PREUVE-1;552100554;;20261016;1.00;p256-0123456789abcdef;AAAA")]
    [InlineData("LETTRA-PREUVE-1;552100554;F1;20261332;1.00;p256-0123456789abcdef;AAAA")]
    [InlineData("LETTRA-PREUVE-1;552100554;F1;20261016;1,00;p256-0123456789abcdef;AAAA")]
    [InlineData("LETTRA-PREUVE-1;552100554;F1;20261016;1.00;cle;AAAA")]
    [InlineData("LETTRA-PREUVE-1;552100554;F1;20261016;1.00;p256-0123456789abcdef;***")]
    public void Un_texte_qui_n_est_pas_une_preuve_est_refuse(string code) => InvoiceProofCode.Parse(code).Should().BeNull();

    [Fact]
    public void La_cle_publique_s_ecrit_en_pem_lisible_par_openssl()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pem = InvoiceProofCode.Pem(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

        pem.Should().StartWith("-----BEGIN PUBLIC KEY-----\n").And.EndWith("-----END PUBLIC KEY-----\n");
        pem.Split('\n').Should().OnlyContain(l => l.Length <= 64);
        using ECDsa read = ECDsa.Create();
        read.ImportFromPem(pem);
        read.ExportSubjectPublicKeyInfo().Should().Equal(key.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public async Task Une_facture_signee_et_identique_a_la_piece_du_dossier_est_authentique()
    {
        _issued.Add("FA-2026-0042", Issued, 426.94m);

        InvoiceProofCheckResponse check = (await VerifyAsync(Code())).Value;

        check.Verdict.Should().Be(InvoiceProofVerdictDto.Authentic);
        check.InvoiceId.Should().Be(_issued.Single.Id.Value);
        check.Differences.Should().BeEmpty();
        check.FullyCredited.Should().BeFalse();
    }

    [Fact]
    public async Task Un_montant_retouche_apres_la_signature_est_une_fausse_preuve()
    {
        _issued.Add("FA-2026-0042", Issued, 426.94m);
        string forged = Code().Replace(";426.94;", ";26.94;", StringComparison.Ordinal);

        InvoiceProofCheckResponse check = (await VerifyAsync(forged)).Value;

        check.Verdict.Should().Be(InvoiceProofVerdictDto.Forged);
        check.TotalInclTax.Should().Be(26.94m, "la vérification dit ce que la preuve prétend");
        check.InvoiceId.Should().BeNull("une fausse preuve ne mène à aucune pièce");
    }

    [Fact]
    public async Task Une_cle_inconnue_du_registre_vient_d_un_autre_dossier()
    {
        _signer.Known = false;

        (await VerifyAsync(Code())).Value.Verdict.Should().Be(InvoiceProofVerdictDto.Foreign);
    }

    [Fact]
    public async Task Une_preuve_signee_que_le_dossier_dement_est_signalee_avec_ses_ecarts()
    {
        (await VerifyAsync(Code())).Value.Differences.Should().Equal("Missing");

        _issued.Add("FA-2026-0042", Issued.AddDays(1), 400m, credited: 400m);
        InvoiceProofCheckResponse check = (await VerifyAsync(Code(siren: "732829320"))).Value;

        check.Verdict.Should().Be(InvoiceProofVerdictDto.Mismatch);
        check.Differences.Should().Equal("IssueDate", "TotalInclTax", "Siren");
        check.FullyCredited.Should().BeTrue();
        (await VerifyAsync("n'importe quoi")).Value.Verdict.Should().Be(InvoiceProofVerdictDto.Unreadable);
    }

    /// <summary>Une clé P-256 de test, connue du registre ou non.</summary>
    private sealed class Signer : IDocumentSigner
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public bool Known { get; set; } = true;

        public byte[] Sign(string statement) => _key.SignData(Encoding.UTF8.GetBytes(statement), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        public Task<Result<DocumentSignature>> SignAsync(Func<string, string> statementFor, CancellationToken cancellationToken)
        {
            string statement = statementFor(KeyId);
            return Task.FromResult<Result<DocumentSignature>>(new DocumentSignature(KeyId, statement, Sign(statement)));
        }

        public Task<Result<DocumentSignatureCheck>> VerifyAsync(string keyId, string statement, byte[] signature, CancellationToken cancellationToken) =>
            Task.FromResult<Result<DocumentSignatureCheck>>(
                !Known || keyId != KeyId ? DocumentSignatureCheck.UnknownKey
                : _key.VerifyData(Encoding.UTF8.GetBytes(statement), signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence) ? DocumentSignatureCheck.Valid
                : DocumentSignatureCheck.Invalid);
    }

    private sealed class IssuedInvoices : IIssuedInvoiceReader
    {
        private readonly List<IssuedInvoice> _invoices = [];

        public IssuedInvoice Single => _invoices.Single();

        public void Add(string number, DateOnly date, decimal total, decimal credited = 0m) =>
            _invoices.Add(new IssuedInvoice(InvoiceId.New(), number, date, total, credited));

        public Task<IssuedInvoice?> FindByNumberAsync(string number, CancellationToken cancellationToken) =>
            Task.FromResult(_invoices.FirstOrDefault(i => i.Number == number));
    }

    private sealed class Profiles(CompanyProfile profile) : ICompanyProfileRepository
    {
        public Task<CompanyProfile?> FindAsync(CancellationToken cancellationToken) => Task.FromResult<CompanyProfile?>(profile);

        public Task AddAsync(CompanyProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
