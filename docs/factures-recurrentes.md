# Factures récurrentes

Beaucoup de clients d'un cabinet paient un forfait : tenue comptable mensuelle, abonnement, maintenance annuelle. La
vague 7 ajoute des **modèles de facture récurrente** : Lettra prépare la facture de chaque période à son échéance, avec
la période facturée écrite sur la facture et dans la facture électronique. Un écran, dans le groupe **Ventes** du menu :

- **Factures récurrentes** (`/recurring-invoices`) : la liste des modèles, leur prochaine facture et la création des
  factures dues ; la fiche d'un modèle (`/recurring-invoices/{id}`) avec son historique.

## Créer un modèle

1. **Factures récurrentes › Nouveau modèle**.
2. **Informations** : un nom (« Tenue comptable mensuelle »), le client, le régime de TVA et un objet facultatif. La
   période facturée est ajoutée à l'objet de chaque facture, par exemple « Honoraires (du 01/10/2026 au 31/10/2026) ».
3. **Échéancier** :
   - la **fréquence** : mensuelle, trimestrielle ou annuelle ;
   - la **facturation** : *à échoir* (la facture est datée du premier jour de la période) ou *échu* (du dernier jour) ;
   - le **début** et une **fin** facultative : après la fin, aucune nouvelle période n'est facturée (la dernière
     période n'est pas proratisée) ;
   - **valider automatiquement les factures** : décoché par défaut, chaque facture est créée en **brouillon** à
     relire ; coché, elle est validée tout de suite (le numéro reste attribué à la validation, dans la série du
     dossier) ;
   - les conditions de paiement, reprises sur chaque facture.
4. **Lignes** : la même grille que les devis et factures (catalogue, quantités, remise, taux de TVA).
5. **Révision des prix** : aucune, un **pourcentage fixe** par an, ou l'**indice Syntec** (voir plus bas).
6. **Enregistrer** : le message en bas de l'écran confirme la création.

Une fois une première facture créée, le **début** et la **fréquence** ne se modifient plus (les périodes déjà facturées
ne bougent pas). Le client, les lignes, l'objet, les conditions, la fin et la révision changent pour les périodes
suivantes. **Mettre en pause** arrête les créations jusqu'à **Reprendre** ; les périodes passées pendant la pause sont
rattrapées à la reprise. **Supprimer** retire le modèle ; les factures déjà créées restent.

## Les périodes

Chaque période part du jour de début, sans dériver en fin de mois : un contrat commencé le 31 janvier facture du 31/01
au 27/02, puis du 28/02 au 30/03, puis du 31/03 au 29/04, etc. La facture d'une période est **due** à la date de la
facture (début ou fin de période).

La période facturée est enregistrée sur la facture (champs `BillingPeriodStart` et `BillingPeriodEnd` de l'API) et
écrite dans la facture électronique, termes BT-73 et BT-74 de la norme EN 16931 :

- CII (Factur-X) : `ram:BillingSpecifiedPeriod` dans `ApplicableHeaderTradeSettlement` ;
- UBL : `cac:InvoicePeriod`.

La fiche de la facture affiche « Période facturée : du … au … ». La période ne fait pas partie de l'empreinte
anti-fraude de la facture (document canonique v1 inchangé) : elle est aussi dans l'objet, qui, lui, en fait partie.

## Créer les factures dues

- **Depuis l'écran** : un bandeau « N factures récurrentes sont à créer » apparaît sur la liste, avec le bouton
  **Créer les factures dues** ; la fiche d'un modèle a son bouton **Créer la facture due**. Le message en bas de
  l'écran donne le nombre de factures créées et validées.
- **Automatiquement** : une passe toutes les 6 heures crée les factures dues de chaque dossier actif. Elle tourne dans
  le Worker ou, dans une installation sans Worker (la démonstration), dans l'API
  (`Lettra:Outbox:Job:HostedByApi=true`). Réglages `Lettra:Worker:RecurringInvoices` : `Enabled`, `Interval`,
  `InitialDelay` et `ServiceAccountId`.
  - **Sous quel compte** : `ServiceAccountId`, s'il est renseigné, est le compte de service propre à la passe. Il est
    déclaré au registre des droits comme tout utilisateur (`Lettra:Authorization:Users`), sans compte Keycloak
    puisque personne ne s'y connecte, avec un rôle qui donne `FactureSaisir` (et `FactureValider` pour la validation
    automatique). Ce compte ne vaut que dans la portée de la passe ; les requêtes HTTP gardent l'identité de leur
    utilisateur. Sans `ServiceAccountId`, la passe agit sous l'identité de l'hôte (`Lettra:Worker:Identity` du
    Worker). Sans aucune identité, elle ne fait rien et les factures se créent depuis l'écran.
  - **Démonstration** : l'API fait la passe 1 minute après son démarrage puis toutes les 15 minutes, sous le compte
    fictif « Facturation récurrente (automatique) » (rôle personnalisé `facturation-recurrente` : lecture du
    dossier, des clients et du catalogue, saisie et validation des factures). Un modèle dont le début est passé a
    donc ses factures sans clic, au plus tard 15 minutes après son enregistrement.

Dans les deux cas :

- une période n'est facturée **qu'une fois** (clé unique modèle + début de période, et version du modèle) ;
- un modèle en retard rattrape au plus **12 périodes** par passe, puis la suite à la passe suivante ;
- la validation automatique passe par le même cas d'usage que le bouton **Valider** d'une facture : droits
  (`FactureValider`), séparation des tâches, contrôles de conformité, numérotation, journal d'intégrité et écriture de
  vente. Si elle est refusée (droit manquant, fiche entreprise incomplète…), la facture **reste en brouillon** et le
  message le signale ;
- la création demande le droit `FactureSaisir` ; la lecture, `DossierLire`.

L'historique de la fiche liste les factures créées, de la plus récente à la plus ancienne, avec leur numéro (ou
« Brouillon »), leur période et la révision appliquée.

## La liste des modèles

La liste suit les règles communes des listes ([tris et filtres](tris-et-filtres.md)) : un clic sur l'en-tête **Nom du
modèle**, **Client**, **Prochaine facture** ou **Total HT** trie la liste sur le serveur (le montant d'abord du plus
grand), un second clic inverse le sens. La recherche (nom, objet, client) reste visible ; le statut (actifs, en
pause, à facturer) et, sur téléphone, la liste **Trier par** se replient derrière **Filtres**. **Effacer les filtres**
garde le tri choisi.

## Révision annuelle des prix

La révision s'applique à chaque **anniversaire** du contrat (jour de début + 1 an, + 2 ans…), à la première période qui
commence ce jour-là ou après. Les prix révisés sont arrondis au centime, demi au-dessus, et deviennent les prix du
modèle.

- **Pourcentage fixe** : prix × (1 + % / 100), entre 0 et 100 % par an.
- **Indice Syntec** : prix × dernier indice ÷ indice de référence. Les indices sont **saisis** dans la fiche (Lettra
  ne les télécharge pas) : l'indice de référence est celui du contrat, le dernier indice celui publié depuis. Après la
  révision, le dernier indice devient la référence. Tant qu'aucun nouvel indice n'est saisi, la révision **attend** :
  les factures continuent aux prix actuels et la liste comme la fiche affichent « Indice à saisir ». Une révision
  tardive s'applique une fois, l'écart entre les deux indices couvrant déjà les anniversaires passés.

Une révision choisie en cours de contrat ne rattrape pas le passé : elle part du premier anniversaire qui suit la
dernière période facturée.

## API

| Méthode | Route | Droit | Rôle |
| --- | --- | --- | --- |
| GET | `/api/v1/recurring-invoices` | DossierLire | Liste paginée : `search`, `customerId`, `active`, `dueOnly`, `sort` (`NextIssueDate`, `Name`, `Customer`, `Amount`), `descending`, `page`, `pageSize` |
| GET | `/api/v1/recurring-invoices/{id}` | DossierLire | Modèle, prochaine période, historique (`ETag`) |
| POST | `/api/v1/recurring-invoices` | FactureSaisir | Création (201 + `ETag`) |
| PUT | `/api/v1/recurring-invoices/{id}` | FactureSaisir | Modification (`If-Match` obligatoire) |
| PUT | `/api/v1/recurring-invoices/{id}/active` | FactureSaisir | Pause ou reprise (`If-Match`) |
| DELETE | `/api/v1/recurring-invoices/{id}` | FactureSaisir | Suppression (`If-Match`) |
| POST | `/api/v1/recurring-invoices/run` | FactureSaisir | Factures dues de tous les modèles |
| POST | `/api/v1/recurring-invoices/{id}/run` | FactureSaisir | Factures dues d'un modèle |

Codes d'erreur : `RecurringInvoice.NotFound`, `NameRequired`, `NameTooLong`, `FrequencyInvalid`, `TimingInvalid`,
`EndBeforeStart`, `RevisionInvalid`, `RevisionPercentInvalid`, `SyntecIndexInvalid`, `ScheduleLocked` (409),
`NothingDue` (409), plus les codes `Billing.*` des lignes et du client.

## Code

- Domaine : `src/Core/Lettra.Domain/Billing/Recurring/RecurringInvoice.cs` (échéancier, révision, création de la
  facture) et `Invoice.CreateForPeriod`.
- Cas d'usage : `src/Core/Lettra.Application/Features/Billing/RecurringInvoiceFeatures.cs`
  (`GenerateRecurringInvoicesCommand` réutilise `ValidateInvoiceCommand`).
- Persistance : `RecurringInvoiceStores.cs` (tables `RecurringInvoices`, `RecurringInvoiceLines`,
  `RecurringInvoiceOccurrences`), migration `FacturesRecurrentes` sur SQLite, PostgreSQL et SQL Server ; elle ajoute
  aussi les colonnes nullables `BillingPeriodStart` et `BillingPeriodEnd` aux factures, sans toucher aux données.
- Passe de fond : `RecurringInvoiceJob.cs`, enregistrée par `AddRecurringInvoicing(configuration, activeDossiers)`
  après l'identité de l'hôte (le compte de service de la passe enveloppe `ICurrentUser`).
- Écrans : `RecurringInvoiceList.razor`, `RecurringInvoiceEditor.razor`, messages `RecurringToasts.cs`, textes
  `Localization/Modules/Recurrentes(.en).resx`.
- Tests : domaine (`RecurringInvoiceTests`), API (`RecurringInvoiceApiTests`, `GatewayParityTests`), trois bases
  (`RecurringScenarios`), passe de fond et compte de service (`RecurringInvoiceJobTests`), formats
  (`EInvoicePeriodTests`), écrans (`RecurringInvoicePagesTests`).

## Limites connues

- La validation automatique se choisit **par modèle**, pas pour tout le dossier.
- Pas d'envoi par e-mail des factures créées : le port e-mail de la vague 7 existe (`IEmailSender`,
  [relances-et-e-mails.md](relances-et-e-mails.md)), mais les factures récurrentes ne s'en servent pas encore.
- Les modèles ne se synchronisent pas entre appareils : sur un poste isolé (mode 1), ils restent dans la base locale ;
  la réplique d'une facture synchronisée ne porte pas sa période.
- L'indice Syntec est saisi à la main ; aucune proratisation à la fin du contrat.
