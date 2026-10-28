# Vérifier les tiers

Avant d'entrer en relation puis tout au long de celle-ci, un professionnel assujetti à la lutte contre le blanchiment (dont l'expert-comptable) doit identifier les bénéficiaires effectifs de ses clients, s'assurer qu'aucun n'est visé par une mesure de gel des avoirs, et suivre leur situation. Une entreprise qui paie un sous-traitant doit aussi vérifier son attestation URSSAF. Cet écran interroge les registres officiels à la source pour chaque client et fournisseur du dossier, garde la réponse et signale ce qui demande un examen.

**Où** : menu Tiers, « Vérifier les tiers » (`/third-party-checks`), dans le dossier du client.

**Ce qui est interrogé** :

| Registre | Ce que Lettra demande | Ce que Lettra garde |
|---|---|---|
| Bénéficiaires effectifs (registre national des entreprises, INPI) | Le SIREN du tiers | Le nombre de bénéficiaires inscrits |
| Gel des avoirs (registre national, DG Trésor) | Rien : la liste publiée est lue en entier, le criblage se fait dans Lettra | Le nom inscrit qui ressemble au tiers, à l'un de ses bénéficiaires ou au titulaire de son compte |
| BODACC (annonces légales) | Le SIREN du tiers | La plus récente procédure collective ou radiation, sa date et son intitulé |
| URSSAF (attestation de vigilance, fournisseurs) | Le SIREN et le code de sécurité de l'attestation | Reconnu ou non ; reconnu, la vérification est notée sur l'attestation |

Un tiers sans SIREN (particulier, entreprise étrangère) n'est criblé que sur la liste des gels.

**Le criblage** : sans accents, casse, ponctuation, forme juridique ni ordre des mots, avec une faute de frappe tolérée ; un nom abrégé ne compte que s'il garde au moins deux mots, pour ne pas signaler tous les « Martin ». Les autres noms d'une personne gelée (alias) sont criblés aussi. Une ressemblance n'est pas une certitude : l'écran rappelle de comparer la date de naissance ou l'adresse sur le registre avant de conclure, de noter l'analyse dans la fiche anti-blanchiment, et de ne jamais prévenir le tiers d'un soupçon.

**Où en est chaque tiers** :

| État | Quand |
|---|---|
| À examiner | Gel possible, procédure collective, radiation ou code URSSAF non reconnu, quel que soit l'âge de la vérification |
| Jamais vérifié | Aucune vérification |
| À refaire | Rien trouvé, mais il y a plus de six mois |
| Rien à signaler | Le reste du temps |

L'écran montre les compteurs (à examiner, à vérifier, rien à signaler), un bouton « Vérifier les N tiers à vérifier » qui les passe un à un (avec la progression, arrêt au premier refus), puis la liste : ceux à examiner d'abord, recherche par nom ou SIREN, filtres par type (client, fournisseur) et par état (dont « À traiter »), tri par tiers, date de vérification ou état. Chaque ligne a son bouton « Vérifier » (« Revérifier » ensuite) et mène à la fiche du tiers. Sur téléphone, chaque tiers devient une carte.

**Avec la fiche anti-blanchiment** : « Reprendre du registre » remplit les bénéficiaires effectifs de l'entreprise du dossier (d'après le SIREN de sa fiche entreprise), coche la pièce « registre des bénéficiaires effectifs » et signale un nom proche du registre des gels. Rien n'est enregistré : l'utilisateur relit, complète puis enregistre.

**Avec les attestations de vigilance** : « Vérifier le code » fait vérifier le code de l'attestation par l'URSSAF. Reconnu, la date de vérification est notée ; non reconnu, l'écran demande une nouvelle attestation et de ne pas payer en l'état.

**Dans la vue cabinet** : une colonne « Tiers à examiner » compte les tiers dont la dernière vérification demande un examen, critique dès qu'un nom ressemble à une personne gelée. Elle ne montre ni nom, ni motif.

**Droits** : aucune permission nouvelle. Voir l'écran : « Anti-blanchiment : consulter » (manager, collaborateur étendu, collaborateur). Vérifier un tiers ou reprendre les bénéficiaires du registre : « Anti-blanchiment : gérer » (manager, collaborateur étendu). Faire vérifier le code d'une attestation : « Tiers : modifier », comme le reste de l'écran des attestations. Ni l'auditeur ni le client (espace client) ne voient ces résultats (interdiction de divulgation, art. L561-18).

**Données personnelles** : le nom d'une personne gelée et l'intitulé d'une annonce publique sont gardés dans la base du dossier, comme trace de la vigilance. Rien n'est écrit dans les journaux ni dans les indicateurs (la vue cabinet ne garde qu'un nombre).

**Dans ce prototype** : les quatre registres sont **simulés**, sans aucun appel extérieur. Les réponses sont fictives et se déduisent du SIREN, toujours les mêmes pour un même SIREN : un à trois bénéficiaires détenteurs du capital ; pour un SIREN sur dix-sept, l'un d'eux est une personne fictive du registre des gels ; un SIREN sur onze a une procédure collective publiée il y a vingt à soixante-dix-neuf jours, un sur dix-neuf est radié ; tout code d'attestation est reconnu sauf ceux qui commencent par « ZZ ». La liste des gels simulée ne contient que des noms inventés. Brancher les vrais registres demande les accès (API RNE de l'INPI pour les professionnels assujettis, flux du registre national des gels, API du BODACC, service de vérification de l'URSSAF) : chacun se branche derrière son port, sans toucher aux écrans.

**Ce qui n'est pas fait** : les vrais registres (voir ci-dessus) ; la liste des personnes politiquement exposées (il n'existe pas de registre public) ; une nouvelle vérification automatique la nuit ; l'abonnement aux annonces du BODACC.

**API** (toutes sous `/api/v1`) :

- `GET /third-party-checks` (Anti-blanchiment : consulter) : les clients et fournisseurs et leur dernière vérification.
- `POST /third-party-checks/{Customer|Supplier}/{id}` (Anti-blanchiment : gérer) : vérifier un tiers ; 404 tiers inconnu ou supprimé, 503 registre injoignable.
- `POST /attestations/{supplierId}/verification` (Tiers : modifier) : faire vérifier le code d'une attestation ; répond 200 avec la réponse de l'URSSAF, même non reconnue.
- `GET /vigilance/registered-owners` (Anti-blanchiment : gérer) : les bénéficiaires de l'entreprise du dossier lus au registre, criblés.

## Ce qui a été vérifié

| Où | Vérifié |
|---|---|
| Navigateur, ordinateur et téléphone, démo Docker (registres simulés) | Oui |
| Vrais registres (INPI, DG Trésor, BODACC, URSSAF) | Non réalisé |
| Application MAUI (Windows, macOS, Android, iOS) | Non vérifié |
