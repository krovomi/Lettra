# Encaissements, rapprochement bancaire et lettrage

Jusqu'ici Lettra savait émettre une facture mais pas dire si elle était payée : le solde du compte client (411) valait
« facturé moins avoirs ». La vague 6 ajoute les règlements reçus, leur écriture de banque, le lettrage du compte client
et le rapprochement avec le relevé de la banque. Deux écrans, dans le groupe **Comptabilité** du menu :

- **Encaissements** (`/payments`) : la liste des règlements reçus, la saisie d'un règlement, sa fiche et sa
  contre-passation ;
- **Rapprochement bancaire** (`/bank`) : l'import d'un relevé, les propositions de Lettra, leur validation.

Aucune connexion à la banque : les relevés viennent de fichiers exportés depuis l'espace en ligne de la banque.

## Saisir un règlement

1. **Encaissements › Saisir un encaissement**, ou le bouton **Imputer** d'une opération du relevé.
2. Choisir le client, la date, le montant reçu et le mode de règlement (virement, chèque, carte bancaire, espèces,
   prélèvement). La référence (numéro de chèque, libellé du virement) est facultative.
3. Lettra répartit le montant sur les factures du client, de l'échéance la plus ancienne à la plus récente. Chaque
   ligne se modifie à la main : un règlement peut solder une facture, en payer une partie, ou couvrir plusieurs
   factures.
4. **Enregistrer** : le message en bas de l'écran donne le numéro de l'encaissement (`ENC-00001`…) et, si les factures
   sont soldées, leur code de lettrage.

Un petit écart entre le montant reçu et le total imputé (frais, arrondi du client) est soldé automatiquement en dessous
du **seuil des petits écarts** (1 € par défaut, réglable de 0 à 100 € sur l'écran Rapprochement bancaire par un
administrateur du catalogue) : la facture est soldée et l'écart passe en 658 (le client a payé moins) ou 758 (il a payé
plus). Au-delà du seuil, l'enregistrement est refusé avec un message : il faut ajuster la répartition.

Un encaissement ne se supprime jamais. **Contre-passer** (fiche de l'encaissement, motif obligatoire, par exemple
« chèque impayé ») passe l'écriture inverse, rouvre les factures, défait le lettrage en gardant la trace (le lettrage
annulé reste en base avec son motif, et un nouveau lettrage relie l'encaissement à sa contre-passation).

## Écritures

Chaque encaissement passe une écriture dans le **journal de banque (BQ)** :

| Compte | Débit | Crédit |
| --- | --- | --- |
| 512 Banque | montant reçu | |
| 658 Charges diverses de gestion courante | écart en moins | |
| 411 Clients | | total imputé |
| 758 Produits divers de gestion courante | | écart en plus |
| 44574 TVA collectée sur encaissements en attente | TVA devenue exigible | |
| 44571 TVA collectée | | TVA devenue exigible |

**TVA sur les encaissements.** Pour une prestation de services sans option pour les débits, la TVA est due à
l'encaissement (art. 269 CGI). Les factures **validées à partir de cette version** passent la TVA des lignes de services
en 44574 au lieu de 44571 ; chaque règlement en fait passer la part payée en 44571, au prorata du TTC encore dû, et le
règlement qui solde la facture prend le reste au centime près. Les biens et les acomptes restent en 44571. Le montant de
TVA imprimé sur la facture ne change pas. **Les écritures déjà enregistrées ne sont jamais recalculées** : une facture
validée avant cette version garde sa TVA en 44571.

Le **brouillon de CA3/CA12** (Comptabilité › Déclarations) suit les mêmes règles : la TVA des services d'une facture
passée en 44574 n'est déclarée qu'à la date de chaque encaissement, pour la part payée (une contre-passation la retire à
sa date). Une facture validée avant cette version reste déclarée à sa date. Le contrôle « accord avec le journal »
compare donc toujours le brouillon au 44571.

Le journal (**Comptabilité › Journal et balance**) montre les écritures BQ à côté des ventes : le numéro mène à la fiche
de l'encaissement, et le code de lettrage s'affiche à côté. La balance et le grand livre comptent les comptes 512, 658,
758 et 44574. Le chiffre d'affaires de la balance ne compte plus que les comptes 70 (pas le 758). Le **FEC** contient
le journal BQ (« Journal de banque »), numéroté à part du journal des ventes, avec `EcritureLet` et `DateLet` remplis
sur les lignes 411 lettrées.

Comptes à faire relire par l'expert-comptable au jalon J2 : 512 unique (un cabinet voudra un 512 par banque), 658/758
pour les écarts, 44574 pour la TVA en attente. Le paiement par escompte (665) n'est pas dans cette version.

## Lettrage

Les écritures du compte d'un client se lettrent dès que toutes les factures d'un groupe sont soldées. Le groupe part de
l'encaissement et s'étend aux factures qu'il règle, à leurs avoirs et aux autres encaissements imputés sur ces
factures. Les codes suivent l'ordre des logiciels comptables, par client : A, B… Z, AA, AB… Une facture payée en deux
fois est lettrée au second règlement, avec les deux.

## Rapprocher le relevé

1. **Rapprochement bancaire › Importer un relevé** : fichier CAMT.053 (XML ISO 20022), OFX/QFX ou CSV, 5 Mo au plus.
   Pour un CSV, Lettra reconnaît seul le séparateur, la virgule décimale, les colonnes Date, Libellé, Montant ou
   Débit/Crédit et l'encodage Windows des banques françaises ; sinon, le bloc **Réglages pour un fichier CSV** donne
   les numéros de colonnes et le format de date. Une opération déjà importée (même date, montant, libellé, référence)
   n'est pas comptée deux fois.
2. Lettra propose une facture pour chaque opération créditrice, avec la raison affichée. Règles, dans l'ordre :
   1. le numéro de facture figure dans le libellé ou la référence (même mal espacé : « FAC 2026 000142 ») ;
   2. le montant égale le reste dû d'une facture et le nom du client figure dans le libellé ou le nom du payeur
      (formes juridiques ignorées) ;
   3. le montant égale le reste dû et l'IBAN du payeur est déjà connu pour ce client (appris à chaque rapprochement
      validé ; seule une empreinte de l'IBAN est conservée) ;
   4. le montant égale la somme de plusieurs factures d'un client reconnu par son nom ou son IBAN.
   Si deux clients sont possibles, Lettra ne propose rien. Un débit (frais, prélèvement fournisseur) n'a jamais de
   proposition.
3. **Rien n'est enregistré sans votre accord** : **Valider** une proposition, ou cocher plusieurs propositions puis
   **Valider la sélection**. Chaque validation crée l'encaissement, son écriture et son lettrage. **Imputer** ouvre la
   saisie pré-remplie pour une opération sans proposition. **Écarter** met de côté une opération qui n'est pas un
   règlement client (réversible avec **Remettre**).

**Démo** : sur la démo navigateur, le bouton **Importer un relevé de démonstration** fabrique un relevé CAMT.053 fictif à
partir des factures dues du dossier : un virement qui cite le numéro, un virement au nom du client, un paiement partiel,
un paiement à 0,40 € près, deux factures payées ensemble, des frais bancaires et une remise de chèque. Le bouton
n'apparaît que là où les données de démonstration sont autorisées ; l'API répond 404 ailleurs.

## Plateforme agréée et vue cabinet

- Quand une facture a été transmise à la plateforme agréée (simulée), son encaissement lui est signalé (statut
  « encaissée »), comme l'impose la réforme pour les prestations de services. Un échec de signalement n'annule pas
  l'encaissement.
- `GET /api/v1/bank/status` donne, pour la vue cabinet multi-dossiers, la date du dernier import, le nombre d'opérations
  à rapprocher et la plus ancienne d'entre elles. Compteurs seulement, aucun montant.

## Intégrité et données

- L'enregistrement et la contre-passation d'un encaissement entrent dans la chaîne d'intégrité (événements
  `PaymentRecorded` et `PaymentReversed`, empreinte SHA-256 de l'encaissement). La clôture de période ne compte pas les
  encaissements dans ses totaux de ventes.
- La migration `Encaissements` ajoute les tables et la série de numéros `ENC-` sans toucher aux données existantes
  (SQLite, SQL Server, PostgreSQL).
- **Hors ligne** : les encaissements se saisissent sur le serveur (navigateur, application reliée au serveur du
  cabinet) ou dans la base locale d'une application installée seule (mode 1). Ils ne sont pas encore synchronisés entre
  appareils en mode réplique.

## Droits

| Action | Permission |
| --- | --- |
| Voir les encaissements, le rapprochement, l'état du rapprochement | `DossierLire` |
| Saisir, contre-passer, importer, valider, écarter | `FactureEmettre` |
| Changer le seuil des petits écarts | `CatalogueGerer` |

## Écrans sur chaque appareil

Les listes passent en cartes sous 768 px (téléphone), les formulaires passent sur deux colonnes à partir de 768 px
(tablette) et trois à partir de 1200 px (bureau). Toutes les créations, modifications et contre-passations s'annoncent
dans le toaster.

| Vérification | État |
| --- | --- |
| API de bout en bout (saisie, TVA, lettrage, FEC, intégrité, import CAMT, propositions, validation, contre-passation, droits) | vérifié par `tests/Lettra.Api.Tests/BankingApiTests.cs` |
| Écrans (répartition, écart soldé, validation unitaire et en masse, écarter, contre-passer, journal) | vérifié par `tests/Lettra.UI.Tests/Banking/BankingPagesTests.cs` |
| Règles de rapprochement, lettrage, lecture CSV/OFX/CAMT, relevé de démonstration | vérifié par les tests unitaires (`Lettra.Domain.Tests/Banking`, `Lettra.Exports.Tests/Banking`) |
| Démo Docker dans un navigateur, applications installées | non vérifié à la main dans cette version |

## Code

- Domaine : `src/Core/Lettra.Domain/Banking` (encaissement, relevé, rapprochement, lettrage) ; calculs purs :
  `src/Core/Lettra.Calculations/PaymentEntries.cs` et `AccountingEntries.cs` (44574).
- Cas d'usage : `src/Modules/Exports/Lettra.Exports.Application/Banking` (lecteurs de relevés dans `Statements/`) ;
  persistance : `src/Modules/Exports/Lettra.Exports.Infrastructure/EfBankingStores.cs` ; TVA à l'encaissement pour les
  déclarations : `src/Modules/Exports/Lettra.Exports.Infrastructure/EfVatOnReceiptsSource.cs`.
- API : `src/Hosts/Lettra.Api/Endpoints/BankingEndpoints.cs` (`/api/v1/payments`, `/api/v1/bank`).
- Écrans : `src/Presentation/Lettra.UI.Shared/Pages/Banking`, styles `wwwroot/lettra-banking.css`, textes
  `Localization/Modules/Encaissements(.en).resx`.
