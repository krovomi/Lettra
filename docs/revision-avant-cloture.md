# Révision avant clôture

Avant d'arrêter les comptes, le collaborateur du cabinet passe en revue les soldes anormaux et les pièces en suspens. Lettra fait ces contrôles automatiquement, à partir des écritures enregistrées à la validation des pièces, des encaissements, du relevé bancaire et des brouillons.

**Où** : Comptabilité, onglet « Révision ». L'écran demande le droit « Dossier : consulter ».

**Date de clôture** : par défaut, la clôture du dernier exercice terminé (réglages fiscaux). Une autre date se choisit dans le champ, et « Aujourd'hui » donne la situation du jour. Les autres onglets de la comptabilité suivent alors l'exercice révisé.

**Les contrôles** (les plus graves en tête) :

| Contrôle | Ce qui est vérifié | Gravité |
|---|---|---|
| Clients créditeurs | Un client dont le compte 411 est au crédit à la clôture (trop-perçu, avoir non remboursé) | À traiter |
| Acomptes reçus non imputés | Un acompte encore au compte 4191 : la facture finale reste-t-elle à émettre ? | À regarder |
| Créances douteuses | Une facture non contestée en retard de plus de six mois (180 jours) à la clôture : une dépréciation est à envisager | À traiter |
| Créances litigieuses | Une facture contestée sur la plateforme agréée | À traiter |
| Rapprochement bancaire | Des opérations du relevé encore à rapprocher ; à traiter si la plus ancienne date d'avant la clôture | À traiter ou à regarder |
| TVA et chiffre d'affaires | Le taux apparent de TVA collectée (44571 et 44574) rapporté aux produits (706 et 707) de l'exercice. Au-dessus de 20 %, un taux ou un compte est faux ; aucune TVA du tout est à justifier (franchise, export, autoliquidation) | À traiter ou à regarder |
| Brouillons oubliés | Des factures créées avant la clôture et jamais validées | À regarder |

Les comptes de bilan (411, 4191) se lisent depuis l'origine jusqu'à la clôture ; les produits et la TVA, sur l'exercice. Le reste dû d'une facture suit la même règle que le tableau des délais de paiement.

**Chaque anomalie mène à ses pièces** : le grand livre du client sur l'exercice, la facture, la liste des factures (pour un brouillon) ou l'écran de rapprochement bancaire. Une longue liste se replie après cinq anomalies (« Tout afficher »). Un filtre garde les contrôles d'une gravité.

**Limites** (à relire avec un expert-comptable) :
- les fournisseurs débiteurs et les comptes d'attente viendront avec les factures reçues ;
- le seuil de six mois pour une créance douteuse est indicatif ; la dépréciation reste une décision du cabinet ;
- le taux apparent de TVA mélange les taux réduits : il ne signale que l'excès au-dessus du taux normal.

**API** : `GET /api/v1/accounting/closing-review?closingDate=AAAA-MM-JJ` (sans date : la dernière clôture) exige « Dossier : consulter » et renvoie `ClosingReviewResponse`.
