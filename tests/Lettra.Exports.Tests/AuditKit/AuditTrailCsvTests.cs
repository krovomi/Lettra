using System.Text;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Integrity;
using Lettra.Exports.Application.AuditKit;
using Lettra.Exports.Application.EInvoicing.Platform;

namespace Lettra.Exports.Tests.AuditKit;

/// <summary>Les tableaux de la piste d'audit fiable : CSV lisible par un tableur français, valeurs échappées.</summary>
public sealed class AuditTrailCsvTests
{
    private static readonly DateTimeOffset Submitted = new(2026, 10, 15, 8, 30, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(null, "")]
    [InlineData("Atelier Durand", "Atelier Durand")]
    [InlineData("Durand; fils", "\"Durand; fils\"")]
    [InlineData("Le \"Petit\" atelier", "\"Le \"\"Petit\"\" atelier\"")]
    [InlineData("deux\nlignes", "\"deux\nlignes\"")]
    public void Une_valeur_est_echappee_selon_la_rfc_4180(string? value, string expected) =>
        AuditTrailCsv.Escape(value).Should().Be(expected);

    [Fact]
    public void Chaque_statut_d_une_transmission_fait_une_ligne_horodatee_en_utc()
    {
        PlatformTransmission transmission = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "FAC-000001", PlatformChannelDto.EInvoice, EInvoiceFormatDto.Cii, "PA simulée", "REF-1", 360m, Submitted,
            [new PlatformStatusEvent(PlatformStatusDto.Deposited, Submitted), new PlatformStatusEvent(PlatformStatusDto.Collected, Submitted.AddDays(20), Amount: 360m)]);

        string[] lines = Lines(AuditTrailCsv.Transmissions([transmission]));

        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("numero_piece;transmission;plateforme");
        lines[1].Should().Contain("FAC-000001").And.Contain("Facture électronique").And.Contain("Déposée;200;2026-10-15T06:30:00Z");
        lines[2].Should().Contain("Encaissée;212").And.EndWith(";360,00");
    }

    [Fact]
    public void Le_journal_reprend_les_empreintes_de_chaque_entree()
    {
        DocumentLifecycleEvent validated = new(JournalEntryKind.InvoiceValidated, JournaledDocumentKind.Invoice, Guid.CreateVersion7(), "FAC", "FAC-000001",
            new DateOnly(2026, 10, 15), Guid.CreateVersion7(), 300m, 60m, 360m, "EUR", new string('a', JournalEntry.HashLength));
        JournalEntry entry = JournalEntry.ForDocument(validated, 1, new string('0', JournalEntry.HashLength), Submitted).Value;

        string[] lines = Lines(AuditTrailCsv.Journal([entry]));

        lines.Should().HaveCount(2);
        lines[1].Should().StartWith("1;2026-10-15T06:30:00Z;Facture validée;FAC;FAC-000001;2026-10-15;300,00;60,00;360,00;" + new string('a', JournalEntry.HashLength));
    }

    [Fact]
    public void Les_fichiers_commencent_par_le_bom_pour_le_tableur() =>
        AuditTrailCsv.Payments([], new Dictionary<Guid, string>()).Take(3).Should().Equal(Encoding.UTF8.GetPreamble());

    [Theory]
    [InlineData(InvoiceKind.Invoice, "Facture")]
    [InlineData(InvoiceKind.DownPayment, "Facture d'acompte")]
    [InlineData(InvoiceKind.CreditNote, "Avoir")]
    public void Chaque_nature_de_piece_a_son_libelle(InvoiceKind kind, string label) =>
        AuditTrailCsv.KindLabel(kind).Should().Be(label);

    [Fact]
    public void Chaque_evenement_et_chaque_statut_ont_un_libelle_francais()
    {
        Enum.GetValues<JournalEntryKind>().Select(AuditTrailCsv.EventLabel).Should().NotContain(l => Enum.GetNames<JournalEntryKind>().Contains(l));
        Enum.GetValues<PlatformStatusDto>().Select(AuditTrailCsv.StatusLabel).Should().NotContain(l => Enum.GetNames<PlatformStatusDto>().Contains(l));
    }

    [Theory]
    [InlineData("FAC-2026-0001", "factures/FAC-2026-0001-cii.xml")]
    [InlineData("AV/2026 01", "factures/AV_2026_01-cii.xml")]
    public void Un_numero_de_piece_devient_un_nom_de_fichier_sur(string number, string path) =>
        AuditKitLayout.XmlFile(number).Should().Be(path);

    [Fact]
    public void Le_nom_du_kit_porte_le_siren_et_la_periode() =>
        AuditKitLayout.FileName("552100554", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Should().Be("kit-controle-fiscal-552100554-20260101-20261231.zip");

    private static string[] Lines(byte[] csv) =>
        Encoding.UTF8.GetString(csv).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
}
