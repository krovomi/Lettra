# Changement d'IBAN d'un fournisseur

La fraude au faux fournisseur commence presque toujours par un e-mail qui annonce de « nouvelles coordonnées bancaires ». La parade est connue : vérifier le nouvel IBAN par un canal que l'on connaissait avant la demande (le numéro de téléphone habituel du fournisseur, jamais celui donné dans l'e-mail).

**Ce qui se passe** : quand l'IBAN d'une fiche fournisseur change (Tiers > Fournisseurs > Modifier), Lettra ouvre une vérification.

- Un bandeau apparaît sur la fiche du fournisseur, orange, ou rouge si le compte change de pays.
- Le changement entre dans l'écran **Tiers > IBAN à vérifier**.
- Une alerte de sécurité (gravité S2, « IBAN d'un fournisseur modifié, à vérifier avant tout paiement ») est levée pour le dossier dans Alertes. Elle reste ouverte tant qu'un changement attend.
- La Vue cabinet affiche une colonne « IBAN à vérifier » pour les dossiers concernés, en critique si un compte change de pays.

**Décider** : sur l'écran « IBAN à vérifier », Confirmer demande par quel canal le nouvel IBAN a été vérifié (appel au numéro habituel, courrier signé ou RIB reçu par un canal connu, en personne, autre avec une note). Refuser demande une note. Un second changement avant décision remplace le premier, qui passe à « Remplacé ».

**Séparation des tâches** : quand elle est active pour le cabinet (Pilotage > Équipe et accès), la personne qui a saisi le nouvel IBAN ne peut pas le confirmer ni le refuser elle-même.

**Données gardées** : seuls des IBAN masqués (« FR76 •••• 0189 ») sont conservés dans l'historique, avec l'auteur, la date, la décision, le canal et la note. L'IBAN complet reste uniquement sur la fiche du fournisseur.

**Limite** : Lettra ne fait pas encore de paiements fournisseurs. Le « blocage » est donc un signalement (bandeau, alerte, indicateur), pas un verrou sur un virement.

**Droits** : consulter exige « Tiers : consulter », décider exige « Tiers : modifier ».

**API** : `GET /api/v1/suppliers/iban-changes?status=&search=&page=&pageSize=&sort=&direction=`, `GET /api/v1/suppliers/{id}/iban-changes`, `POST /api/v1/suppliers/iban-changes/{changeId}/confirmation`, `POST /api/v1/suppliers/iban-changes/{changeId}/rejection` (en-tête de version comme les autres écritures). Refus possibles : `IbanChange.NotFound`, `IbanChange.AlreadyDecided`, `IbanChange.MethodRequired`, `IbanChange.NoteRequired`, `IbanChange.NoteTooLong`, `IbanChange.SameUser`.

## Nom et IBAN vérifiés

Depuis le 9 octobre 2025, chaque virement SEPA passe par la vérification du bénéficiaire : la banque du payeur demande à la banque du titulaire si le nom donné est le bon. La réponse est « correspond », « correspond presque » (la banque donne alors le nom exact), « ne correspond pas » ou « impossible à vérifier ».

**Titulaire du compte** : sur la fiche fournisseur (Modifier, bloc bancaire), le champ facultatif « Titulaire du compte » reçoit le nom exact du compte quand il diffère du nom du fournisseur (nom commercial, gérant). Il fait 70 caractères au plus, la limite d'un virement SEPA. Sans titulaire, c'est le nom du fournisseur qui est présenté à la banque.

**Contrôle local** : Lettra compare le titulaire au nom du fournisseur sans tenir compte des accents, de la casse, de la ponctuation, des formes juridiques (SAS, SARL, GmbH…), des petits mots ni de l'ordre des mots. Une faute de frappe ou un nom abrégé donne « proche du nom du fournisseur » ; un nom sans rapport donne « ne ressemble pas au nom du fournisseur ».

**Vérification du bénéficiaire** : le bouton « Vérifier le nom et l'IBAN », sur la fiche ou dans l'onglet « Nom et IBAN », interroge la banque. La réponse reste sur la fiche avec sa date. Elle s'efface dès que l'IBAN ou le nom présenté change. Il faut le droit « Tiers : modifier ».

**Banque simulée** : aucun prestataire n'est encore sous contrat, donc une banque simulée répond. Rien ne quitte le poste. Elle connaît trois comptes de démonstration :

| IBAN | Réponse |
|---|---|
| DE89 3704 0044 0532 0130 00 | titulaire « Schmidt Handels GmbH » : ne correspond pas pour un fournisseur français |
| FR76 3000 6000 0112 3456 7890 189 | titulaire « Minoterie Dupuy » : « Minoterie Dupuis » correspond presque |
| FR14 2004 1010 0505 0001 3M02 606 | banque qui ne répond pas : impossible à vérifier |

Pour tout autre IBAN, elle répond « correspond ». La vraie vérification passera par un établissement de paiement, branché derrière le même port (`IPayeeVerification`).

**Onglet « Nom et IBAN »** : il liste les fournisseurs payables par virement (avec un IBAN, non supprimés). Par défaut, il ne montre que ceux à contrôler, du plus risqué au plus sûr :

1. ne correspond pas (à la banque ou au contrôle local) ;
2. IBAN modifié à vérifier ;
3. correspond presque ;
4. impossible à vérifier ;
5. jamais vérifié.

L'écran propose une recherche sans accents ni casse (nom ou titulaire), un tri par nom ou par risque, et la case « Seulement ceux à contrôler ».

**API** :
- `GET /api/v1/suppliers/payees?onlyToReview=&search=&page=&pageSize=&sort=Name|Risk&direction=` exige « Tiers : consulter ».
- `POST /api/v1/suppliers/{id}/payee-verification` exige « Tiers : modifier ». Refus possibles : `Supplier.IbanMissing`, `Supplier.NotFound`, `Directory.Unavailable`.
- `SupplierResponse` porte `AccountHolderName`, `PayeeCheck` et `HolderNameMatch`.

## Titulaire du compte de l'entreprise

L'écran « Fiche entreprise » (bloc des conditions de paiement, sous l'IBAN et le BIC) a un champ facultatif « Titulaire du compte ». Il reçoit le nom exact du compte bancaire quand il diffère de la raison sociale, en 70 caractères au plus. Lettra signale un titulaire qui ne ressemble pas à la raison sociale. Le titulaire figure :
- sous l'IBAN du PDF (« Titulaire … ») ;
- dans le QR code de paiement, à la place du nom de l'émetteur ;
- dans la facture électronique (nom du compte de paiement, BT-85), jamais sur un avoir.

La banque des clients répond alors « correspond » à la vérification du bénéficiaire avant leurs virements.
