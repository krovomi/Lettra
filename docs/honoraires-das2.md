# Honoraires à déclarer (DAS 2)

Une entreprise qui verse à un même bénéficiaire plus de 1 200 € TTC dans l'exercice en honoraires, vacations, commissions, courtages, ristournes, droits d'auteur ou d'inventeur doit les déclarer à l'administration fiscale (Code général des impôts, art. 240 ; formulaire 2460-2-SD, dit DAS 2, ou fichier TD-DAS2). La déclaration se dépose avec la déclaration de résultats : le deuxième jour ouvré après le 1er mai pour un exercice clos au 31 décembre, trois mois après la clôture sinon. À défaut, l'amende est de 50 % des sommes non déclarées (art. 1736). Cet écran prépare cette déclaration pour le dossier du client.

**Où** : menu Comptabilité › Déclarations, onglet « Honoraires (DAS 2) » (`/tax/fees`), dans le dossier du client. L'écran s'ouvre sur le dernier exercice clos ; les flèches passent d'un exercice à l'autre.

**Pour chaque bénéficiaire suivi** (un fournisseur du dossier) :

| Champ | Contenu |
|---|---|
| Nature des sommes | Honoraires et vacations, commissions, courtages, ristournes, droits d'auteur, droits d'inventeur, autres rémunérations |
| Profession | La profession du bénéficiaire, demandée par la déclaration (60 caractères au plus) |
| Versé hors relevés | Les sommes de l'exercice qui ne passent pas par un relevé importé (chèque, autre compte) ; zéro efface la ligne |

**Comment Lettra compte** :

- Les virements des relevés bancaires importés dont l'IBAN du destinataire est celui de la fiche fournisseur, pendant l'exercice ; Lettra compare des empreintes d'IBAN, comme le contrôle avant paiement.
- Plus les sommes notées hors relevés pour cet exercice.
- Le bénéficiaire est « à déclarer » au-delà de 1 200 € TTC, « sous le seuil » sinon.
- L'écran indique jusqu'à quelle date les relevés sont importés : les virements plus récents ne sont pas encore comptés.
- Un bénéficiaire français sans SIRET est signalé : la déclaration le demande.
- Les modifications concurrentes sont refusées (version lue, en-tête `If-Match`).

**Échéance** : dès qu'un bénéficiaire est suivi, la déclaration rejoint le calendrier fiscal du dossier (obligation « Déclaration des honoraires (DAS 2) »), à la date de la déclaration de résultats. Elle se marque déposée depuis l'écran ou depuis le calendrier, comme les autres obligations.

**Dans la vue cabinet** : une colonne « Honoraires DAS 2 » compte les bénéficiaires à déclarer du dernier exercice clos, avec l'échéance (« avant le … ») : pour information, à traiter à trente jours ou moins, critique une fois l'échéance dépassée. Rien n'apparaît une fois la déclaration marquée déposée, ni pour une échéance antérieure au début du suivi. Elle ne montre ni nom, ni montant.

L'écran se trie par bénéficiaire, nature ou total, et se filtre par nom et par état (à déclarer, sous le seuil, SIRET manquant).

**Droits** : aucune permission nouvelle. Lire l'écran : la permission de consulter le dossier. Suivre un bénéficiaire, noter une somme, marquer déposée : la permission de travailler sur la comptabilité (celle de l'export FEC). Le client (espace client) n'y a pas accès.

**Ce qui n'est pas fait** : la production du fichier TD-DAS2 et son dépôt (Lettra prépare, le cabinet déclare) ; la distinction des frais remboursés et des avantages en nature ; la retenue à la source sur les bénéficiaires étrangers ; la lecture des paiements par carte ou des factures fournisseurs (seuls les virements des relevés et les sommes notées comptent).

## Données personnelles

La DAS 2 vise souvent des personnes physiques (professions libérales, auteurs) : le suivi note une profession et des
montants versés à une personne identifiable. Ces données restent dans la base du dossier, suivent la conservation de
la fiche fournisseur (supprimées avec elle) et n'apparaissent ni dans les journaux ni dans les indicateurs (la vue
cabinet ne reçoit qu'un compteur, une urgence et une date). Base légale : l'obligation légale de l'entreprise cliente
(art. 240 du CGI). Lettra lit les IBAN par leur empreinte et ne les copie pas.

## API

| Méthode | Adresse | Droit | Rôle |
|---|---|---|---|
| GET | `/api/v1/tax/fees[?date=]` | `DossierLire` (en-tête `X-Dossier`) | bénéficiaires de l'exercice qui contient la date (dernier exercice clos par défaut), totaux et état |
| PUT | `/api/v1/tax/fees/{fournisseur}` | `FecExporter` | suivre un bénéficiaire, sa nature, sa profession, les sommes hors relevés ; version lue en `If-Match` |
| DELETE | `/api/v1/tax/fees/{fournisseur}` | `FecExporter` | ne plus suivre le bénéficiaire ; version lue en `If-Match` |

Le dépôt se marque par `PUT /api/v1/tax/calendar/filings`, comme les autres obligations du calendrier fiscal. La base de
chaque dossier gagne deux tables (le bénéficiaire suivi, lié au fournisseur ; les sommes versées hors relevés, une
ligne par exercice), pour SQLite, SQL Server et PostgreSQL. La migration ne touche aucune donnée existante.

## Ce qui a été vérifié

| Où | Vérifié |
|---|---|
| Navigateur, ordinateur et téléphone, démo Docker | Oui |
| Application MAUI (Windows, macOS, Android, iOS) | Non vérifié |
