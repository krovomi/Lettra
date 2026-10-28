# Connaissance du client (anti-blanchiment)

L'expert-comptable est assujetti à la lutte contre le blanchiment de capitaux et le financement du terrorisme (LCB-FT). Il doit identifier son client et ses bénéficiaires effectifs avant d'entrer en relation, évaluer le risque, le revoir régulièrement et garder ces éléments cinq ans après la fin de la relation (Code monétaire et financier, art. L561-4-1, L561-5, L561-10 et L561-12). L'Ordre le vérifie lors de ses contrôles. Cet écran tient cette fiche pour chaque dossier.

**Où** : menu Comptabilité, « Anti-blanchiment » (`/vigilance`), dans le dossier du client.

**La fiche** :

| Partie | Contenu |
|---|---|
| Identification | Pièces reçues : extrait K-bis de moins de trois mois et pièce d'identité du dirigeant (exigés), extrait du registre des bénéficiaires effectifs, statuts ; date de vérification de l'identité |
| Bénéficiaires effectifs | Dix au plus : nom, à quel titre (plus de 25 % du capital, plus de 25 % des droits de vote, contrôle par d'autres moyens, ou représentant légal à défaut), part détenue |
| Évaluation du risque | Facteurs (personne politiquement exposée, pays à risque élevé, activité qui manie beaucoup d'espèces, montage complexe, client jamais rencontré, opérations inhabituelles), niveau (faible, standard, élevé) et justification |
| Revue | Date de la revue (le jour de l'enregistrement par défaut) et, le cas échéant, fin de la relation |

**Les règles** :

- Une personne politiquement exposée ou un pays à risque élevé impose le niveau élevé (art. L561-10) ; l'écran le rappelle dès que la case est cochée.
- Un niveau faible ou élevé doit être justifié.
- Une part de capital ou de droits de vote dépasse 25 % sans aller au-delà de 100 % ; les parts d'une même nature additionnées ne dépassent pas 100 %.
- Aucune date dans le futur.
- Chaque enregistrement vaut revue : Lettra note qui l'a faite et quand.

**Où en est la fiche** :

| État | Quand |
|---|---|
| À faire | Aucune fiche |
| Incomplète | Il manque l'extrait K-bis, la pièce d'identité du dirigeant, la date de vérification ou un bénéficiaire effectif |
| À revoir | La date de la prochaine revue est atteinte |
| À jour | Le reste du temps |
| Relation terminée | Une date de fin est saisie : la fiche est à garder jusqu'à cinq ans après |

La prochaine revue tombe un an après la dernière pour un risque élevé, deux ans pour un risque standard, trois ans pour un risque faible. Ces rythmes reprennent la pratique courante des cabinets ; la loi impose une vigilance constante sans fixer de délai.

**Avec la lettre de mission** : la lettre ne se signe pas (date de signature) tant que la fiche n'est pas à jour. Une lettre déjà signée le reste.

**Dans la vue cabinet** : une colonne « Anti-blanchiment » signale le dossier dont la fiche est à faire ou incomplète (à traiter), dont la revue est passée (critique pour un risque élevé), ou dont la revue tombe dans les trente jours (pour information). Elle ne montre ni nom, ni niveau de risque.

**Droits** : deux nouvelles permissions. Lire la fiche : manager, collaborateur étendu et collaborateur. La remplir ou la revoir : manager et collaborateur étendu. Ni l'auditeur ni le client (espace client) n'y ont accès : la fiche reste au cabinet (interdiction de divulgation, art. L561-18). Les modifications concurrentes sont refusées (version lue).

**Ce qui n'est pas fait** : le dépôt des pièces elles-mêmes (Lettra note lesquelles ont été reçues) ; la consultation automatique de la liste des personnes politiquement exposées ; la déclaration de soupçon à Tracfin ; l'effacement automatique cinq ans après la fin de la relation. « Reprendre du registre » remplit les bénéficiaires effectifs depuis le registre et les crible sur le registre des gels (simulés dans ce prototype, voir [Vérifier les tiers](verifier-les-tiers.md)).

## Données personnelles

La fiche contient des données personnelles : noms des bénéficiaires effectifs, qualité de personne politiquement
exposée, justification du risque. Elle reste dans la base du dossier, n'apparaît ni dans les journaux ni dans les
indicateurs (la vue cabinet ne reçoit qu'un compteur, une urgence et une date) et n'est lue que par les membres du
cabinet qui ont la permission. Elle se garde cinq ans après la fin de la relation, comme la loi l'impose. Ce
traitement est à inscrire au registre des traitements et dans l'analyse d'impact.

## API

| Méthode | Adresse | Droit | Rôle |
|---|---|---|---|
| GET | `/api/v1/vigilance` | Consulter la fiche anti-blanchiment (en-tête `X-Dossier`) | fiche du dossier et son état |
| PUT | `/api/v1/vigilance` | Gérer la fiche anti-blanchiment (en-tête `X-Dossier`) | enregistrer la fiche, ce qui vaut revue ; la version lue est exigée (en-tête `If-Match`) |

La base de chaque dossier gagne deux tables (la fiche du client et ses bénéficiaires effectifs), pour SQLite,
SQL Server et PostgreSQL. La migration ne touche aucune donnée existante.

## Ce qui a été vérifié

| Où | Vérifié |
|---|---|
| Navigateur, ordinateur et téléphone, démo Docker | Oui |
| Application MAUI (Windows, macOS, Android, iOS) | Non vérifié |
