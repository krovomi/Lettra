# Relances des factures échues et envoi d'e-mails

La vague 7 donne à Lettra une sortie e-mail et s'en sert pour deux choses :

- **relancer les factures échues** : l'écran **Relances** (`/reminders`, groupe **Comptabilité** du menu), l'envoi
  quotidien par le Worker et l'historique de chaque relance ;
- **inviter par e-mail** les personnes ajoutées dans **Équipe et accès**.

En démonstration, les messages ne partent que vers **MailPit**, une boîte de réception locale (http://localhost:8025).
Rien ne sort de la machine.

## Quelles factures sont à relancer

Une facture est **échue** quand son échéance est passée (date du jour à Paris), que ce n'est pas un avoir et qu'il en
reste quelque chose à payer. Le reste dû est celui des encaissements (vague 6) : total TTC, moins les avoirs, moins les
règlements imputés. Une facture soldée n'est donc jamais relancée, et une facture payée en partie est relancée pour
ce qui reste.

## Le scénario de relance

Quatre niveaux se suivent, du plus doux au plus ferme :

| Niveau | Délai par défaut | Ce que dit le message |
| --- | --- | --- |
| Rappel amical | J+3 | « Sauf erreur de notre part… peut-être un simple oubli » |
| Relance | J+15 | La facture reste impayée, merci de la régler rapidement |
| Dernière relance | J+30 | Sans règlement sous huit jours, une mise en demeure suivra. Pénalités citées. |
| Mise en demeure | J+45 | Mise en demeure de payer sous huit jours. Pénalités citées. |

Les délais comptent les jours depuis l'échéance. Ils se règlent par dossier (bloc **Scénario de relance** de l'écran,
droit « gérer le catalogue ») : quatre nombres de 1 à 365, strictement croissants, et un **écart minimal** de 1 à 90
jours (7 par défaut) entre deux relances d'une même facture. Les niveaux montent un par un : une facture très en retard
reçoit d'abord le rappel amical, jamais la mise en demeure en premier.

**L'envoi automatique est coupé par défaut.** Rien ne part chez un client tant que le cabinet ne l'a pas décidé, en
cochant « Envoyer automatiquement chaque jour les relances dues par e-mail ».

### Pénalités de retard

La dernière relance et la mise en demeure citent ce que le retard coûte au client (art. L441-10 du Code de commerce) :

- les **intérêts de retard** au taux annuel figé sur la facture (celui de la fiche entreprise au moment de la
  validation), sur le reste dû, au prorata des jours de retard (base 365), arrondis au centime ;
- l'**indemnité forfaitaire de 40 €** pour frais de recouvrement, pour un client professionnel seulement.

Lettra les **cite**, il ne les facture pas. Les deux premiers niveaux n'en parlent pas. Les textes et le calcul sont à
faire relire par un expert-comptable (jalon J2).

### Langue et contenu

Le message est écrit dans la langue du client : anglais si sa langue préférée commence par `en`, français sinon. Il
reprend le numéro de la facture, sa date, son échéance, le retard, le reste dû et, si la fiche entreprise en a un,
l'IBAN et le BIC pour le virement. Il est signé du nom de l'entreprise, qui est aussi le nom d'expéditeur affiché.
C'est du texte simple, sans pièce jointe.

## L'écran Relances

- **Les factures en retard** : numéro, client et son adresse e-mail, échéance, jours de retard, reste dû, pénalités
  possibles, dernière relance faite et prochain niveau. Le badge « Aujourd'hui » marque celles dont le niveau suivant
  est dû. Recherche (numéro, client, adresse), filtre « À envoyer aujourd'hui seulement », tri en cliquant sur
  l'en-tête (facture, client, échéance, retard, reste dû), 25 factures par page. La liste est chargée en entier et
  triée à l'écran (`ListSorting`). Sur téléphone, le tableau devient une liste de cartes ; le bouton **Filtres** déplie
  le filtre du jour et la liste **Trier par**.
- **Voir la relance** ouvre l'aperçu : destinataire, langue, objet et texte exacts de la prochaine relance. Deux choix :
  - **Envoyer par e-mail** (si le client a une adresse et que l'installation envoie des e-mails) ;
  - **Noter comme envoyée** : la relance a été faite autrement (courrier, téléphone). Elle compte dans le scénario et
    dans l'historique, sans e-mail.
- **Envoyer les relances du jour (N)** envoie toutes les relances dues aujourd'hui. Le message en bas de l'écran dit
  combien sont parties, combien ont échoué et combien de clients n'ont pas d'adresse.
- **Historique** : chaque relance avec sa date, sa facture, son client, son niveau, son canal (e-mail ou noté à la
  main, « automatique » quand le Worker l'a envoyée), son résultat et le texte envoyé. Recherche (numéro, client,
  destinataire, objet, sans tenir compte des accents), filtres par niveau, résultat, canal et période, tri en cliquant
  sur l'en-tête (date, facture, client, niveau). La liste est triée et paginée par le serveur (25 lignes).
- Les deux listes utilisent les briques communes de tri et de filtre (`ListFilterBar`, `SortHeader`, `SortSelect`,
  `DateRangeFilter`, voir `docs/tris-et-filtres.md`) : **Effacer les filtres** apparaît dès qu'un filtre est posé.

Tous les messages de réussite ou d'erreur passent par le toaster. Si l'installation n'envoie pas d'e-mails, un encadré
le dit et seule la relance notée à la main reste possible.

Droits : lire l'écran demande **lire le dossier** ; envoyer, noter ou lancer la passe demande **émettre les factures** ;
régler le scénario demande **gérer le catalogue**.

## Ce qui est gardé

Chaque relance partie ou notée est une ligne de `PaymentReminders`, jamais modifiée : facture, client, niveau, canal,
résultat, destinataire, langue, objet et texte envoyés, reste dû et retard du jour, déclencheur (personne ou Worker),
auteur et date.

- Un **e-mail refusé** par le serveur est gardé avec le résultat « En échec » et son code d'erreur. Il ne compte pas
  comme une relance faite : le niveau sera retenté.
- Un client **sans adresse**, ou une installation **sans envoi d'e-mails**, ne laisse aucune ligne : rien n'a été tenté.

Le scénario du dossier est une ligne unique de `ReminderSettings`, créée au premier réglage (versionnée, comme le reste
des réglages). Tant qu'elle n'existe pas, les valeurs par défaut s'appliquent.

La migration `Relances` (SQLite, SQL Server, PostgreSQL) **crée** ces deux tables et leurs index. Elle ne touche à
aucune donnée existante.

## L'envoi quotidien par le Worker

`PaymentReminderJob` passe sur chaque dossier actif toutes les 12 heures (première passe 2 minutes après le démarrage).
Il n'agit que sur les dossiers où l'envoi automatique est coché, et seulement si l'installation envoie des e-mails.
Pour chaque facture dont le niveau suivant est dû, il envoie la relance au client s'il a une adresse et la garde dans
l'historique avec le déclencheur « automatique ». Une seconde passe le même jour n'envoie rien de plus (écart minimal).

```json
"Lettra": {
  "Worker": {
    "PaymentReminders": { "Enabled": true, "Interval": "12:00:00", "InitialDelay": "00:02:00" }
  }
}
```

Le branchement se fait avec `services.AddPaymentReminders(configuration, activeDossiers)` dans l'hôte qui porte les
jobs (le Worker, ou l'API quand elle les héberge).

## Le port e-mail

L'application ne connaît que le port `IEmailSender` (`Lettra.Application.Abstractions.Mail`) :

```csharp
public interface IEmailSender
{
    bool IsConfigured { get; }
    Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
```

`EmailMessage` porte le destinataire (`Email`), l'objet, le texte, le nom d'expéditeur, une adresse de réponse et des
pièces jointes facultatives. Sans configuration, le port vaut `NoEmailSender` : `IsConfigured` est faux et tout envoi
répond `Email.NotConfigured`. Un refus du serveur répond `Email.SendFailed`.

L'adaptateur `SmtpEmailSender` (`Lettra.Infrastructure.External`) envoie par SMTP avec la pile .NET, sans paquet tiers.
Il ouvre une connexion par message, écrit en UTF-8 et ne journalise jamais le destinataire ni le contenu (seulement
l'hôte et le port en cas d'échec). Il se règle dans la section `Lettra:Email` :

| Clé | Défaut | Rôle |
| --- | --- | --- |
| `Enabled` | `false` | Active l'envoi. Sans lui, rien ne part. |
| `Host`, `Port` | `""`, `587` | Serveur SMTP. Obligatoires quand `Enabled` vaut `true` : sinon l'hôte refuse de démarrer. |
| `EnableSsl` | `true` | STARTTLS. `false` pour MailPit. |
| `UserName`, `Password` | vides | Authentification, si le serveur la demande (à mettre dans le coffre, jamais dans le dépôt). |
| `FromAddress`, `FromName` | `""`, `Lettra` | Expéditeur. `FromAddress` est obligatoire. Le nom affiché est celui du cabinet ou de l'entreprise quand il est connu. |
| `Timeout` | `00:00:15` | Délai d'un envoi. |
| `PublicUrl` | vide | Adresse publique de Lettra, citée dans l'e-mail d'invitation. |

En démonstration (`deploy/demo`) : `Enabled=true`, `Host=mailpit`, `Port=1025`, `EnableSsl=false`.

## Invitations par e-mail

Quand un administrateur ajoute une personne dans **Équipe et accès**, Lettra lui envoie une invitation : qui l'invite,
dans quel cabinet, et comment se connecter (avec cette adresse, sur l'adresse publique de Lettra). Le message est en
français puis en anglais, car la langue de la personne n'est pas encore connue.

- Le message de l'écran dit si l'invitation est partie. Si l'e-mail a échoué, un second message le signale.
  **Un échec n'annule jamais l'invitation** : la personne est bien ajoutée.
- La fiche d'une personne invitée a un bouton **Renvoyer l'invitation** (message perdu, adresse corrigée). Le message
  dit si l'e-mail est parti, si l'installation n'envoie pas d'e-mails (prévenir la personne soi-même) ou s'il a échoué.
- Renvoyer l'invitation d'une personne déjà connectée est refusé (`Administration.NotInvited`).

## API

| Méthode | Chemin | Rôle |
| --- | --- | --- |
| `GET` | `/api/v1/reminders` | Factures échues, scénario et nombre de relances dues aujourd'hui |
| `GET` | `/api/v1/reminders/history` | Historique paginé. Paramètres : `invoiceId`, `customerId`, `level`, `outcome`, `channel`, `from`, `to`, `search`, `sort` (`Date`, `Invoice`, `Customer`, `Level`), `descending`, `page`, `pageSize` (200 au plus) |
| `GET` | `/api/v1/reminders/invoices/{id}/preview` | Texte de la prochaine relance d'une facture |
| `POST` | `/api/v1/reminders/invoices/{id}` | Envoie (`{"channel":"Email"}`) ou note (`{"channel":"Manual"}`) la prochaine relance |
| `POST` | `/api/v1/reminders/run` | Envoie toutes les relances dues aujourd'hui |
| `GET`, `PUT` | `/api/v1/reminders/settings` | Lire ou régler le scénario |
| `POST` | `/api/v1/cabinets/{cabinetId}/personnes/{userId}/invitation` | Renvoyer l'invitation d'une personne |

Codes d'erreur : `Reminders.InvoiceNotOverdue`, `Reminders.CustomerEmailMissing`, `Reminders.NoLevelLeft`,
`Reminders.CompanyProfileMissing`, `Reminders.DelaysInvalid`, `Reminders.GapInvalid`, `Email.NotConfigured`,
`Email.SendFailed`, `Administration.NotInvited`.

## Pas encore fait

- Relance en PDF ou par courrier, pièce jointe de la facture, modèles de texte modifiables par le cabinet.
- Facture des pénalités de retard (Lettra les cite seulement).
- Balance âgée et DSO.
- Retour des e-mails non distribués (rebonds) et désinscription d'un client des relances automatiques.
- Les e-mails des factures récurrentes et du kit contrôle fiscal (autres parties de la vague 7) utiliseront le même port.

## Tests

- Domaine : scénario, planificateur, trace d'une relance, pénalités (`tests/Lettra.Domain.Tests/Reminders`,
  `Calculations/LatePenaltyTests.cs`).
- Service et cas d'usage sur des ports en mémoire, modèles FR/EN (`tests/Lettra.Exports.Tests/Reminders`).
- Adaptateur SMTP contre un serveur SMTP en mémoire, validation de la configuration
  (`tests/Lettra.External.Tests/SmtpEmailSenderTests.cs`).
- Scénario complet sur SQLite, PostgreSQL et SQL Server, Worker compris : quatre factures dont une soldée, aperçu,
  envoi, échec gardé, historique filtré, réglage, puis passe automatique huit jours plus tard
  (`tests/Lettra.Persistence.Tests/ReminderScenarios.cs`).
- API réelle : relance, historique, scénario, droits, invitations envoyées et renvoyées
  (`tests/Lettra.Api.Tests/ReminderApiTests.cs`).
- Écrans : `tests/Lettra.UI.Tests/Reminders/RemindersPageTests.cs` et `Administration/AdministrationPageTests.cs`.
