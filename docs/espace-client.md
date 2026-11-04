# Espace client : paiement en ligne et devis signé

**But.** Se faire payer et faire signer plus vite, sans que le client crée de compte.

## Parcours

1. Le cabinet crée un lien : depuis la fiche client (espace complet), une facture due (« Paiement en ligne ») ou un
   devis validé (« Signature en ligne », avec un acompte facultatif de 20, 30, 40 ou 50 %). Il le copie et l'envoie
   lui-même à son client : Lettra n'envoie pas d'e-mail pour ce lien. Seuls le rappel avant échéance et la relance
   d'échéance manquée en portent un, créé à l'envoi ([avant-le-retard.md](avant-le-retard.md)).
2. Le client ouvre l'espace : ses factures dues avec le QR code de virement, ses devis, ses factures réglées.
3. Il paie par carte (page du prestataire) ou par virement (QR code).
4. Il signe un devis : le devis passe « Accepté » et, si le lien le demande, la facture d'acompte est créée, validée et
   aussitôt payable en ligne.
5. Une facture qu'un client professionnel peut étaler montre **Payer en plusieurs fois** : il choisit 2 ou 3
   échéances dans les bornes de l'entreprise, donne son nom, s'engage, et l'échéancier est accordé tout de suite
   ([avant-le-retard.md](avant-le-retard.md)).

## Lien

- Le jeton contient l'identifiant du dossier et un secret aléatoire de 32 octets. Lettra ne garde que l'empreinte
  SHA-256 du secret : l'adresse complète n'est visible qu'à sa création.
- Durée de 30 jours par défaut, 90 au plus ; révocable. Le cabinet voit la dernière ouverture.
- Le jeton figure dans l'adresse : c'est le lien à envoyer.

## Identité

Les actions du client (lire ses pièces, enregistrer un encaissement, accepter un devis, créer et valider l'acompte)
s'exécutent sous le compte de service de l'espace client (`Lettra:CustomerPortal:ServiceAccountId`). Ce compte est
déclaré au registre des droits avec un rôle limité, et chaque pièce créée porte son identité. Sans compte de service,
l'espace client est fermé et aucun lien ne peut être créé.

Dans la démo Docker, le rôle `espace-client` porte PortailConsulter, DevisAccepter, DossierLire, TiersConsulter,
DevisCreer, FactureSaisir, FactureValider et FactureEmettre, et l'utilisateur fictif « Espace client (automatique) »
le reçoit sur le cabinet (`deploy/demo/compose.yaml`). Une cellule qui veut ouvrir l'espace client déclare la même
clé, le même rôle et le même utilisateur ; les charts Helm ne le font pas.

## Sécurité des pages

- Pages HTML rendues par le serveur, sans script ; la politique de contenu reste stricte.
- `Cache-Control: no-store`, `noindex`, referrer `same-origin` (le jeton ne part vers aucun autre site).
- Débit limité par adresse IP (`Lettra:CustomerPortal:PermitsPerMinute`, 60 par défaut, de 1 à 10 000).
- Un formulaire posté depuis une autre origine est refusé.
- Un jeton faux, un dossier inconnu et un lien révoqué donnent la même page « introuvable ».
- Le service worker de l'application ne met jamais en cache `/espace-client/` ni `/paiement-simule/`.

Ces pages n'ont ni session ni double facteur : le lien tient lieu de contrôle d'entrée, puis le médiateur applique
ses contrôles habituels (garde du dossier, autorisation, validation) sous le compte de service.

## Paiement

- Port `IOnlinePaymentProvider`. Adaptateur simulé par défaut (`Lettra:CustomerPortal:Payments=Simulated`) ou aucun
  (`None` : virement seulement). Les charts Helm posent `None`.
- Dans la démo, la carte 4000 0000 0000 0002 est refusée, toute autre est acceptée, et aucune carte n'est débitée.
- Lettra ne voit jamais de numéro de carte réel : la page de paiement est celle du prestataire.
- Un paiement accepté est réservé une seule fois avant d'être enregistré : deux retours simultanés n'encaissent qu'une
  fois. Il devient un encaissement par carte, imputé sur la facture.

## Signature

- Port `IElectronicSignatureProvider`. L'adaptateur simulé produit une signature électronique simple au sens eIDAS :
  nom saisi, accord, horodatage, empreinte du devis tirée du journal inaltérable.
- La preuve (signataire, date, prestataire et référence, empreinte) s'affiche sur le devis côté cabinet.
- Une signature qualifiée passerait par un prestataire réel.

## Langues

La langue de la fiche client d'abord, puis celle du navigateur, puis le français. Montants et dates suivent la langue ;
les heures sont celles de Paris.

## Limites connues

- Prestataires de paiement et de signature simulés.
- Paiements commencés gardés en mémoire (perdus au redémarrage), sans webhook.
- Pas de téléchargement du PDF depuis l'espace client.
- Fermé sur un poste isolé : l'espace client demande le serveur du cabinet.
