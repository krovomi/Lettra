using System.Globalization;
using System.Text;
using Lettra.Contracts.Exports;

namespace Lettra.Exports.Application.AuditKit;

/// <summary>
/// Les textes lisibles du kit, en français (le kit est remis à l'administration fiscale). La description de la piste
/// d'audit fiable est à faire relire par un expert-comptable (voir <c>docs/kit-controle-fiscal.md</c>).
/// </summary>
internal static class AuditKitTexts
{
    public static string ReadMe(AuditKitContent content, AuditKitFecSummary fec, AuditKitIntegritySummary integrity)
    {
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"""
            Kit contrôle fiscal Lettra (format {AuditKitLayout.Format})

            Entreprise : {content.CompanyName} (SIREN {content.Siren})
            Période : du {AuditKitWriter.Day(content.From)} au {AuditKitWriter.Day(content.To)}
            Préparé le : {content.GeneratedAt.ToUniversalTime().ToString("dd/MM/yyyy 'à' HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}

            Verdicts au moment de la préparation
            - FEC : {(fec.IsValid ? "conforme au validateur intégré" : "NON CONFORME")} ({fec.EntryCount.ToString("N0", french)} écritures, {fec.ErrorCount} erreur(s), {fec.WarningCount} avertissement(s)).
            - Chaîne anti-fraude : {(integrity.Intact ? "intacte" : $"{integrity.FindingCount} CONSTAT(S), voir integrite/attestation.json")} ({integrity.EntriesChecked.ToString("N0", french)} entrées, {integrity.ClosingsChecked} clôture(s) et {integrity.DocumentsChecked} pièce(s) vérifiées).
            - Pièces de la période : {content.Trail.Invoices.Count} ; encaissements : {content.Trail.Payments.Count} ; transmissions à la plateforme : {content.Trail.Transmissions.Count}.

            Contenu
            - {AuditKitLayout.Manifest} : ce que contient le kit, les verdicts et l'empreinte SHA-256 de chaque autre fichier.
              L'empreinte SHA-256 de ce manifeste est celle du kit : elle s'affiche à l'écran au téléchargement.
            - fec/ : le fichier des écritures comptables (article A47 A-1 du LPF) et le rapport du validateur intégré.
            - integrite/attestation.json : le contrôle de toute la chaîne anti-fraude, le registre des clés publiques
              (versions retirées comprises) et les clôtures signées qui recouvrent la période.
            - integrite/clotures/ : l'archive scellée de chacune de ces clôtures, vérifiable hors de Lettra
              (voir le LISEZMOI.txt de chaque archive).
            - piste-audit-fiable/ : la description des contrôles (description.txt), les pièces avec leurs auteurs
              (pieces.csv), les encaissements (encaissements.csv) et les entrées de la chaîne de la période (journal.csv).
            - factures/ : le XML CII (EN 16931) de chaque pièce ; quand le kit est préparé depuis l'écran, le PDF/A-3
              Factur-X de chaque pièce, qui embarque ce même XML (comparer l'empreinte du XML joint au PDF à celle
              du manifeste). Les PDF sont des copies lisibles ajoutées après le manifeste : ils n'y figurent pas.
            - plateforme/transmissions.csv : chaque statut reçu de la plateforme agréée, dans l'ordre.

            Vérifier
            1) l'empreinte de chaque fichier est celle du manifeste ; 2) le FEC passe le validateur de
            l'administration (Test Compta Demat) ; 3) chaque archive scellée se vérifie avec la clé publique dont
            l'identifiant figure au registre des clés ; 4) les montants de pieces.csv sont ceux du FEC et du XML.

            Limites
            - Le kit n'est pas horodaté par un tiers (pas de jeton RFC 3161) : l'heure est celle du serveur.
            - Prototype interne : données fictives, rien n'est transmis à l'administration.

            """);
        return text.ToString();
    }

    public const string TrailDescription = """
        Piste d'audit fiable (article 289 du CGI) : les contrôles mis en place par Lettra

        À faire relire par un expert-comptable avant tout usage réel.

        1. Numérotation. Chaque pièce reçoit à sa validation le numéro suivant de sa série (facture, acompte, avoir,
           encaissement), en une transaction avec son entrée au journal : pas de trou, pas de doublon. Le contrôle
           d'intégrité signale tout numéro consommé sans pièce.
        2. Contenu figé. À la validation, la pièce est figée (vendeur, client, lignes, totaux, mentions) et son
           empreinte SHA-256 est enregistrée. Une pièce validée ne se modifie plus : elle se corrige par un avoir.
        3. Journal inaltérable. Chaque validation, émission, annulation, transmission à la plateforme et encaissement
           entre dans un journal chaîné (empreinte de l'entrée précédente), scellé par la clé du dossier. Chaque mois
           est clôturé par une clôture signée (ECDSA P-256) qui reprend les totaux et le total perpétuel. Les clés
           tournent sans casser l'historique : les anciennes restent au registre.
        4. Auteurs. Chaque pièce garde qui l'a créée, validée, émise (et approuvée pour un avoir au-delà du seuil
           du cabinet), avec l'heure : voir pieces.csv. Les droits viennent des rôles attribués dans l'espace
           d'administration.
        5. Transmission. Chaque dépôt à la plateforme agréée et chaque statut reçu sont journalisés ; le contrôle
           d'intégrité compare l'historique en base au journal.
        6. Encaissements. Un encaissement enregistré ne se supprime pas : il se contre-passe, avec son auteur et son
           motif. Une ligne lettrée se délettre avec trace.
        7. Comptabilité. Le FEC est écrit à partir des écritures enregistrées à la validation de chaque pièce et de
           chaque encaissement : il ne recalcule rien.
        8. Contrôle. La chaîne entière est vérifiée chaque nuit et à chaque préparation de ce kit ; un écart lève
           une alerte de conformité pour les administrateurs du cabinet.

        Lien entre la pièce, la facture électronique et le paiement : pieces.csv (numéro, empreinte, statut de la
        plateforme), factures/ (XML et PDF), plateforme/transmissions.csv et encaissements.csv.
        """;
}
