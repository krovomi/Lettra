# Pièces justificatives des dépenses

À la clôture comme à chaque déclaration de TVA, le cabinet doit retrouver la facture ou le ticket de chaque dépense du relevé bancaire. Lettra suit ces pièces ligne par ligne et les fait déposer par le client, en photo depuis son téléphone.

**Côté cabinet** : menu Comptabilité, « Pièces justificatives ». L'écran demande « Dossier : consulter » ; réclamer une pièce ou dire qu'elle n'est pas attendue demande « Factures : émettre » ; déposer une pièce demande « Pièces : déposer ».
- Les compteurs donnent les pièces à obtenir, leur montant, les pièces réclamées et les pièces déposées.
- La liste s'ouvre sur les dépenses à obtenir (manquantes ou réclamées), les plus récentes en tête, avec recherche, filtre par statut, période et tri par opération, date, montant ou statut. Sur téléphone, chaque dépense devient une carte.
- **Réclamer** : on coche les dépenses (ou « Sélectionner les dépenses à obtenir »), on saisit l'adresse du client (la dernière adresse utilisée est proposée) et un mot facultatif. Un seul e-mail part, en français puis en anglais, avec la liste des dépenses (date, montant, tiers) et le lien vers « Pièces à fournir ». Si l'installation n'envoie pas d'e-mails, la demande est notée quand même et l'écran prévient. Une dépense réclamée plusieurs fois garde le nombre de demandes et la date de la dernière.
- **Déposer** une pièce reçue autrement, la **remplacer**, la **télécharger**.
- **Non attendue** : le relevé suffit (frais bancaires, prélèvement fiscal). Le choix se défait.

**Côté client** : « Pièces à fournir » (`/my-receipts`), lien de l'e-mail. Les pièces réclamées, les plus anciennes en tête, une carte chacune avec un grand bouton « Déposer » qui ouvre l'appareil photo sur téléphone. Une pièce déposée disparaît de la liste.

**Lire la pièce sur l'appareil** : après le choix d'une photo, Lettra la lit sur l'appareil, dans le navigateur ou l'application (reconnaissance de texte Tesseract compilée en WebAssembly, modèle français livré avec Lettra), puis propose le montant TTC, la date et la TVA : le total est cherché par ses mots (« total TTC », « net à payer »…), la TVA sur une ligne « total TVA » ou « dont TVA », sinon dans le tableau par taux ; avec un seul taux lu et pas de montant, la TVA est calculée et l'écran le dit. L'utilisateur confirme ou corrige les trois valeurs avant l'envoi ; un montant différent de celui du relevé est signalé. Seuls le fichier et ces trois valeurs partent : ni l'image lue, ni le texte reconnu ne quittent l'appareil, et rien n'est écrit dans les journaux. Un PDF, une photo HEIC hors Safari ou un appareil sans WebAssembly SIMD ne se lisent pas : l'utilisateur saisit les trois valeurs. Sur le serveur, montant, date et TVA sont vérifiés (montant positif au centime, TVA entre zéro et le montant, date ni future ni avant 2000) et gardés avec la pièce, avec l'indication « lu sur l'appareil » ou « saisi ». La liste du cabinet les affiche sous le nom du fichier, et signale un écart avec le relevé. Dans le navigateur installé (PWA), le modèle français est mis en cache avec le reste de l'application, pour lire une photo hors ligne (non vérifié hors ligne).

**Fichiers acceptés** : photo (JPEG, PNG, WebP, HEIC) ou PDF, 5 Mo au plus. Le type se lit dans le contenu du fichier, jamais dans son nom : un SVG, un exécutable ou un fichier renommé est refusé. La pièce est toujours servie en téléchargement (pièce jointe), jamais affichée dans le navigateur. Seules les dépenses (débits) attendent une pièce ; une pièce déposée ne se réclame plus.

**Statuts** : manquante, réclamée, déposée, non attendue.

**Limite connue** : sur le serveur, les pièces sont rangées dans la base du dossier sans chiffrement applicatif, comme le reste des données du dossier côté serveur (voir les écarts connus de l'annexe F).

**API** (toutes sous `/api/v1`) :

- `GET /bank/receipts` (Dossier : consulter) : dépenses et justificatifs, compteurs, dernière adresse.
- `GET /bank/receipts/requested` (Pièces : déposer) : les pièces réclamées, pour le client.
- `POST /bank/receipts/requests` (Factures : émettre) : réclamer jusqu'à 100 pièces d'un seul e-mail.
- `POST /bank/lines/{id}/receipt-not-needed` (Factures : émettre).
- `PUT /bank/lines/{id}/receipt` (Pièces : déposer) : dépôt en base 64, 5 Mo au plus, avec le montant TTC, la date et la TVA confirmés (facultatifs pour un appel direct à l'API).
- `GET /bank/lines/{id}/receipt` (Dossier : consulter) : téléchargement.

## Droits

« Pièces : déposer » (`PiecesDeposer`) appartient au client du portail et aux rôles collaborateur, collaborateur étendu et manager, qui peuvent déposer une pièce reçue par un autre canal (courrier, e-mail). Le client du portail voit « Pièces à fournir » dans son menu.
