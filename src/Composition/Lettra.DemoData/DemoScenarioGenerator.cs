using System.Globalization;
using System.Text;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;

namespace Lettra.DemoData;

/// <summary>
/// Tire le scénario d'un dossier sur les mois écoulés : même graine et même date du jour, même scénario. Les dates
/// sont des jours ouvrés, aux heures de bureau (Paris), toutes avant la veille : le chargement les rejoue dans l'ordre.
/// </summary>
internal sealed class DemoScenarioGenerator
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    private readonly Random _random;
    private readonly DemoCompany _company;
    private readonly DateOnly _today;
    private readonly List<DateOnly> _businessDays;
    private readonly DateTimeOffset _limit;

    private DemoScenarioGenerator(DemoBusiness business, int seed, DateOnly today, int months)
    {
        // Pas HashCode.Combine : sa graine change à chaque processus, le jeu ne serait plus reproductible.
        _random = new Random(unchecked((seed * 397) + (int)business));
        _company = DemoCompanies.For(business);
        _today = today;
        DateOnly first = today.AddMonths(-months);
        _businessDays = [.. Enumerable.Range(0, today.DayNumber - first.DayNumber)
            .Select(first.AddDays)
            .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)];
        _limit = At(today.AddDays(-1), 19, 0);
    }

    /// <summary>Le scénario d'un dossier du métier <paramref name="business"/>, sur <paramref name="months"/> mois avant <paramref name="today"/>.</summary>
    public static DemoScenario Generate(DemoBusiness business, int seed, DateOnly today, int months = 12)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 2);
        return new DemoScenarioGenerator(business, seed, today, months).Build();
    }

    private DemoScenario Build()
    {
        DateOnly start = _businessDays[0];
        DateTimeOffset startsAt = At(start, 8, 30);
        List<DemoCatalogItem> items = Items(startsAt.AddMinutes(5));
        int[] popularity = Shuffled(items.Count);
        List<DemoCustomer> customers = Customers(items, popularity);
        List<DemoSupplier> suppliers = Suppliers();
        List<DemoPriceList> priceLists = PriceLists(customers, items);
        double[] customerWeights = [.. Shuffled(customers.Count).Select(rank => 1d / Math.Pow(rank + 1, 0.8))];

        List<DemoQuoteStory> quotes = Quotes(customers, customerWeights, items, popularity, priceLists);
        List<DemoInvoiceStory> invoices = DirectInvoices(customers, items, priceLists);

        // Deux articles retirés de la vente la veille : ils restent sur les pièces passées, plus au catalogue courant.
        int[] archived = [.. Shuffled(items.Count).Take(2)];
        return new DemoScenario(
            _company.Business, startsAt, Profile(), items, customers, suppliers, priceLists, quotes, invoices, archived, At(_today.AddDays(-1), 18, 30));
    }

    private SaveCompanyProfileRequest Profile()
    {
        string siren = FrenchIdentifiers.Siren(_company.SirenOrdinal);
        return new SaveCompanyProfileRequest
        {
            Name = _company.Name,
            LegalForm = _company.LegalForm,
            ShareCapital = _company.ShareCapital,
            Siren = siren,
            Siret = FrenchIdentifiers.Siret(siren, 1),
            Rcs = $"RCS {_company.City.Name} {siren[..3]} {siren[3..6]} {siren[6..]}",
            VatNumber = FrenchIdentifiers.VatNumber(siren),
            AddressLine1 = _company.AddressLine1,
            PostalCode = _company.City.PostalCode,
            City = _company.City.Name,
            // Le prestataire de services a opté pour les débits : ses factures et ses avoirs portent la mention.
            VatOnDebits = _company.Business == DemoBusiness.Services,
            VatDeclarationRegime = _company.Business == DemoBusiness.Trading ? VatDeclarationRegimeDto.RealNormal : VatDeclarationRegimeDto.RealSimplified,
            PaymentDays = _company.PaymentDays,
            PaymentTerms = $"Paiement à {_company.PaymentDays} jours par virement",
            LatePenaltyRate = 10m,
            EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
            Iban = FrenchIdentifiers.Iban((int)_company.Business, 10_000 + _company.SirenOrdinal, 1_000_000L * _company.SirenOrdinal),
            Bic = "DEMOFRPPXXX",
        };
    }

    /// <summary>Le catalogue du métier, et une variante pour une partie des articles (gamme, urgence) : 40 à 60 références.</summary>
    private List<DemoCatalogItem> Items(DateTimeOffset at)
    {
        List<DemoCatalogItem> items = [];
        Dictionary<string, int> perFamily = [];
        foreach (DemoItemTemplate template in _company.Items)
        {
            Add(template.Designation, template.Description, template.UnitPrice);
            if (_random.NextDouble() < 0.55)
            {
                (string suffix, decimal factor) = template.IsService
                    ? Pick(("en urgence (sous 48 h)", 1.5m), ("hors heures ouvrées", 1.35m), ("formule renforcée", 1.25m))
                    : Pick(("gamme premium", 1.35m), ("lot professionnel", 0.9m), ("format familial", 1.2m));
                Add($"{template.Designation}, {suffix}", template.Description, decimal.Round(template.UnitPrice * factor, 2));
            }

            void Add(string designation, string? description, decimal price)
            {
                string prefix = Prefix(template.Family);
                int number = perFamily[prefix] = perFamily.GetValueOrDefault(prefix) + 1;
                items.Add(new DemoCatalogItem(
                    new SaveCatalogItemRequest
                    {
                        Reference = string.Create(CultureInfo.InvariantCulture, $"{prefix}-{number:000}"),
                        Designation = designation.Length > 200 ? designation[..200] : designation,
                        Description = description,
                        Kind = template.IsService ? CatalogItemKindDto.Service : CatalogItemKindDto.Good,
                        UnitCode = template.UnitCode,
                        UnitPriceExclTax = price,
                        VatRateCode = template.VatRateCode,
                    },
                    at.AddSeconds(items.Count * 20)));
            }
        }

        return items;
    }

    private List<DemoCustomer> Customers(List<DemoCatalogItem> items, int[] popularity)
    {
        int count = _random.Next(30, 51);
        HashSet<string> names = new(StringComparer.Ordinal);
        List<DemoCustomer> customers = [];
        for (int i = 0; i < count; i++)
        {
            // Une bonne part des clients est là dès le début, les autres arrivent au fil de l'année.
            DateOnly day = i < count * 0.4 ? _businessDays[_random.Next(Math.Min(15, _businessDays.Count))] : Day(0.05, 0.95);
            DemoCity city = Pick(DemoReferenceData.Cities);
            string address = $"{_random.Next(1, 120)} {Pick(DemoReferenceData.StreetTypes)} {Pick(DemoReferenceData.StreetNames)}";
            CreateCustomerRequest request;
            if (_random.NextDouble() < _company.IndividualShare)
            {
                string first = Pick(DemoReferenceData.FirstNames);
                string last = Pick(DemoReferenceData.LastNames);
                string name = Unique(names, $"{first} {last}");
                request = new CreateCustomerRequest
                {
                    Kind = CustomerKindDto.Individual,
                    Name = name,
                    Email = $"{Slug(first)}.{Slug(last)}@particulier.example",
                    AddressLine1 = address,
                    PostalCode = city.PostalCode,
                    City = city.Name,
                };
            }
            else
            {
                string siren = FrenchIdentifiers.Siren((_company.SirenOrdinal * 1_000) + i + 1);
                string name = Unique(names, _random.NextDouble() < 0.5
                    ? $"{Pick(DemoReferenceData.CompanyNameStarts)} {Pick(DemoReferenceData.CompanyNameEnds)}"
                    : $"{Pick(DemoReferenceData.CompanyNameStarts)} {Pick(DemoReferenceData.LastNames)}");
                request = new CreateCustomerRequest
                {
                    Kind = CustomerKindDto.Company,
                    Name = $"{name} {Pick(DemoReferenceData.CompanyForms)}",
                    Siren = siren,
                    Siret = FrenchIdentifiers.Siret(siren, _random.Next(1, 4)),
                    VatNumber = FrenchIdentifiers.VatNumber(siren),
                    Email = $"compta@{Slug(name)}.example",
                    AddressLine1 = address,
                    PostalCode = city.PostalCode,
                    City = city.Name,
                    PreferredLanguage = _random.NextDouble() < 0.05 ? "en-US" : null,
                };
            }

            // Des habitués : les articles populaires reviennent chez beaucoup de clients, les autres sont rares.
            int[] habitual = [.. Enumerable.Range(0, _random.Next(2, 6)).Select(_ => PopularItem(popularity)).Distinct()];
            DemoPayer payer = _random.NextDouble() switch
            {
                < 0.6 => DemoPayer.OnTime,
                < 0.8 => DemoPayer.Late,
                < 0.92 => DemoPayer.InTwoInstalments,
                _ => DemoPayer.Defaulting,
            };
            customers.Add(new DemoCustomer(request, At(day, 8, 40 + _random.Next(0, 20)), payer, habitual));
        }

        return [.. customers.OrderBy(c => c.CreatedAt)];
    }

    private List<DemoSupplier> Suppliers()
    {
        int count = _random.Next(10, 16);
        HashSet<string> names = new(StringComparer.Ordinal);
        List<DemoSupplier> suppliers = [];
        for (int i = 0; i < count; i++)
        {
            string activity = _company.SupplierActivities[i % _company.SupplierActivities.Count];
            string name = Unique(names, $"{activity} {Pick(DemoReferenceData.CompanyNameEnds)}");
            string siren = FrenchIdentifiers.Siren((_company.SirenOrdinal * 1_000) + 500 + i);
            DemoCity city = Pick(DemoReferenceData.Cities);
            suppliers.Add(new DemoSupplier(
                new CreateSupplierRequest
                {
                    Name = $"{name} {Pick(DemoReferenceData.CompanyForms)}",
                    Siren = siren,
                    Siret = FrenchIdentifiers.Siret(siren, 1),
                    VatNumber = FrenchIdentifiers.VatNumber(siren),
                    Email = $"factures@{Slug(name)}.example",
                    Iban = FrenchIdentifiers.Iban(i + 1, _random.Next(10_000, 99_999), _random.NextInt64(1_000_000, 99_999_999_999L)),
                    AddressLine1 = $"{_random.Next(1, 80)} {Pick(DemoReferenceData.StreetTypes)} {Pick(DemoReferenceData.StreetNames)}",
                    PostalCode = city.PostalCode,
                    City = city.Name,
                },
                At(Day(0, 0.2), 14, _random.Next(0, 50))));
        }

        return [.. suppliers.OrderBy(s => s.CreatedAt)];
    }

    /// <summary>Deux grilles : « Partenaires » (-10 %) et « Grands comptes » (-15 %), sur les articles habituels de leurs clients.</summary>
    private List<DemoPriceList> PriceLists(List<DemoCustomer> customers, List<DemoCatalogItem> items)
    {
        DateTimeOffset at = At(_businessDays[Math.Min(25, _businessDays.Count - 1)], 11, 0);
        int[] early = [.. Enumerable.Range(0, customers.Count).Where(i => customers[i].CreatedAt < at && customers[i].IsCompany())];
        List<DemoPriceList> lists = [];
        foreach ((string name, decimal discount, int size) in new[] { ("Partenaires", 10m, 4), ("Grands comptes", 15m, 2) })
        {
            int[] members = [.. early.Where(i => lists.All(l => !l.Customers.Contains(i))).OrderBy(_ => _random.Next()).Take(size)];
            if (members.Length == 0)
            {
                continue;
            }

            int[] covered = [.. members.SelectMany(i => customers[i].HabitualItems).Distinct().Order()];
            if (covered.Length < 3)
            {
                covered = [.. covered.Concat(Shuffled(items.Count).Take(3)).Distinct().Order()];
            }

            lists.Add(new DemoPriceList(name, discount, members, covered, at.AddMinutes(lists.Count * 7)));
        }

        return lists;
    }

    private List<DemoQuoteStory> Quotes(
        List<DemoCustomer> customers, double[] weights, List<DemoCatalogItem> items, int[] popularity, List<DemoPriceList> priceLists)
    {
        int months = _today.Year * 12 + _today.Month - (_businessDays[0].Year * 12 + _businessDays[0].Month);
        int count = (int)Math.Round(_company.QuotesPerYear * months / 12d * (0.9 + (_random.NextDouble() * 0.2)));
        List<DemoQuoteStory> quotes = [];
        for (int i = 0; i < count; i++)
        {
            DateOnly day = SeasonalDay(from: 0.06);
            DateTimeOffset created = At(day, 9, _random.Next(0, 8 * 60));
            int customer = WeightedCustomer(customers, weights, created);
            List<DemoLine> lines = Lines(customers[customer], items, popularity, priceLists, customer, _random.Next(1, 6));
            quotes.Add(QuoteStory(customer, customers[customer].Payer, lines, items, created));
        }

        return [.. quotes.OrderBy(q => q.CreatedAt)];
    }

    private DemoQuoteStory QuoteStory(int customer, DemoPayer payer, List<DemoLine> lines, List<DemoCatalogItem> items, DateTimeOffset created)
    {
        string subject = PickFrom(_company.QuoteSubjects);
        bool recent = created > _limit.AddDays(-9);
        if ((recent && _random.NextDouble() < 0.6) || _random.NextDouble() < 0.02)
        {
            return new DemoQuoteStory(customer, subject, lines, created, null, null, null, null);
        }

        DateTimeOffset validated = created.AddMinutes(_random.Next(15, 120));
        if (validated > _limit)
        {
            return new DemoQuoteStory(customer, subject, lines, created, null, null, null, null);
        }

        // Réponse du client avant la fin de validité (30 jours) ; sans réponse, le devis expire.
        double answer = _random.NextDouble();
        DemoOutcome? outcome = answer < _company.AcceptanceRate ? new DemoOutcome(true, Later(validated, 2, 25))
            : answer < _company.AcceptanceRate + 0.22 ? new DemoOutcome(false, Later(validated, 3, 28))
            : null;
        if (outcome is null || outcome.At > _limit)
        {
            return new DemoQuoteStory(customer, subject, lines, created, validated, null, null, null);
        }

        if (!outcome.Accepted)
        {
            return new DemoQuoteStory(customer, subject, lines, created, validated, outcome, null, null);
        }

        decimal total = lines.Sum(l => l.TotalExclTax(items));
        DemoDownPayment? downPayment = null;
        DateTimeOffset workStarts = outcome.At;
        if (total >= 1_500m && _random.NextDouble() < _company.DownPaymentShare)
        {
            DateTimeOffset createdAt = Later(outcome.At, 1, 4);
            if (createdAt <= _limit)
            {
                decimal percent = _company.Business == DemoBusiness.Building ? Pick(30m, 30m, 40m) : Pick(20m, 30m);
                downPayment = new DemoDownPayment(percent, Settled(createdAt, payer, lines.Count, allowCredit: false, downPayment: true));
                workStarts = createdAt;
            }
        }

        (int min, int max) delay = _company.Business switch
        {
            DemoBusiness.Building => (15, 60),
            DemoBusiness.Services => (7, 45),
            _ => (2, 10),
        };
        DateTimeOffset invoiceAt = Later(workStarts, delay.min, delay.max);
        // Pas d'avoir sur une facture de solde : ses lignes d'acompte déduit ne se créditent pas comme une prestation.
        DemoSettledInvoice? final = invoiceAt <= _limit ? Settled(invoiceAt, payer, lines.Count, allowCredit: downPayment is null, downPayment: false) : null;
        return new DemoQuoteStory(customer, subject, lines, created, validated, outcome, downPayment, final);
    }

    /// <summary>Abonnements mensuels des clients récurrents, et ventes au comptoir pour le négoce.</summary>
    private List<DemoInvoiceStory> DirectInvoices(List<DemoCustomer> customers, List<DemoCatalogItem> items, List<DemoPriceList> priceLists)
    {
        List<DemoInvoiceStory> invoices = [];
        int[] monthlyItems = [.. Enumerable.Range(0, items.Count).Where(i => items[i].Request.UnitCode is "MON")];
        int[] subscribers = [.. Enumerable.Range(0, customers.Count)
            .Where(i => customers[i].IsCompany())
            .OrderBy(_ => _random.Next())
            .Take(_company.RecurringCustomers)];
        foreach (DateOnly firstDay in _businessDays.GroupBy(d => (d.Year, d.Month)).Select(g => g.Min()).Skip(1))
        {
            foreach (int subscriber in subscribers)
            {
                DateTimeOffset at = At(firstDay, 8, 45 + _random.Next(0, 60));
                if (customers[subscriber].CreatedAt > at)
                {
                    continue;
                }

                List<DemoLine> lines = monthlyItems.Length > 0
                    ? [.. monthlyItems.OrderBy(_ => _random.Next()).Take(_random.Next(1, 3)).Select(i => new DemoLine(i, 1m, Discount(priceLists, subscriber, i)))]
                    : Lines(customers[subscriber], items, [.. Enumerable.Range(0, items.Count)], priceLists, subscriber, _random.Next(2, 5), habitualOnly: true);
                string subject = string.Create(CultureInfo.GetCultureInfo("fr-FR"), $"{(monthlyItems.Length > 0 ? "Abonnement" : "Commande mensuelle")} {firstDay:MMMM yyyy}");
                invoices.Add(new DemoInvoiceStory(subscriber, subject, lines, Settled(at, customers[subscriber].Payer, lines.Count, allowCredit: true, downPayment: false)));
            }
        }

        if (_company.Business == DemoBusiness.Trading)
        {
            int sales = _businessDays.Count / 10;
            for (int i = 0; i < sales; i++)
            {
                DateTimeOffset at = At(SeasonalDay(from: 0.02), 10, _random.Next(0, 6 * 60));
                int customer = Enumerable.Range(0, customers.Count).Where(c => customers[c].CreatedAt < at).OrderBy(_ => _random.Next()).FirstOrDefault();
                List<DemoLine> lines = Lines(customers[customer], items, [.. Enumerable.Range(0, items.Count)], priceLists, customer, _random.Next(1, 4));
                invoices.Add(new DemoInvoiceStory(customer, "Vente au comptoir", lines, Settled(at, customers[customer].Payer, lines.Count, allowCredit: false, downPayment: false)));
            }
        }

        // Les deux dernières factures du mois sont encore en préparation : des brouillons à valider.
        invoices = [.. invoices.OrderBy(i => i.Invoice.CreatedAt)];
        for (int i = Math.Max(0, invoices.Count - 2); i < invoices.Count; i++)
        {
            invoices[i] = invoices[i] with { Invoice = new DemoSettledInvoice(invoices[i].Invoice.CreatedAt, null, null, null, []) };
        }

        return invoices;
    }

    /// <summary>
    /// La suite d'une facture créée à <paramref name="created"/> : validée dans l'heure (quelques brouillons récents
    /// restent), transmise à la plateforme, encaissée selon le client, parfois corrigée par un avoir.
    /// </summary>
    private DemoSettledInvoice Settled(DateTimeOffset created, DemoPayer payer, int lineCount, bool allowCredit, bool downPayment)
    {
        DateTimeOffset validated = created.AddMinutes(_random.Next(10, 90));
        if (validated > _limit || (created > _limit.AddDays(-12) && _random.NextDouble() < 0.6))
        {
            return new DemoSettledInvoice(created, null, null, null, []);
        }

        DateTimeOffset transmitted = validated.AddMinutes(_random.Next(5, 600));
        if (transmitted > _limit || _random.NextDouble() < 0.04)
        {
            return new DemoSettledInvoice(created, validated, null, null, []);
        }

        DemoCredit? credit = null;
        if (allowCredit && _random.NextDouble() < 0.07)
        {
            DateTimeOffset at = Later(transmitted, 4, 25);
            if (at <= _limit)
            {
                bool partial = _random.NextDouble() < 0.6;
                credit = partial
                    ? new DemoCredit(at, Pick("Retour de marchandise", "Prestation non réalisée en totalité", "Geste commercial sur une ligne"), _random.Next(1, lineCount + 1), null)
                    : new DemoCredit(at, Pick("Annulation de la commande", "Erreur de facturation", "Litige réglé à l'amiable"), null, null);
            }
        }

        if (credit is { Line: null })
        {
            return new DemoSettledInvoice(created, validated, transmitted, credit, []);
        }

        DateOnly due = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(validated, Paris).DateTime).AddDays(_company.PaymentDays);
        List<DemoPayment> payments = [];
        DemoPayer effective = downPayment && payer == DemoPayer.Defaulting ? DemoPayer.Late : downPayment ? DemoPayer.OnTime : payer;
        switch (effective)
        {
            case DemoPayer.OnTime:
                payments.Add(new DemoPayment(At(due.AddDays(_random.Next(-20, 4)), 10, _random.Next(0, 300)), 1m));
                break;
            case DemoPayer.Late:
                payments.Add(new DemoPayment(At(due.AddDays(_random.Next(12, 70)), 10, _random.Next(0, 300)), 1m));
                break;
            case DemoPayer.InTwoInstalments:
                payments.Add(new DemoPayment(At(due.AddDays(_random.Next(-10, 3)), 10, _random.Next(0, 300)), 0.5m));
                payments.Add(new DemoPayment(At(due.AddDays(_random.Next(20, 45)), 10, _random.Next(0, 300)), 1m));
                break;
            case DemoPayer.Defaulting:
                break;
        }

        // Jamais avant la transmission ni après la veille : un encaissement à venir n'a pas encore eu lieu.
        DateTimeOffset floor = (credit?.At ?? transmitted).AddMinutes(30);
        payments = [.. payments.Select(p => p.At < floor ? p with { At = floor.AddHours(_random.Next(1, 48)) } : p).Where(p => p.At <= _limit).OrderBy(p => p.At)];
        return new DemoSettledInvoice(created, validated, transmitted, credit, payments);
    }

    private List<DemoLine> Lines(
        DemoCustomer customer, List<DemoCatalogItem> items, int[] popularity, List<DemoPriceList> priceLists, int customerIndex, int count, bool habitualOnly = false)
    {
        List<DemoLine> lines = [];
        HashSet<int> used = [];
        for (int i = 0; i < count; i++)
        {
            int item = habitualOnly || _random.NextDouble() < 0.7 ? PickFrom(customer.HabitualItems) : PopularItem(popularity);
            if (!used.Add(item))
            {
                continue;
            }

            lines.Add(new DemoLine(item, Quantity(items[item].Request), Discount(priceLists, customerIndex, item)));
        }

        return lines;
    }

    private static decimal Discount(List<DemoPriceList> priceLists, int customer, int item) =>
        priceLists.FirstOrDefault(l => l.Customers.Contains(customer) && l.Items.Contains(item))?.DiscountPercent ?? 0m;

    private decimal Quantity(SaveCatalogItemRequest item) => item.UnitCode switch
    {
        "HUR" => _random.Next(2, 41),
        "DAY" => _random.Next(1, 16),
        "MTK" => _random.Next(6, 90),
        "MTR" => _random.Next(1, 9),
        "KGM" => _random.Next(5, 201),
        "LTR" => _random.Next(5, 61),
        "KMT" => _random.Next(20, 301),
        "MON" => Pick(1m, 1m, 3m, 12m),
        "LS" => 1m,
        _ => item.UnitPriceExclTax >= 500m ? _random.Next(1, 3) : item.UnitPriceExclTax >= 100m ? _random.Next(1, 6) : _random.Next(2, 25),
    };

    private int WeightedCustomer(List<DemoCustomer> customers, double[] weights, DateTimeOffset at)
    {
        int[] known = [.. Enumerable.Range(0, customers.Count).Where(i => customers[i].CreatedAt < at)];
        if (known.Length == 0)
        {
            return 0;
        }

        double total = known.Sum(i => weights[i]);
        double target = _random.NextDouble() * total;
        foreach (int i in known)
        {
            target -= weights[i];
            if (target <= 0)
            {
                return i;
            }
        }

        return known[^1];
    }

    /// <summary>Un article tiré selon sa popularité (rang dans <paramref name="popularity"/>, loi de Zipf).</summary>
    private int PopularItem(int[] popularity)
    {
        double total = popularity.Sum(rank => 1d / (rank + 1));
        double target = _random.NextDouble() * total;
        for (int i = 0; i < popularity.Length; i++)
        {
            target -= 1d / (popularity[i] + 1);
            if (target <= 0)
            {
                return i;
            }
        }

        return popularity.Length - 1;
    }

    /// <summary>Un jour ouvré pondéré par la saisonnalité, après la fraction <paramref name="from"/> de la période.</summary>
    private DateOnly SeasonalDay(double from)
    {
        int skip = (int)(_businessDays.Count * from);
        DateOnly[] days = [.. _businessDays.Skip(skip)];
        double total = days.Sum(d => DemoReferenceData.MonthlyActivity[d.Month - 1]);
        double target = _random.NextDouble() * total;
        foreach (DateOnly day in days)
        {
            target -= DemoReferenceData.MonthlyActivity[day.Month - 1];
            if (target <= 0)
            {
                return day;
            }
        }

        return days[^1];
    }

    private DateOnly Day(double from, double to)
    {
        int first = (int)(_businessDays.Count * from);
        int last = Math.Max(first + 1, (int)(_businessDays.Count * to));
        return _businessDays[_random.Next(first, last)];
    }

    /// <summary>Entre <paramref name="minDays"/> et <paramref name="maxDays"/> jours plus tard, un jour ouvré, aux heures de bureau.</summary>
    private DateTimeOffset Later(DateTimeOffset from, int minDays, int maxDays)
    {
        DateOnly day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, Paris).DateTime).AddDays(_random.Next(minDays, maxDays + 1));
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return At(day, 9, _random.Next(0, 8 * 60));
    }

    private static DateTimeOffset At(DateOnly day, int hour, int minutes)
    {
        DateTime local = day.ToDateTime(new TimeOnly(hour, 0)).AddMinutes(minutes);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Paris), TimeSpan.Zero);
    }

    private int[] Shuffled(int count)
    {
        int[] values = [.. Enumerable.Range(0, count)];
        _random.Shuffle(values);
        return values;
    }

    private T Pick<T>(params T[] values) => values[_random.Next(values.Length)];

    private T PickFrom<T>(IReadOnlyList<T> values) => values[_random.Next(values.Count)];

    private string Unique(HashSet<string> names, string name)
    {
        string candidate = name;
        while (!names.Add(candidate))
        {
            candidate = $"{name} {Pick(DemoReferenceData.CompanyNameEnds)}";
        }

        return candidate;
    }

    private static string Prefix(string family)
    {
        string plain = Slug(family).Replace("-", string.Empty, StringComparison.Ordinal);
        return plain.Length >= 3 ? plain[..3].ToUpperInvariant() : plain.ToUpperInvariant().PadRight(3, 'X');
    }

    /// <summary>Minuscules sans accents ni espaces, pour les adresses électriques fictives.</summary>
    private static string Slug(string value)
    {
        StringBuilder slug = new(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if ((c == ' ' || c == '-' || c == '\'') && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().Trim('-');
    }
}
