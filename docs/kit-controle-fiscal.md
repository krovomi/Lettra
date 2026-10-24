# Kit contrôle fiscal

Menu **Comptabilité › Déclarations › Contrôle fiscal** (`/tax/audit-kit`). En un clic, Lettra prépare l'archive à
remettre au vérificateur pour une période : le FEC, la preuve que la chaîne anti-fraude est intacte, la piste d'audit
fiable, les factures et le journal des transmissions à la plateforme agréée. **Rien n'est envoyé à l'administration** :
le kit se télécharge, le cabinet le remet lui-même.

Permission demandée : `ArchivesExporter` (le kit contient les archives scellées des clôtures), que donnent les rôles
manager et auditeur, et le niveau « Approuver » de l'espace d'administration.

## Ce que contient le kit

`kit-controle-fiscal-<SIREN>-<du>-<au>.zip`, chaque fichier daté de l'heure de préparation :

| Chemin | Contenu | D'où il vient |
|---|---|---|
| `manifeste.json` | Le dossier, la période, les verdicts, la liste des pièces et l'empreinte SHA-256 de chaque autre fichier. **Son empreinte est celle du kit**, affichée à l'écran. | `AuditKitWriter` |
| `LISEZMOI.txt` | Le même résumé en français, le contenu et la façon de vérifier. | `AuditKitTexts` |
| `fec/<SIREN>FEC<clôture>.txt` | Le FEC de la période. | export FEC existant (`FecExportService`) |
| `fec/rapport-de-validation.json` | Le verdict du validateur intégré. | `FecValidator` |
| `integrite/attestation.json` | Le contrôle de **toute** la chaîne fait à la préparation, le registre des clés publiques (versions retirées comprises) et les clôtures signées qui recouvrent la période. | `DossierIntegrityService`, registre des clés |
| `integrite/clotures/*.zip` | L'archive scellée de chacune de ces clôtures, vérifiable hors de Lettra. | `ISealedArchiveBuilder` |
| `piste-audit-fiable/description.txt` | Les contrôles mis en place (numérotation, contenu figé, journal chaîné et scellé, auteurs, transmissions, encaissements, FEC). | texte fixe |
| `piste-audit-fiable/pieces.csv` | Une ligne par facture, acompte ou avoir validé de la période : qui l'a créé, validé, émis (et approuvé pour un avoir), quand, son empreinte figée et son dernier statut à la plateforme. | pièces de la base du dossier |
| `piste-audit-fiable/encaissements.csv` | Les encaissements datés de la période, contre-passés compris, avec leur auteur. | encaissements (#105) |
| `piste-audit-fiable/journal.csv` | Les entrées de la chaîne datées de la période ou enregistrées pendant la période, avec leurs empreintes. | journal inaltérable |
| `plateforme/transmissions.csv` | Chaque statut reçu de la plateforme agréée, dans l'ordre. | transmissions (phase 5.3) |
| `factures/<numéro>-cii.xml` | Le XML CII (EN 16931) de chaque pièce. | `EInvoiceSerializer` |
| `factures/<numéro>-factur-x.pdf` | Le PDF/A-3 Factur-X de chaque pièce, qui embarque le XML ci-dessus octet pour octet. | rendu par l'écran, comme depuis la fiche de la facture |

Les CSV s'ouvrent dans un tableur français : séparateur `;`, décimales à la virgule, dates ISO, horodatages en UTC,
UTF-8 avec BOM. Une personne s'écrit « Nom (identifiant) » quand le registre du cabinet la connaît ; sur un poste
isolé (mode 1), seulement son identifiant.

## Comment c'est construit

- **Serveur** (`Lettra.Exports.Application/AuditKit`) : `GetAuditKitQuery` → `AuditKitService` rassemble les briques
  existantes (export FEC, contrôle d'intégrité, archives scellées, registre des clés, XML CII) et le port
  `IAuditTrailReader` (EF : `EfAuditTrailReader`) ; `AuditKitWriter`, pur, écrit l'archive. Route
  `GET /api/v1/exports/audit-kit?from=AAAA-MM-JJ&to=AAAA-MM-JJ` (`application/zip`).
- **Écran** (`Pages/Tax/AuditKitPage.razor`) : appelle la passerelle (`IAuditKitGateway`, HTTP ou en processus pour
  l'application installée), puis `AuditKitAssembler` ajoute les PDF Factur-X. Le rendu PDF reste côté client, comme
  partout dans Lettra (le modèle d'impression ne passe pas la frontière client ↔ serveur). Les PDF sont des copies
  lisibles ajoutées après le manifeste : ils n'y figurent pas, et l'empreinte du kit ne change pas.
- La période suit les règles du FEC : bornes comprises, 24 mois au plus ; les refus sont ceux du FEC (fiche
  entreprise, SIREN, période).
- Le contrôle d'intégrité du kit est le même que la passe quotidienne (`IntegrityCheckJob`) : il republie l'état des
  deux alertes de la chaîne. Cette passe tourne dans le Worker, 5 min après son démarrage puis toutes les 24 h ; dans
  une installation sans Worker (la démo Docker), l'API la lance elle-même, 30 s après le démarrage dans la démo, puis
  une fois par jour (PR #163).

## Vérifier un kit

1. L'empreinte SHA-256 de `manifeste.json` est celle notée dans le procès-verbal de remise.
2. L'empreinte de chaque fichier est celle du manifeste (`sha256sum`).
3. Le FEC passe le validateur de l'administration (Test Compta Demat).
4. Chaque archive de `integrite/clotures/` se vérifie avec la clé publique dont l'identifiant figure au registre
   (voir le `LISEZMOI.txt` de chaque archive).
5. Le XML joint à chaque PDF a l'empreinte du fichier `factures/<numéro>-cii.xml`.

## À faire relire par un expert-comptable

- La description de la piste d'audit fiable (`piste-audit-fiable/description.txt`) et les colonnes de `pieces.csv` :
  couvrent-elles ce que l'article 289 du CGI et le BOFiP attendent d'une PAF ?
- La période par défaut (année civile en cours, comme l'export FEC) au lieu de l'exercice des réglages fiscaux.
- Le choix d'inclure, dans `journal.csv`, les entrées enregistrées pendant la période en plus de celles datées de la
  période (transmissions et encaissements tardifs).

## Limites

- Pas d'horodatage par un tiers (jeton RFC 3161) : l'heure est celle du serveur.
- Le kit n'inclut pas encore de pré-contrôle lisible par l'IA (skill `controleur-fiscal`) : il viendra désactivé par
  défaut, comme le reste de la couche IA.
- Les achats ne sont pas dans Lettra : le kit ne couvre que les ventes, les encaissements et la TVA collectée.
- Prototype interne, données fictives.
