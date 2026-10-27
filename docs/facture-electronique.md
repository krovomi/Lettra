# Réforme de la facturation électronique

La réforme rend obligatoires, entre entreprises établies en France assujetties à la TVA, la facture électronique
(e-invoicing) et la transmission à l'administration des autres opérations (e-reporting), par une **plateforme agréée**
(PA, anciennement « PDP »). Lettra couvre les deux côtés : il **émet** les factures et en suit les statuts, et il
**reçoit** les factures des fournisseurs et y répond. Dans le prototype, la plateforme et l'annuaire sont **simulés**
(aucune plateforme réelle n'est choisie) ; ils sont branchés derrière des ports et se remplacent par un vrai partenaire
sans toucher aux écrans.

## Calendrier (décret 2024-266)

| Obligation | Date | Pour qui |
|---|---|---|
| Recevoir des factures électroniques | 1er septembre 2026 | Toutes les entreprises |
| Émettre, e-reporting, nouvelles mentions | 1er septembre 2026 | Grandes entreprises et ETI |
| Émettre, e-reporting, nouvelles mentions | 1er septembre 2027 | PME et microentreprises |

La **catégorie d'entreprise** se choisit dans **Entreprise** (fiche de l'entreprise) : « Grande entreprise »,
« Entreprise de taille intermédiaire (ETI) », « PME » ou « Micro-entreprise ». Sans catégorie, Lettra applique la première échéance. Sous le champ, Lettra affiche les
deux dates qui s'appliquent.

La catégorie règle le contrôle de conformité : avant la date d'émission de l'entreprise, une mention de la réforme
absente (SIREN du client, catégorie d'opération, option pour les débits) donne un **avertissement** ; à partir de cette
date, elle **bloque** la validation. Une PME peut donc valider jusqu'au 31 août 2027 une facture sans SIREN client,
avec un avertissement.

## Les données de la réforme dans la facture électronique

Lettra écrit dans le CII (le XML du Factur-X) et dans l'UBL :

| Donnée | Code EN 16931 | Valeur | CII | UBL |
|---|---|---|---|---|
| Cadre de facturation | BT-23 | B1 (biens), S1 (services), M1 (mixte) ; B4, S4, M4 pour la facture définitive d'un devis qui déduit ses acomptes | `BusinessProcessSpecifiedDocumentContextParameter` | `cbc:ProfileID` |
| Option pour la TVA d'après les débits | BT-8 | 5 en CII, 3 en UBL | `DueDateTypeCode` de la TVA d'en-tête | `cac:InvoicePeriod/cbc:DescriptionCode` |

Le cadre de facturation se déduit de la **catégorie d'opération** de la facture (biens, services, mixte). Le code
d'exigibilité ne s'écrit que si l'entreprise a opté pour les débits (fiche entreprise), et seulement sur une TVA due.
Les quatre nouvelles mentions (SIREN du client, adresse de livraison, catégorie d'opération, option pour les débits)
étaient déjà contrôlées et imprimées.

## Annuaire

L'annuaire central de la facturation électronique dit, pour chaque SIREN, à quelle plateforme et à quelle adresse
électronique adresser ses factures. Lettra le consulte à deux endroits :

- **Factures reçues** : l'inscription de l'entreprise (adresse électronique, plateforme, date d'inscription). Sans
  inscription, un bandeau prévient que les fournisseurs ne peuvent pas lui adresser de facture électronique.
- **Sous une facture validée**, avant la transmission : pour un client professionnel français, une ligne indique
  l'adresse à laquelle la facture partira, ou avertit que le client ne figure pas à l'annuaire (la plateforme
  rejettera la facture). L'avertissement n'empêche pas l'envoi.

Seul le SIREN part vers l'annuaire. Sans plateforme configurée, l'annuaire n'est pas consulté et l'écran n'en parle pas.
Un SIREN absent de l'annuaire n'est pas une erreur : la réponse le dit.

## Factures reçues

Menu **Tiers › Factures reçues** (`/received-invoices`, Ctrl+K « factures reçues », « litige », « annuaire »).

1. **Relever les factures** : Lettra demande à la plateforme les factures mises à disposition de l'entreprise (son
   SIREN, fiche entreprise). Seules les nouvelles s'enregistrent : relever deux fois n'ajoute rien. Un fichier
   illisible, ou adressé à un autre SIREN, reste à la plateforme ; le message le compte à part.
2. La liste montre le fournisseur, le numéro, la date, l'échéance, le TTC (un avoir en négatif) et le statut. Le
   fournisseur est rapproché de la fiche **Fournisseurs** qui porte le même SIREN ; sinon la ligne dit « Pas encore
   dans vos fournisseurs ». Recherche, filtre par statut (« À traiter » = mises à disposition ou prises en charge),
   période, et tri par colonne.
3. **Ouvrir** une facture : en-tête, cadre de facturation, lignes, TVA, notes, historique des statuts, et
   **Télécharger le fichier** reçu, tel quel (c'est l'original de la facture).
4. **Répondre** au fournisseur, par sa plateforme, avec les statuts du cycle de vie (norme XP Z12-012) :

| Réponse | Code | Remarque |
|---|---|---|
| Prise en charge | 204 | La facture est en cours de traitement |
| Approuvée | 205 | |
| En litige | 207 | Motif obligatoire (500 caractères au plus) |
| Refusée | 210 | Motif obligatoire ; définitif : le fournisseur corrige par une nouvelle facture |
| Paiement transmis | 211 | Montant payé (le TTC est proposé) ; pas sur un avoir |

Chaque réponse part à la plateforme sous une clé d'idempotence : rejouée après une coupure, elle n'en crée pas une
seconde. Les statuts ne s'effacent ni ne se réécrivent.

Les factures reçues sont enregistrées dans la base du dossier (tables `ReceivedInvoices` et
`ReceivedInvoiceStatusEvents`), fichier d'origine compris.

### Droits

| Action | Droit |
|---|---|
| Voir les factures reçues, les ouvrir, télécharger le fichier | Lire le dossier (`DossierLire`) |
| Relever les factures, y répondre | Traiter les factures reçues (`FacturesRecuesTraiter`) : manager, collaborateur étendu, collaborateur |
| Consulter l'annuaire | Consulter les tiers (`TiersConsulter`) |

### Routes

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/api/v1/e-invoicing/received` | Liste, inscription à l'annuaire, dates de la réforme |
| POST | `/api/v1/e-invoicing/received/fetch` | Relever les factures mises à disposition |
| GET | `/api/v1/e-invoicing/received/{id}` | Détail d'une facture reçue |
| GET | `/api/v1/e-invoicing/received/{id}/file` | Fichier reçu (XML) |
| POST | `/api/v1/e-invoicing/received/{id}/responses` | Répondre (statut, motif, montant) |
| GET | `/api/v1/e-invoicing/directory/{siren}` | Chercher un SIREN à l'annuaire |

## Plateforme simulée

Réglage `Lettra:EInvoicing:Platform` : `None` (par défaut, rien ne part et rien ne se reçoit) ou `Simulated` (la démo
Docker, le développement). La plateforme simulée met à disposition de chaque entreprise quatre factures fictives de
trois fournisseurs fictifs : Papeterie Moreau SARL (fournitures, CII, B1, et un avoir en UBL), Transports Lefèvre
(transport, UBL, S1) et Logiciels Arcade SAS (abonnement, CII, S1). Leurs références se déduisent du SIREN de
l'entreprise : après un redémarrage, la plateforme rend les mêmes factures et le dossier ne les reprend pas deux fois.
Son annuaire inscrit tout SIREN valide. Elle garde les réponses en mémoire.

## Limites

- Plateforme et annuaire simulés : aucune plateforme agréée réelle n'est choisie.
- Une facture reçue n'est ni comptabilisée (journal des achats), ni rapprochée d'un bon de commande ou d'un paiement :
  ces traitements viennent avec les achats.
- Les factures reçues ne sont pas chaînées dans le journal d'intégrité des pièces émises.
- L'e-reporting des achats auprès de fournisseurs étrangers n'est pas fait.
- Le cadre de facturation ne couvre pas les factures déjà payées (B2, S2, M2), les sous-traitants (S5, S6) ni les
  autres cas particuliers de la norme.
- La catégorie d'entreprise se choisit à la main : Lettra ne la calcule pas (effectif, chiffre d'affaires, bilan).
