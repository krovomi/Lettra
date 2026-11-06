# Décisions à prendre

Lettra calcule déjà beaucoup de signaux (factures en retard, échéances fiscales, seuils à surveiller, IBAN changés, attestations, fiche anti-blanchiment), chacun sur son écran. « Décisions à prendre » les rassemble en une liste de choses à décider, les plus urgentes d'abord, et prépare l'action quand il y en a une. Ce sont des **règles fixes**, écrites et testées : aucune IA ne décide, ne classe ni n'écrit ces phrases.

**Où** : menu, juste sous « En bref » (`/decisions`), et le compteur « Décisions » de la vue cabinet. Les mots « à faire », « priorités », « urgences », « anticiper » le trouvent aussi dans « Aller à… » et Ctrl+K.

## Ce que Lettra surveille

| Type | Quand une décision est proposée | Urgence | Action préparée | Il faut le droit de |
|---|---|---|---|---|
| Relance due (`relance.due`) | Une facture échue atteint aujourd'hui un palier du scénario de relance. Rien quand l'envoi automatique des relances est réglé (la passe quotidienne s'en charge). | Normale ; urgente pour la dernière relance et la mise en demeure | « Envoyer la relance » par e-mail, si le client a une adresse et qu'un serveur d'envoi est branché | émettre les factures |
| Rappel avant échéance (`avant-le-retard.rappel`) | Un client que la météo des paiements cible (et qui ne s'y est pas opposé) doit recevoir aujourd'hui son rappel aimable avant l'échéance. Rien quand l'envoi automatique est réglé | Normale | Aucune : la décision mène à l'écran des relances | émettre les factures |
| Échéance manquée (`avant-le-retard.echeance-manquee`) | Une échéance d'un échéancier n'est pas réglée et sa relance est due aujourd'hui. Une décision par échéance manquée. Rien quand l'envoi automatique est réglé ; la relance due de cette facture n'est alors pas proposée une seconde fois en « Relance due » | Urgente | Aucune : la décision mène à l'écran des relances | émettre les factures |
| Échéancier à voir (`avant-le-retard.echeancier-a-voir`) | Un client a demandé dans son espace client un échéancier que l'entreprise n'a pas encore marqué « Vu ». Proposée même avec l'envoi automatique | Normale | Aucune : la décision mène à l'écran des relances (« Vu » ou « Arrêter ») | émettre les factures |
| Échéance fiscale (`fiscal.echeance`) | Une déclaration du calendrier fiscal est en retard ou due dans les sept jours, et pas marquée déposée | Critique en retard ; urgente à trois jours ou moins ; normale sinon | Aucune : la décision mène au calendrier fiscal | exporter le FEC (celui qui marque une déclaration déposée) |
| Seuil proche (`seuil.proche`) | Un seuil de « Seuils à surveiller » est franchi, ou son franchissement est prévu dans les trois mois (franchise ou régime de TVA, déclaration trimestrielle, catégorie de l'entreprise, attestation de vigilance d'un sous-traitant, honoraires d'un bénéficiaire pour la DAS 2). Une décision par seuil et par période : ignorée, elle revient à la période suivante | Critique quand un seuil de TVA à effet immédiat est franchi (la TVA des factures change dès maintenant) ; urgente pour tout autre seuil franchi ; normale quand le franchissement est prévu | Aucune : la décision mène à « Seuils à surveiller » (le geste est dans la fiche entreprise, les réglages fiscaux, ou chez le cabinet) | exporter le FEC |
| IBAN à vérifier (`iban.a-verifier`) | Le nouvel IBAN d'un fournisseur attend sa vérification | Critique si le pays de la banque a changé ; urgente sinon | Aucune : la vérification se fait par un autre canal | modifier les tiers |
| Attestation à renouveler (`attestation.a-renouveler`) | L'attestation de vigilance URSSAF d'un fournisseur suivi manque, a expiré, n'est pas vérifiée, ou expire dans les trente jours | Critique si expirée ; urgente si manquante ou non vérifiée ; normale si elle expire bientôt | Aucune | modifier les tiers |
| Fiche anti-blanchiment (`lcb-ft.fiche`) | La fiche du client est à faire, incomplète, à revoir, ou sa revue tombe dans les trente jours | Critique si la revue est passée pour un risque élevé ; urgente sinon ; normale à l'approche | Aucune | gérer la vigilance (la fiche est confidentielle ; la décision ne dit pas le niveau de risque) |

Chaque décision a une clé : son type et son sujet (la facture et le niveau de relance, la facture ou l'échéancier et sa date d'échéance, l'obligation et sa période, le changement d'IBAN, le fournisseur et la fin de validité, la fiche et sa date de revue). Une même décision n'est proposée qu'une fois ; la relance suivante, la période suivante ou une attestation renouvelée sont de nouvelles décisions.

## Répondre

- **Accepter** (quand une action est préparée et que le cadran le permet) : Lettra envoie la commande de l'écran concerné, avec les droits de l'utilisateur (par exemple, l'envoi de la relance vérifie le droit d'émettre les factures). En cas de refus, la décision reste ouverte et le message dit pourquoi.
- **Voir le détail** : ouvre l'écran qui traite le sujet.
- **Plus tard** : demain, dans 7 jours ou dans 30 jours ; la décision revient d'elle-même ce jour-là. « Annuler » dans le message la remet tout de suite.
- **Ignorer** : la décision n'est plus proposée. « Annuler » dans le message, ou « Remettre dans la liste » (filtre « Traitées »), la fait revenir.
- **Réglée d'elle-même** : quand la situation est réglée (l'IBAN est confirmé, la déclaration marquée déposée, la facture payée), la décision se ferme à l'actualisation suivante.

Chaque réponse part avec la version lue : si un collègue a répondu entre-temps, la liste est relue et un message le dit. Chaque action est confirmée dans le toaster.

## Le cadran d'autonomie

Réglé par dossier, par un manager du cabinet (les autres le voient en lecture seule) :

| Niveau | Ce que fait Lettra |
|---|---|
| Montrer seulement | Signale la décision ; on la traite depuis l'écran concerné. Aucun bouton « Accepter ». |
| Préparer l'action (par défaut) | Prépare l'action ; un clic sur le bouton la fait. |
| Faire et prévenir | **Non réalisé.** Lettra ferait seul les actions réversibles et préviendrait. Un test d'architecture tient la liste fermée des actions réversibles ; elle est vide (un e-mail envoyé ne se reprend pas). |

## Actualisation

Les règles tournent chaque jour pour chaque dossier, pendant le calcul des compteurs de la vue cabinet (pas de tâche de plus), et quand quelqu'un clique « Actualiser ». L'écran actualise aussi de lui-même des décisions calculées il y a plus de six heures. Une règle en échec n'empêche pas les autres et laisse ses décisions telles quelles.

## Droits

- **Traiter les décisions** (voir, accepter, remettre à plus tard, ignorer, actualiser) : collaborateur, collaborateur étendu, manager. Pas le client du portail ni l'auditeur.
- **Régler le cadran** : manager.
- En plus, chaque décision n'est montrée qu'à qui a le droit de sa règle (tableau ci-dessus) : un collaborateur voit les IBAN et les attestations, pas les relances à envoyer ; un collaborateur étendu voit aussi les relances et la fiche anti-blanchiment ; un manager voit tout, dont les échéances fiscales et les seuils.

## Données

Les décisions sont gardées dans la base du dossier (tables `Decisions` et `DecisionPolicy`), avec leurs faits (numéro de facture, nom du client ou du fournisseur, dates, montant). Rien ne sort du dossier : le compteur de la vue cabinet ne donne que le nombre, le niveau et l'échéance la plus proche, sans nom ni montant ; les journaux ne contiennent ni nom ni montant. Une décision close est effacée treize mois après sa clôture.

## Sur chaque appareil

Écran en cartes, les mêmes du grand écran au téléphone : urgence, pour qui, échéance (« à faire aujourd'hui », « avant le … » ou « dépassée depuis le … »), montant en jeu, puis la phrase et les boutons. Vingt décisions par page (le dossier de démonstration en a près de cent, surtout des relances). Barre de filtres commune (recherche, état ; pour qui, urgence et tri repliés derrière « Filtres » sur téléphone), tri par urgence, échéance, montant ou intitulé. Navigateur et PWA, application installée reliée au serveur, poste isolé : mêmes règles, mêmes écrans.

## Architecture

- Domaine : l'agrégat `Decision` (clé type + sujet, état, faits, action préparée) et `DecisionPolicy` (le cadran).
- Application : les ports `IDecisionRule` (une règle lit ses stores et propose, sans médiateur ni IA), `IDecisionActionHandler` (l'action envoie une commande existante par le médiateur), `IDecisionStore` ; l'actualisation `DecisionRefresher` ; les cas d'usage liste, actualiser, répondre, régler le cadran ; la source d'indicateur de la vue cabinet.
- Les règles vivent avec leur module : relances, « Avant le retard », échéances fiscales et seuils dans Exports, IBAN, attestations et anti-blanchiment dans le cœur.
- API : `GET /api/v1/decisions`, `POST /api/v1/decisions/refresh`, `POST /api/v1/decisions/{id}/answer` et `PUT /api/v1/decisions/policy` (les deux dernières avec `If-Match`).
- Tests d'architecture : les règles ne dépendent ni du médiateur ni des assemblages d'IA ; aucune action ne se dit réversible hors de la liste fermée.

## Ajouter une règle (pour les prochaines features)

Une classe `IDecisionRule` dans le module qui a les signaux (`services.AddScoped<IDecisionRule, …>()`), avec son type, sa version, la permission qu'il faut pour agir, et ses phrases `Decision.<type>.Title`, `.Reason` et `.Rule` en français et en anglais. Les faits suivent trois conventions : une valeur qui commence par `@` est une clé de libellé, une date s'écrit `aaaa-mm-jj`, un fait dont le nom finit par `Amount` est un montant en euros. Un libellé peut lui-même porter des faits entre accolades (un seul niveau) : « Honoraires versés à {party} ». Si la règle prépare une action, un `IDecisionActionHandler` qui envoie la commande existante. Un test de la règle (proposé, pas proposé).

## Limites

- Le niveau « Faire et prévenir » n'est pas réalisé.
- Pas de notification hors de Lettra (e-mail, téléphone) : les décisions se consultent dans l'écran et la vue cabinet.
- Au-delà de cinquante changements d'IBAN en attente, l'écran des changements d'IBAN prend le relais.
- Une décision ignorée ne revient pas d'elle-même, même si la situation dure.
