# Seuils à surveiller

Plusieurs règles fiscales et sociales changent quand un montant franchit un seuil : la TVA à facturer, le régime de déclaration, la catégorie de l'entreprise, les attestations à demander, les sommes à déclarer. Le dirigeant le découvre souvent après coup. Cet onglet suit ces seuils pour le dossier, projette où ils en seront à la fin de la période et dit ce qui change, assez tôt pour s'organiser avec le cabinet.

**Où** : menu Fiscalité, onglet « Seuils » des Déclarations (`/tax/thresholds`), dans le dossier du client. Une carte « Seuils à surveiller » apparaît aussi sur l'accueil et dans « En bref » quand au moins un seuil est franchi ou le sera dans les trois mois (les trois plus urgents, puis un lien vers l'onglet). Elle se tait sinon, et aussi quand la lecture est refusée.

**Les seuils suivis** (montants au catalogue daté de Lettra, chacun avec son texte) :

| Seuil | Montant | Ce qui est compté | Ce qui change au franchissement | Source |
|---|---|---|---|---|
| Franchise en base de TVA, ventes : seuil de base | 85 000 € HT (91 900 € en 2023 et 2024) | Tout le chiffre d'affaires de l'année civile | La franchise s'arrête au 1er janvier suivant : les factures portent la TVA | CGI, art. 293 B ; loi de finances pour 2025 ; loi n° 2025-1044 du 3 novembre 2025, qui maintient ces seuils |
| Franchise en base de TVA, ventes : seuil majoré | 93 500 € HT (101 000 € en 2023 et 2024) | Idem | La franchise s'arrête dès la facture qui le dépasse | Idem |
| Franchise en base de TVA, services : seuil de base | 37 500 € HT (36 800 € en 2023 et 2024) | Les prestations de services de l'année civile | La franchise s'arrête au 1er janvier suivant | Idem |
| Franchise en base de TVA, services : seuil majoré | 41 250 € HT (39 100 € en 2023 et 2024) | Idem | La franchise s'arrête dès la facture qui le dépasse | Idem |
| Régime simplifié de TVA, ventes : seuil majoré | 1 040 000 € HT en 2026 (925 000 € de 2023 à 2025) | Tout le chiffre d'affaires de l'année | Le réel normal s'applique depuis le début de l'année (ou de l'exercice) : CA3 mensuelles | Code des impositions sur les biens et services, art. L. 162-5 ; BOI-BAREME-000036 |
| Régime simplifié de TVA, services : seuil majoré | 323 000 € HT en 2026 (287 000 € de 2023 à 2025) | Les prestations de services de l'année | Idem | Idem |
| Déclaration de TVA trimestrielle : seuil de base | 1 000 000 € HT, à partir de 2027 | Tout le chiffre d'affaires de l'année | Déclarations mensuelles à partir du 1er janvier suivant | Loi de finances pour 2025, art. 38 (le régime simplifié de TVA est supprimé au 1er janvier 2027) ; impots.gouv.fr |
| Déclaration de TVA trimestrielle : seuil majoré | 1 100 000 € HT, à partir de 2027 | Idem | Déclarations mensuelles dès le mois du dépassement | Idem |
| Micro-entreprise, PME, ETI : limite de chiffre d'affaires | 2 M€, 50 M€, 1,5 Md€ HT | Le chiffre d'affaires de l'exercice | La catégorie peut changer (l'effectif et le total du bilan comptent aussi) : à vérifier avec le cabinet ; elle fixe notamment le calendrier de la facture électronique | Décret n° 2008-1354 du 18 décembre 2008, art. 3 |
| Attestation de vigilance d'un fournisseur | 5 000 € HT (3 000 € avant le 1er mai 2015) | Les factures reçues de ce fournisseur pendant l'exercice | Demander son attestation URSSAF, puis tous les six mois tant que la prestation dure | Code du travail, art. L. 8222-1 et R. 8222-1 |
| Honoraires d'un bénéficiaire (DAS 2) | 2 400 € TTC (1 200 € pour les exercices clos avant 2024) | Les virements des relevés vers son IBAN et les sommes notées hors relevés, sur l'exercice | Les sommes se déclarent avec la déclaration de résultats | CGI, art. 240 ; BOI-BIC-DECLA-30-70-20 |

Un nouveau montant s'ajoute au catalogue avec sa date d'effet, sans effacer l'ancien : une année passée se relit avec ses propres seuils. Les seuils de la DAS 2 et de l'attestation de vigilance de leurs écrans viennent du même catalogue. Comme les autres règles fiscales de Lettra, ces montants sont à faire relire par un expert-comptable avant tout usage réel.

**Quels seuils pour quel dossier** (lus dans la fiche entreprise, menu Pilotage › Entreprise) :

- En franchise en base : les deux seuils des services ; ceux des ventes dès que le dossier vend des biens (le chiffre d'affaires total se compare alors au seuil des ventes). Sans aucune vente, les seuils des services.
- Au régime simplifié (jusqu'au 31 décembre 2026) : le seuil majoré, pour les services et pour les ventes selon ce que le dossier facture.
- Au régime simplifié, ou en déclaration de TVA trimestrielle (Réglages fiscaux) : les seuils de la déclaration trimestrielle, dont le seuil de base se lit dès 2026 sur le chiffre d'affaires de l'année pour 2027.
- La catégorie choisie dans la fiche (micro-entreprise, PME, ETI) : la limite de chiffre d'affaires de cette catégorie. Une grande entreprise ou une catégorie non renseignée n'a pas de ligne.
- Les fournisseurs dont l'attestation n'est pas encore exigée dans l'écran Attestations, et les bénéficiaires d'honoraires suivis : seulement quand ils approchent de leur seuil (ils ne sont pas listés loin du seuil).
- Sans fiche entreprise, un bandeau mène à la fiche : seuls les fournisseurs et les bénéficiaires d'honoraires sont suivis.

**Comment Lettra compte** (règles écrites en C#, aucune IA) :

- Le chiffre d'affaires est celui des factures et avoirs validés, hors taxes, lignes de biens et de services, sans les débours ni les acomptes (comptés sur la facture finale) ; un avoir se retranche à sa date.
- Le cumul de la période (année civile, ou exercice pour la catégorie, la vigilance et la DAS 2) est prolongé jusqu'au dernier jour au rythme des 90 derniers jours (somme des 90 derniers jours divisée par 90, comptée même avant le début de la période ; un rythme négatif compte pour zéro). La méthode est écrite en haut de l'onglet.
- Un seuil « à surveiller » a une projection à 80 % de son montant ou plus ; « franchissement prévu » : le cumul prolongé le franchit avant la fin de la période, à la date affichée ; « franchi » : le cumul l'a dépassé, à la date du dernier passage au-dessus (un avoir peut le faire redescendre). La vigilance se déclenche dès 5 000 € atteints ; les autres seuils au-delà de leur montant.
- « Les règles changent le … » : le 1er janvier suivant pour un seuil de base, le jour du franchissement pour la franchise majorée et l'attestation de vigilance, le début de l'année (ou de l'exercice) pour le régime simplifié, le premier jour du mois pour la déclaration trimestrielle. Rien pour la catégorie et la DAS 2.
- Un seuil est « proche » quand il est franchi, ou que son franchissement est prévu dans les trois mois.

**L'écran** : en haut, la méthode et quatre compteurs (franchis, franchissement prévu, à surveiller, loin du seuil) ; puis une carte par seuil : le libellé, le sujet et la période, l'état, une barre (le cumul en plein, la projection en clair), « 36 000 € sur 41 250 € HT », la projection à la fin de la période et ce qui la dépasse, ce qui change, la date d'effet, la source et un bouton vers l'écran où agir (fiche entreprise, réglages fiscaux, attestations, honoraires). L'ordre d'ouverture est le plus urgent d'abord ; la liste se trie par part du seuil, date de franchissement ou nom, et se filtre par texte, sujet et état. Deux colonnes sur grand écran, une sur tablette, la pastille d'état sous le titre et un bouton pleine largeur sur téléphone.

**Dans la vue cabinet** : une colonne « Seuils proches » compte les seuils franchis ou prévus dans les trois mois, avec la date la plus proche (« dès le … »). Critique quand un seuil de TVA à effet immédiat (franchise majorée, régime simplifié, déclaration trimestrielle majorée) est déjà franchi, à traiter sinon. Elle ne montre ni nom, ni montant. Le lien « à faire » des tableaux de bord mène à l'onglet.

**Droits** : aucune permission nouvelle. L'onglet et la carte se lisent avec la permission de consulter le dossier. Le client (espace client) n'y a pas accès.

**API** : `GET /api/v1/tax/thresholds` (permission `DossierLire`, en-tête `X-Dossier`) rend la date du jour légal, la méthode (90 jours, 3 mois), l'absence de fiche entreprise et les lignes, la plus urgente d'abord.

**Ce qui n'est pas fait** :

- Les acquisitions intracommunautaires, les autres seuils particuliers (avocats, auteurs et artistes-interprètes, ventes à distance) et le régime des micro-entreprises en impôt sur le revenu (plafonds du micro-BIC et du micro-BNC) ne sont pas suivis.
- La catégorie de l'entreprise ne se lit que sur le chiffre d'affaires : l'effectif et le total du bilan ne sont pas dans Lettra.
- La vigilance ne compte que les factures reçues par la plateforme agréée rattachées à un fournisseur (ni les commandes, ni les contrats) ; la DAS 2 ne compte que les virements des relevés importés et les sommes notées.
- Le régime simplifié de TVA est supprimé au 1er janvier 2027 : ses seuils s'arrêtent fin 2026 au catalogue, mais le brouillon de TVA prépare encore une CA12 pour un dossier au réel simplifié (à revoir avant 2027).
- Lettra prévient ; il ne change ni la fiche entreprise, ni la TVA des factures, ni les réglages fiscaux.

**Ce qui a été vérifié** :

| Où | Vérifié |
|---|---|
| Navigateur, ordinateur, tablette et téléphone, démo Docker | Oui |
| Application MAUI (Windows, macOS, Android, iOS) | Non vérifié |
