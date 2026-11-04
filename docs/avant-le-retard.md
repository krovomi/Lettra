# Avant le retard : rappel avant échéance et échéancier en libre-service

Les relances agissent après l'échéance, quand le retard est déjà là. « Avant le retard » agit avant : un rappel aimable aux clients qui paient souvent en retard, et, pour un client professionnel qui ne peut pas tout régler d'un coup, un échéancier qu'il demande lui-même dans son espace client, dans des bornes fixées à l'avance par l'entreprise.

**Où** : écran **Relances** (`/reminders`, menu Comptabilité). Le réglage est dans le bloc **Scénario de relance** (partie « Avant le retard ») ; deux listes s'ajoutent, **Rappels avant échéance** et **Échéanciers**, et un bloc **Clients opposés au ciblage**. Sur la facture, un panneau **Échéancier** montre le calendrier et son suivi. Dans l'espace client, une facture qu'on peut étaler montre **Payer en plusieurs fois**.

## Le rappel avant échéance

- **Qui le reçoit** : les clients dont la [météo des paiements](../README.md#météo-des-paiements) est « pluie » (11 à 30 jours de retard moyen) ou « orage » (plus d'un mois), pour leurs factures validées, non soldées, qui arrivent à échéance. Pas les clients « soleil » ou « nuages », pas ceux qui n'ont pas encore de météo, pas ceux qui se sont opposés au ciblage, pas une facture sous échéancier.
- **Quand** : N jours avant l'échéance (3 par défaut, de 1 à 30). Si le client a un meilleur jour (le jour de la semaine après lequel il paie le plus vite, lu dans la météo des paiements), le rappel est avancé au dernier de ces jours-là avant la date prévue, jamais avant la date de la facture. Une seule fois par facture.
- **Comment il part** : il suit l'envoi automatique du scénario de relance. Coché, la passe quotidienne l'envoie (le Worker, ou l'API dans la démo) ; décoché (par défaut), il reste à envoyer depuis la liste **Rappels avant échéance** (**Voir la relance**, puis **Envoyer par e-mail** ou **Noter comme envoyée**).
- **Ce qu'il dit** (dans la langue du client, français ou anglais) : le numéro, la date et le reste dû de la facture, son échéance, le lien de l'espace client du client pour payer en ligne (créé à l'envoi, valable 30 jours, seulement sur un serveur où l'espace client est ouvert et dont l'adresse publique est connue), la possibilité d'y demander un échéancier (client professionnel et échéancier proposé), l'IBAN et le BIC, et le **QR code de virement** (norme EPC) en pièce jointe, qui ouvre le virement déjà rempli dans l'application bancaire. Il ne cite ni pénalité ni retard.
- **La liste** montre les rappels des deux semaines à venir : facture, client et sa météo, meilleur jour, échéance, reste dû, date prévue du rappel, ou « Parti le … ». Recherche, filtre par météo, « À envoyer aujourd'hui seulement », tri par colonne.
- **Réglage** : case « Envoyer un rappel avant l'échéance aux clients à la météo pluvieuse ou orageuse » et nombre de jours. Un dossier dont le scénario n'a jamais été enregistré a la case cochée (sans envoi automatique) ; un dossier qui l'avait enregistré avant cette version l'a décochée jusqu'à ce qu'il la coche.

## Le droit d'opposition au ciblage

Choisir à qui envoyer le rappel selon la façon dont il paie est un **profilage** au sens du RGPD (art. 4, 4°). Le rappel n'a aucun effet juridique et ne refuse rien à personne : ce n'est pas une décision individuelle automatisée (art. 22). Le client peut néanmoins s'y opposer (art. 21) : il le dit à l'entreprise, qui clique **Ne pas cibler ce client** sur une ligne de la liste. Il ne reçoit plus de rappel avant échéance ; il reçoit toujours les relances après l'échéance, qui ne ciblent personne. Le bloc **Clients opposés au ciblage** les liste avec leur date ; **Retirer l'opposition** la lève. Droit : « Modifier les tiers ».

## L'échéancier en libre-service

- **Pour qui** : un client professionnel (une entreprise, pas un particulier), depuis son espace client, pour une facture validée qu'il n'a pas fini de payer.
- **Les bornes, les mêmes pour tous** (jamais selon une note ou la météo) : de 2 à 3 échéances (3 au plus par défaut, réglable à 2), un reste dû d'au moins 300 € TTC (réglable de 0 à 1 000 000 €), une facture qui n'est pas contestée à la plateforme agréée, qui n'a pas déjà un échéancier en cours, et une dernière échéance au plus tard au **plafond légal des délais de paiement** entre professionnels (art. L441-10 du Code de commerce : 60 jours après l'émission ou 45 jours fin de mois, la date la plus tardive).
- **Le calendrier** : la première échéance tombe un pas après la demande, la dernière au plus tard au plafond ; le pas est d'un mois au plus et d'une semaine au moins (sinon le plafond est trop proche et rien n'est proposé). Les montants sont égaux, au centime près, la dernière échéance prend l'arrondi. L'espace client montre chaque option possible (« En 2 fois », « En 3 fois ») avec ses dates et montants.
- **La demande** : le client choisit une option, donne ses prénom et nom et coche « Je m'engage, au nom de mon entreprise, à régler la facture … selon cet échéancier ». L'échéancier est **accordé tout de suite** ; l'espace client le montre ensuite avec ses échéances et qui l'a demandé. Un refus dit pourquoi, en clair (réservé aux professionnels, montant trop faible, facture contestée, plafond trop proche…).
- **Côté entreprise** : une alerte « Échéancier demandé par un client, à prendre en compte » (S3, fonctionnelle, sans nom ni montant) s'ouvre dans le centre d'alertes du dossier et se ferme quand quelqu'un clique **Vu**. La liste **Échéanciers** de l'écran Relances montre chaque échéancier (facture, client, échéances réglées ou manquées, montant, reste, demandé le, état : à jour, échéance manquée, non tenu, soldé, arrêté), avec recherche, filtre par état, « Seulement ceux à voir » et tri. Le panneau **Échéancier** de la facture montre la même chose. **Arrêter** (avec confirmation) y met fin : la facture reprend ses relances habituelles. Droit : « Émettre les factures ».
- **Les relances pendant l'échéancier** : les relances habituelles de la facture s'arrêtent. Une échéance non couverte à sa date est **manquée** : une seule relance « Échéance manquée » part, au premier délai du scénario (J+3 par défaut), avec le calendrier, ce qu'il manque pour être à jour, le lien de l'espace client et le QR code. Les règlements couvrent les échéances dans l'ordre. Si la dernière échéance passe sans que la facture soit soldée, l'échéancier n'est **pas tenu** : la facture reprend son scénario compté depuis son échéance d'origine, la relance d'échéance manquée tenant lieu de rappel amical. Soldée, la facture n'a plus rien à relancer.
- **La météo des paiements** : sous un échéancier en cours, la facture est attendue à sa prochaine échéance pas encore couverte (la prévision des encaissements suit le calendrier), et un client qui tient son échéancier n'est pas compté en retard.

## Pour « Décisions à prendre »

Une requête de l'application rend les signaux du jour, en lecture seule : un rappel avant échéance à envoyer aujourd'hui, un échéancier que l'entreprise n'a pas encore vu, une échéance manquée à relancer. Chacun a un type stable, la facture ou l'échéancier concernés, le montant en jeu, une date et un lien vers l'écran Relances. L'écran « Décisions à prendre » s'y branchera.

## Droits

Aucune permission nouvelle. Voir : « Lire le dossier ». Envoyer un rappel, prendre en compte ou arrêter un échéancier : « Émettre les factures ». Opposition : « Modifier les tiers ». Réglage : « Gérer le catalogue » (comme le reste du scénario). Le compte de service de l'espace client crée l'échéancier avec la permission de consulter l'espace client.

## API

- `GET /api/v1/reminders` rend aussi les rappels avant échéance à venir, les échéanciers et les oppositions.
- `PUT /api/v1/reminders/settings` accepte le bloc `prevention` (rappel avant échéance et bornes de l'échéancier) ; absent, il ne change pas.
- `GET /api/v1/reminders/invoices/{facture}/plan` : l'échéancier de la facture et son suivi (`DossierLire`).
- `POST /api/v1/reminders/plans/{échéancier}/acknowledge` et `/cancel` avec `{ "version": n }` : prendre en compte, arrêter (`FactureEmettre`) ; une version périmée répond 412, un échéancier déjà arrêté 409.
- `PUT /api/v1/reminders/objections/{client}` avec `{ "objects": true|false }` : poser ou retirer l'opposition (`TiersModifier`) ; 204, ou 404 pour un client inconnu.
- Espace client : `POST /espace-client/{jeton}/factures/{facture}/echeancier` (formulaire : nombre d'échéances, nom, engagement), même origine exigée, limité en débit comme le reste de l'espace client.

## Ce qui n'est pas fait

- Le QR code n'est joint que par l'hôte de l'API (démo, serveur). Un déploiement où seul le Worker envoie les relances les envoie sans QR code ; le lien de l'espace client n'y part que si l'espace client et l'adresse publique sont configurés pour lui (non réalisé dans le Helm).
- Le client ne peut pas demander d'échéancier depuis le rappel lui-même : il passe par son espace client. Les particuliers n'ont pas d'échéancier en ligne.
- Pas de frais, ni d'intérêts, ni d'accord sur mesure : un échéancier hors bornes se négocie hors de Lettra. Un échéancier arrêté ne se rouvre pas ; le client peut en demander un nouveau s'il reste dans les bornes.
- L'opposition se pose par l'entreprise à la demande du client : le client ne la pose pas lui-même dans son espace client.
- Les applications installées (poste, téléphone) montrent les listes et le réglage, mais l'espace client n'existe que sur un serveur : pas d'échéancier en libre-service en mode poste isolé.
