# Radar de prévention des difficultés

Un expert-comptable voit souvent les difficultés d'un client trop tard, au bilan. Le radar lit les données que le dossier a déjà (factures, encaissements, TVA, fournisseurs, vérification des tiers) et signale, chaque jour, les dossiers dont les chiffres annoncent un manque d'argent ou une fragilité. Il aide à décider d'appeler le dirigeant tôt, au moment où une solution (échéancier, relance, rendez-vous avec la banque, procédure de prévention) est encore possible. Il ne décide rien.

**Où** :

- menu, juste sous « Vue cabinet » (hors sections), « Radar de prévention » (`/cabinet/prevention`) : un dossier par ligne, les plus préoccupants d'abord ;
- dans le dossier, « Radar de prévention » (`/prevention`) : chaque signal avec sa raison et ses chiffres, les seuils du dossier et le courrier type ;
- à l'accueil du dossier, la carte « Signaux de prévention » (pour le cabinet seulement) ;
- dans la vue cabinet, une colonne « Radar de prévention » et un lien vers la liste ;
- dans « Décisions à prendre », une décision de rendez-vous quand le dossier est « à appeler » (voir plus bas).

Les mots « prévention », « difficultés », « alerte précoce », « entreprises fragiles », « trésorerie » et « procédure collective » le trouvent aussi dans « Aller à… » et Ctrl+K.

**Les sept signaux** :

| Signal | Allumé quand | Grave (à traiter vite) quand | Effet chiffré | Échéance |
|---|---|---|---|---|
| Trésorerie des prochaines semaines | Le solde prévu passe sous zéro dans l'horizon réglé (8 semaines par défaut), à partir du solde bancaire saisi par le cabinet, des encaissements attendus (météo des paiements), des factures fournisseurs à payer et de la TVA à reverser | Sous zéro dans la première moitié de l'horizon | Le manque au plus bas | La semaine où le solde passe sous zéro |
| Délais de paiement des clients | Le délai moyen de paiement (DSO) s'allonge trois mois de suite, d'au moins le seuil (5 jours par défaut) | Hausse d'au moins trois fois le seuil | La trésorerie restée chez les clients | — |
| Chiffre d'affaires face au secteur | Le chiffre d'affaires baisse sur un an et décroche d'au moins le seuil (15 points par défaut) par rapport à la médiane des dossiers du même secteur (code APE), s'ils sont au moins cinq | Écart d'au moins deux fois le seuil | — | — |
| TVA à payer et encaissements | La TVA de la prochaine échéance (dans les 45 jours) dépasse les encaissements attendus d'ici là | Échéance dans les 15 jours | Le manque pour payer la TVA | La date limite de paiement |
| Dépendance au premier client | Le premier client pèse au moins le seuil (50 % par défaut) du chiffre d'affaires des douze derniers mois | — | Le chiffre d'affaires réalisé avec ce client | — |
| Clients en procédure collective | La dernière vérification au BODACC d'un client (écran « Vérifier les tiers ») montre une procédure collective | Une somme reste due par ce client | Ce qui reste dû | Deux mois après la publication (délai pour déclarer la créance) |
| Facturé et encaissé | L'écart entre facturé et encaissé grandit trois mois de suite et dépasse le seuil (20 % du facturé par défaut) | Écart d'au moins deux fois le seuil | L'écart en euros | — |

Un signal sans assez de données n'est pas évalué, et l'écran dit pourquoi (solde bancaire jamais saisi ou plus vieux que 45 jours, pas assez de factures, pas de code APE, moins de cinq dossiers du même secteur, régime de TVA inconnu, aucun client vérifié…) avec le lien pour compléter. Un signal coupé par le cabinet est marqué « désactivé ».

**Le niveau d'attention** :

| Niveau | Quand |
|---|---|
| Calme | Aucun signal allumé |
| À surveiller | Au moins un signal allumé |
| À appeler | Un signal grave, ou au moins trois signaux allumés en même temps (nombre réglable, de 2 à 7) |

**Les seuils du dossier** (bloc « Seuils du dossier ») : le solde bancaire du jour et sa date (point de départ de la trésorerie prévue ; la date est proposée au jour même), l'horizon de la trésorerie (2 à 26 semaines), la hausse du délai de paiement (1 à 60 jours), l'écart au secteur (5 à 100 points), la part du premier client (20 à 100 %), l'écart facturé-encaissé (5 à 100 %), le nombre de signaux qui fait passer à « à appeler » (2 à 7), et les signaux suivis (chacun peut être coupé). « Valeurs proposées » remet les seuils de Lettra en gardant le solde saisi. Les valeurs proposées sont à relire avec un expert-comptable. Le serveur revérifie chaque borne ; deux personnes qui règlent le même dossier en même temps : la seconde est prévenue (version).

**Le courrier type** : « Préparer le courrier » écrit, en français ou en anglais, une proposition de rendez-vous adressée au dirigeant, qui cite les sujets à aborder en termes neutres (« la trésorerie des prochaines semaines », « les délais de paiement de vos clients »…), jamais le niveau d'attention ni un chiffre. Le collaborateur relit et modifie l'objet et le texte, puis l'envoie par e-mail (si l'envoi est configuré ; MailPit dans la démo), le télécharge en PDF ou le copie. Rien ne part sans son clic.

**Dans « Décisions à prendre »** : quand un dossier passe « à appeler », Lettra propose aussi, dans l'écran [Décisions à prendre](decisions-a-prendre.md) du dossier, une décision « Proposer un rendez-vous de prévention au dirigeant » (urgente, pour le cabinet). Elle donne le nombre de signaux allumés et le plus grave, et mène au radar. Elle ne prépare aucune action : le courrier se relit et s'envoie depuis le radar, jamais tout seul. Une décision ignorée revient le mois suivant si le dossier est toujours « à appeler » ; elle se règle d'elle-même quand le dossier n'est plus « à appeler ». Elle ne se voit qu'avec le droit de consulter le radar. Le compteur « Décisions » de la vue cabinet et des tableaux de bord ne la compte pas, et Grafana non plus : le niveau d'attention ne sort pas du dossier, même sous forme de compteur.

**La liste du cabinet** : compteurs « à appeler », « à surveiller », « calmes » ; recherche par nom, filtre par niveau, tri par dossier, niveau, nombre de signaux ou échéance ; « Ouvrir le radar » ouvre le dossier sur son radar. Sur téléphone, chaque dossier devient une carte. La liste lit la vue cabinet (niveau, nombre de signaux allumés, échéance la plus proche), calculée chaque nuit ou à « Actualiser » ; une vue calculée avant le radar est recalculée une fois à l'ouverture de la liste.

**Droits** :

- « Voir le radar de prévention » (`PreventionConsulter`) : manager, collaborateur étendu, collaborateur. Lire le radar, la carte, la colonne de la vue cabinet, préparer et envoyer le courrier.
- « Régler le radar » (`PreventionRegler`) : manager, collaborateur étendu. Régler les seuils et le solde bancaire.
- Ni l'auditeur ni le client (espace client) ne voient le radar : la carte de l'accueil ne s'affiche pas pour eux (sans erreur), la colonne de la vue cabinet ne leur est pas montrée et ne compte pas dans l'urgence du dossier.

**Garde-fous (RGPD, AI Act)** :

- Aucune décision automatique (RGPD, article 22) : le radar signale, le collaborateur décide d'appeler ou non ; le courrier ne part que par son clic.
- Aucun modèle d'IA ne note les entreprises : des règles écrites, lisibles dans l'écran (bloc « Comment le radar décide »), les mêmes pour tous les dossiers. Une entreprise individuelle est une personne physique : la note d'une personne par un système d'IA serait à haut risque (AI Act, annexe III), c'est pourquoi le radar n'en utilise pas.
- Le niveau reste dans le cabinet : il n'est jamais montré au client ni à l'auditeur, ni écrit dans le courrier, ni envoyé aux indicateurs (Prometheus, Grafana), ni écrit dans les journaux. La vue cabinet ne garde que le niveau, le nombre de signaux allumés et l'échéance la plus proche : ni nom, ni montant, ni quel signal.
- La comparaison au secteur exige au moins cinq dossiers du même secteur, pour ne rien laisser deviner d'un autre client du cabinet.
- Les clients doivent en être informés : un paragraphe est proposé pour la lettre de mission (voir plus bas).

**Données** : les seuils sont gardés dans la base du dossier (table `PreventionSettings`) ; le radar se calcule à la demande, rien d'autre n'est stocké. Les données de la démonstration posent un solde bancaire une seule fois par dossier (jamais par-dessus un réglage fait à la main).

**Dans ce prototype** : le solde bancaire se saisit à la main (la banque connectée ne l'alimente pas encore) ; la comparaison au secteur n'existe que là où les indicateurs sont collectés (serveur du cabinet), pas sur un poste isolé.

**Ce qui n'est pas fait** : le solde bancaire lu à la banque connectée ; un historique du niveau dans le temps  ; une notification hors de Lettra.

**API** (toutes sous `/api/v1/insights/prevention`) :

- `GET /` (Voir le radar) : le niveau, les sept signaux (état, raison, chiffres, effet, échéance, détail) et les seuils ; `canAdjust` dit si l'utilisateur peut les régler.
- `GET /summary` (Voir le radar, filtré) : la carte de l'accueil ; répond 200 « cachée » à qui n'a pas le droit, sans refus.
- `PUT /settings` (Régler le radar) : les seuils et le solde ; 400 valeur hors bornes, 412 version dépassée.
- `GET /letter?language=fr-FR|en-US` (Voir le radar) : le courrier proposé ; `canSend` faux si l'envoi d'e-mails n'est pas configuré.
- `POST /letter` (Voir le radar) : envoyer le courrier relu ; 204 envoyé, 400 destinataire, objet ou texte invalide, 409 envoi d'e-mails non configuré.

**Pour les prochaines features** : la requête `GetPreventionRadarQuery` (module Exports) rend pour chaque signal son état, l'effet chiffré (`Impact`, en euros), l'échéance (`DueOn`) et des chiffres nommés par des clés stables (`lowestBalance`, `tiedUp`, `dueDate`…). La règle de décision `prevention.rendez-vous` la lit telle quelle ; une autre règle peut faire de même.
