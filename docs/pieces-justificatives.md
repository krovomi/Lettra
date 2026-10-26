# Pièces justificatives des dépenses

À la clôture comme à chaque déclaration de TVA, le cabinet doit retrouver la facture ou le ticket de chaque dépense du relevé bancaire. Lettra suit ces pièces ligne par ligne et les fait déposer par le client, en photo depuis son téléphone.

**Côté cabinet** : menu Comptabilité, « Pièces justificatives ». L'écran demande « Dossier : consulter » ; réclamer une pièce ou dire qu'elle n'est pas attendue demande « Factures : émettre » ; déposer une pièce demande « Pièces : déposer ».
- Les compteurs donnent les pièces à obtenir, leur montant, les pièces réclamées et les pièces déposées.
- La liste s'ouvre sur les dépenses à obtenir (manquantes ou réclamées), les plus récentes en tête, avec recherche, filtre par statut, période et tri par opération, date, montant ou statut. Sur téléphone, chaque dépense devient une carte.
- **Réclamer** : on coche les dépenses (ou « Sélectionner les dépenses à obtenir »), on saisit l'adresse du client (la dernière adresse utilisée est proposée) et un mot facultatif. Un seul e-mail part, en français puis en anglais, avec la liste des dépenses (date, montant, tiers) et le lien vers « Pièces à fournir ». Si l'installation n'envoie pas d'e-mails, la demande est notée quand même et l'écran prévient. Une dépense réclamée plusieurs fois garde le nombre de demandes et la date de la dernière.
- **Déposer** une pièce reçue autrement, la **remplacer**, la **télécharger**.
- **Non attendue** : le relevé suffit (frais bancaires, prélèvement fiscal). Le choix se défait.

**Côté client** : « Pièces à fournir » (`/my-receipts`), lien de l'e-mail. Les pièces réclamées, les plus anciennes en tête, une carte chacune avec un grand bouton « Déposer » qui ouvre l'appareil photo sur téléphone. Une pièce déposée disparaît de la liste.

**Fichiers acceptés** : photo (JPEG, PNG, WebP, HEIC) ou PDF, 5 Mo au plus. Le type se lit dans le contenu du fichier, jamais dans son nom : un SVG, un exécutable ou un fichier renommé est refusé. La pièce est toujours servie en téléchargement (pièce jointe), jamais affichée dans le navigateur. Seules les dépenses (débits) attendent une pièce ; une pièce déposée ne se réclame plus.

**Statuts** : manquante, réclamée, déposée, non attendue.

**Limite connue** : sur le serveur, les pièces sont rangées dans la base du dossier sans chiffrement applicatif, comme le reste des données du dossier côté serveur (voir les écarts connus de l'annexe F).

**API** (toutes sous `/api/v1`) :

- `GET /bank/receipts` (Dossier : consulter) : dépenses et justificatifs, compteurs, dernière adresse.
- `GET /bank/receipts/requested` (Pièces : déposer) : les pièces réclamées, pour le client.
- `POST /bank/receipts/requests` (Factures : émettre) : réclamer jusqu'à 100 pièces d'un seul e-mail.
- `POST /bank/lines/{id}/receipt-not-needed` (Factures : émettre).
- `PUT /bank/lines/{id}/receipt` (Pièces : déposer) : dépôt en base 64, 5 Mo au plus.
- `GET /bank/lines/{id}/receipt` (Dossier : consulter) : téléchargement.

## Droits

« Pièces : déposer » (`PiecesDeposer`) appartient au client du portail et aux rôles collaborateur, collaborateur étendu et manager, qui peuvent déposer une pièce reçue par un autre canal (courrier, e-mail). Le client du portail voit « Pièces à fournir » dans son menu.
