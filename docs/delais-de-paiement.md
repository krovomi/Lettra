# Délais de paiement

Le Code de commerce plafonne les délais de paiement entre professionnels (article L441-10). Le délai convenu ne peut dépasser 60 jours après la date de facture, ou, par dérogation, 45 jours fin de mois. Les sociétés dont les comptes sont certifiés publient dans leur rapport de gestion un tableau des factures non réglées à la clôture, par tranche de retard (article D441-6, modèle de l'annexe 4-1).

**Où** : Déclarations, onglet « Délais de paiement ». L'écran demande le droit « Dossier : consulter ».

**Date de clôture** : par défaut, la clôture du dernier exercice terminé (réglages fiscaux). Une autre date se choisit dans le champ, et « Aujourd'hui » donne la situation du jour.

**Tableau du rapport de gestion** :
- **(A) Tranches** : « 0 jour (indicatif) » (factures échues le jour même de la clôture), 1 à 30 jours, 31 à 60, 61 à 90, 91 jours et plus, puis le total « 1 jour et plus ». Pour chaque tranche, l'écran donne le nombre de factures, le montant TTC restant dû et le pourcentage du chiffre d'affaires TTC de l'exercice.
- **(B) Factures exclues** : celles contestées sur la plateforme agréée (créances litigieuses).
- **(C) Délai de référence** : le délai contractuel le plus fréquent de l'exercice, ou les délais légaux.
- Les factures non encore échues sont comptées à part.

**Reste dû à la clôture** : le montant TTC, moins les avoirs émis et les encaissements reçus jusqu'à cette date. Ce sont ceux saisis dans Lettra ou ceux déclarés à la plateforme, s'ils sont plus élevés. Un encaissement contre-passé avant la clôture rend la somme due. Les acomptes comptent comme des factures.

**Échéances au-delà du plafond légal** : ce sont les factures de l'exercice à un client professionnel dont l'échéance dépasse le plafond. Le plafond retenu est la plus tardive des lectures admises : 60 jours, fin de mois puis 45 jours, ou 45 jours puis fin de mois. Un bandeau les signale en haut de l'écran.

**Liste des factures non réglées** : on peut chercher par numéro ou client, filtrer par tranche (dont « non échues » et « litigieuses »), trier par retard, montant, client ou numéro. La liste se pagine par 25.

**Limites** (à relire avec un expert-comptable) :
- la lecture de « 0 jour » ;
- les délais propres à certains secteurs (transport, denrées périssables, export), qui ne sont pas traités ;
- le côté « factures reçues des fournisseurs », qui viendra quand Lettra les recevra.

**API** : `GET /api/v1/tax/payment-delays?closingDate=AAAA-MM-JJ` (sans date : la dernière clôture) exige « Dossier : consulter » et renvoie `PaymentDelaysResponse`.
