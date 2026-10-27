# Attestations de vigilance des sous-traitants

Une entreprise qui confie un travail ou une prestation d'au moins 5 000 € HT doit obtenir de son cocontractant une attestation de vigilance de l'URSSAF à la conclusion du contrat, puis tous les six mois jusqu'à la fin de son exécution (Code du travail, art. L8222-1, R8222-1 et D8222-5). Sans elle, si ce sous-traitant est condamné pour travail dissimulé, l'entreprise est tenue solidairement de ses impôts, cotisations, salaires et pénalités (art. L8222-2). Elle doit aussi vérifier l'authenticité de l'attestation sur le site de l'URSSAF grâce à son code de sécurité. Cet écran tient ce suivi pour chaque fournisseur du dossier.

**Où** : menu Tiers, « Attestations de vigilance » (`/attestations`), dans le dossier du client.

**Pour chaque fournisseur** :

| Champ | Contenu |
|---|---|
| Montant du contrat HT | Le contrat en cours, ou les prestations prévues sur l'année ; à partir de 5 000 € HT, l'attestation est exigée |
| Date de l'attestation | La date imprimée sur l'attestation reçue |
| Code de sécurité | De 8 à 20 lettres et chiffres ; Lettra retire les espaces et le met en majuscules |
| Code vérifié le | Le jour où le cabinet a vérifié le code sur le site de l'URSSAF |

**Les règles** :

- Une attestation vaut six mois à partir de sa date (elle expire la veille du même jour, six mois plus tard).
- Aucune date dans le futur ; la vérification ne précède pas la date de l'attestation et demande la date et le code.
- Le montant est positif, au centime près.
- Les modifications concurrentes sont refusées (version lue, en-tête `If-Match`).

**Où en est chaque fournisseur** :

| État | Quand |
|---|---|
| Non exigée | Contrat de moins de 5 000 € HT, ou fournisseur non suivi |
| Manquante | Contrat d'au moins 5 000 € HT et aucune attestation |
| Expirée | L'attestation a plus de six mois |
| Code à vérifier | Reçue, mais son code n'a pas été vérifié |
| Expire bientôt | Valable, et elle expire dans les trente jours |
| Valable | Le reste du temps |

L'écran montre d'abord les fournisseurs à ne pas payer en l'état (expirée, manquante, code à vérifier), puis ceux dont l'attestation expire bientôt. Il se trie par fournisseur, montant, date de fin de validité ou état, et se filtre par nom et par état (dont « À obtenir avant de payer »).

**Avec le contrôle avant paiement** : dans « Nom et IBAN des bénéficiaires », un fournisseur dont l'attestation est manquante, expirée ou non vérifiée passe « à contrôler », juste après un IBAN modifié, avec un lien « Attestation de vigilance à obtenir » vers cet écran.

**Dans la vue cabinet** : une colonne « Attestations URSSAF » compte les fournisseurs à ne pas payer en l'état (critique si une attestation a expiré, à traiter sinon), ou à défaut ceux dont l'attestation expire dans les trente jours (pour information), avec l'échéance la plus proche. Elle ne montre ni nom, ni montant.

**Droits** : aucune permission nouvelle. Lire l'écran : la permission de consulter les tiers. Enregistrer : celle de modifier les tiers. Le client (espace client) n'y a pas accès.

**Ce qui n'est pas fait** : le dépôt de l'attestation elle-même (Lettra note sa date et son code) ; la vérification automatique du code auprès de l'URSSAF ; l'attestation de régularité fiscale et la liste des salariés étrangers, demandées dans certains cas ; le blocage effectif d'un virement (Lettra ne paie pas les fournisseurs, il signale).

## API

| Méthode | Adresse | Droit | Rôle |
|---|---|---|---|
| GET | `/api/v1/attestations` | Consulter les tiers (en-tête `X-Dossier`) | suivi de chaque fournisseur et son état |
| PUT | `/api/v1/attestations/{fournisseur}` | Modifier les tiers (en-tête `X-Dossier`) | enregistrer le montant, l'attestation et sa vérification ; la version lue est exigée (en-tête `If-Match`) |

La base de chaque dossier gagne une table (le suivi de l'attestation, une ligne par fournisseur suivi), pour SQLite,
SQL Server et PostgreSQL. La migration ne touche aucune donnée existante. Le suivi porte sur des entreprises, pas sur
des personnes ; un fournisseur entrepreneur individuel peut être identifiable par son nom, et son suivi suit alors la
conservation de la fiche fournisseur.

## Ce qui a été vérifié

| Où | Vérifié |
|---|---|
| Navigateur, ordinateur et téléphone, démo Docker | Oui |
| Application MAUI (Windows, macOS, Android, iOS) | Non vérifié |
