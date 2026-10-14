using Lettra.Compliance.Invoices;

namespace Lettra.Poc.Application;

/// <summary>Données fictives du POC : un cabinet, un dossier client « WebCraft », trois clients et deux factures.</summary>
public static class SampleDossiers
{
    public static readonly Guid CabinetId = Guid.Parse("0f6f3a0e-0000-4000-8000-00000000ca01");
    public static readonly Guid WebCraftId = Guid.Parse("0f6f3a0e-0000-4000-8000-00000000d001");
    public static readonly Guid ClientDupontId = Guid.Parse("0f6f3a0e-0000-4000-8000-00000000c001");
    public static readonly Guid ClientMartinId = Guid.Parse("0f6f3a0e-0000-4000-8000-00000000c002");
    public static readonly Guid ClientNordwindId = Guid.Parse("0f6f3a0e-0000-4000-8000-00000000c003");
    public static readonly Guid InvoiceConformeId = Guid.Parse("0f6f3a0e-0000-4000-8000-00000000f041");
    public static readonly Guid InvoiceIncompleteId = Guid.Parse("0f6f3a0e-0000-4000-8000-00000000f042");

    public static InMemoryDossierStore CreateStore()
    {
        var store = new InMemoryDossierStore();
        store.Add(CreateWebCraft());
        return store;
    }

    public static Dossier CreateWebCraft()
    {
        var dossier = new Dossier
        {
            Id = WebCraftId,
            CabinetId = CabinetId,
            Company = new CompanyProfile
            {
                Name = "WebCraft",
                LegalForm = "SASU",
                Capital = 1000,
                Address = "12 rue du Commerce, 69002 Lyon",
                Siren = "912345678",
                Siret = "91234567800014",
                Rcs = "RCS Lyon",
                Naf = "6201Z",
                TvaIntracom = "FR40912345678",
                Tax = new CompanyTax { RegimeTva = "reel_normal", RegimeIs = "reel_simplifie", TvaRate = 0.20m },
                Invoicing = new CompanyInvoicing { Prefix = "F", AvoirPrefix = "AV" },
                EInvoicing = new CompanyEInvoicing { Pa = "", PaName = "", ReceptionReady = false, EmissionReady = false, EReportingReady = false },
                Payment = new CompanyPayment
                {
                    DefaultTermsLabel = "30 jours date de facture",
                    LatePenaltyRate = "3x_legal",
                    LatePenaltyLabel = "3 fois le taux d'interet legal",
                    EscompteLabel = "Pas d'escompte pour paiement anticipe",
                    RecoveryFee = 40,
                    BankDetails = new BankDetails { Iban = "FR76 3000 6000 0112 3456 7890 189", Bic = "AGRIFRPP" },
                },
            },
        };
        dossier.Clients.Add(new DossierClient(ClientDupontId, "Dupont SARL", "45 avenue de la République, 75011 Paris", "FR", "987654321", "FR11987654321", "compta@dupont-sarl.fr"));
        dossier.Clients.Add(new DossierClient(ClientMartinId, "Jean Martin", "8 impasse des Lilas, 69100 Villeurbanne", "FR", null, null, "jean.martin@example.fr"));
        dossier.Clients.Add(new DossierClient(ClientNordwindId, "Nordwind GmbH", "Hauptstraße 1, 10115 Berlin", "DE", null, "DE123456789", null));

        dossier.Invoices.Add(new StoredInvoice(InvoiceConformeId, new InvoiceDocument
        {
            Number = "F-2026-041", Date = "2026-09-15", DueDate = "2026-10-15", Category = "services",
            Client = new InvoiceClient { Name = "Dupont SARL", Address = "45 avenue de la République, 75011 Paris", Country = "FR", Siren = "987654321" },
            Lines = [new InvoiceLine { Description = "Développement application web", Quantity = 10, Unit = "jours", UnitPrice = 500 }],
            Payment = new InvoicePayment { Terms = "30 jours date de facture", Method = "virement" },
        }));
        // Facture incomplète : SIREN client et catégorie d'opération absents (obligations 2026 distinctes), pas d'échéance.
        dossier.Invoices.Add(new StoredInvoice(InvoiceIncompleteId, new InvoiceDocument
        {
            Number = "F-2026-042", Date = "2026-09-20",
            Client = new InvoiceClient { Name = "Dupont SARL", Address = "45 avenue de la République, 75011 Paris", Country = "FR" },
            Lines = [new InvoiceLine { Description = "Prestation de conseil", Quantity = 1, UnitPrice = 2000 }],
        }));
        return dossier;
    }
}
