# Abonnements : formules et options à la carte (mode 5)

> Conception seulement. Aucun prix : les montants se fixent avec le commercial. Rien n'est facturé par Lettra
> aujourd'hui.

## Les profils de client

| Profil | Qui | Mode | Ce qu'il attend |
|---|---|---|---|
| **Indépendant** | Un professionnel seul qui facture ses clients | 1 (poste isolé) | Un prix fixe, tout sur son poste |
| **Petit cabinet** | 1 à 5 collaborateurs, jusqu'à quelques centaines de dossiers | 5 mutualisée | Un forfait simple, qui ne demande pas de compter |
| **Cabinet moyen** | 5 à 30 collaborateurs | 5 mutualisée | Payer selon l'activité réelle, sans payer les dossiers dormants |
| **Grand cabinet ou réseau** | Plus de 30 collaborateurs, des milliers de dossiers | 5 dédiée | Un prix prévisible à l'année, une isolation contractuelle |
| **Cabinet qui garde ses données** | Toute taille | 2 ou 3 (sur site) | Une licence et du support, sans hébergement |
| **Entreprise cliente du cabinet** | Le client du cabinet, dans son espace client | Celui du cabinet | Rien à payer à Lettra : c'est le cabinet qui choisit d'ouvrir l'espace |

## Les trois mesures possibles, et quand chacune convient

| Mesure | Pour | Contre | Profil |
|---|---|---|---|
| **Par cabinet** (forfait) | Simple, prévisible | Injuste aux extrêmes | Petit cabinet |
| **Par dossier actif** dans le mois | Suit la valeur rendue, ne paie pas les dossiers dormants | Facture qui varie | Cabinet moyen |
| **Par utilisateur** (siège) | Habituel pour les logiciels | Pousse à partager les comptes, ce qui est contraire à la sécurité et à la traçabilité | Déconseillé seul ; possible en complément du forfait |

**Dossier actif** : un dossier où quelque chose a été enregistré dans le mois (une pièce, un tiers, une synchronisation).
Lire un dossier seul ne le rend pas actif. La mesure vient des indicateurs agrégés déjà collectés pour Grafana : un
nombre de dossiers actifs par cabinet, sans aucune donnée des dossiers.

## Les formules

| Formule | Mesure | Ce qui est compris |
|---|---|---|
| **Essentiel** | Forfait par cabinet, avec un plafond de dossiers actifs | Facturation, devis, relances, encaissements, facture électronique (réception, puis émission), FEC, réversibilité, postes reliés |
| **Activité** | Par dossier actif dans le mois, avec un minimum | Comme Essentiel, sans plafond |
| **Cabinet dédié** | Contrat annuel, selon la taille de la cellule (L ou XL) | Comme Activité, plus une cellule réservée et un engagement de service écrit |
| **Sur site** | Licence annuelle et support | Les charts et les mises à jour ; l'hébergement reste chez le cabinet |
| **Poste isolé** | Licence | Le mode 1 |

## Les options à la carte

Chaque option s'ajoute à une formule, par cabinet, et s'arrête à la fin du mois. Les options qui reposent sur un
service extérieur (banque, plateforme agréée, IA) attendent un contrat avec ce service.

| Option | Pour quel profil surtout | Mesure |
|---|---|---|
| Espace client (portail des entreprises du cabinet) | Cabinets moyens et grands | Par entreprise invitée |
| Banque connectée (agrégateur) | Tous | Par compte relié |
| Plateforme agréée réelle (émission et réception) | Tous, obligatoire avec la réforme | Par volume de factures |
| Assistant IA (désactivé par défaut) | Cabinets qui le demandent | Par cabinet ; l'IA sur le poste reste sans surcoût |
| Clés apportées par le cabinet (BYOK) | Grands cabinets | Avec la formule Cabinet dédié seulement |
| Rétention allongée des sauvegardes | Grands cabinets | Par mois de rétention en plus |
| Accompagnement (reprise de données, formation) | Tous | Au forfait |

## Ce que le produit doit savoir faire pour cela (non réalisé)

- Compter les dossiers actifs par cabinet et par mois. Les indicateurs existent, mais pas encore l'agrégat mensuel
  par cabinet.
- Activer ou couper une option pour un cabinet depuis l'écran « Plateforme », avec une politique par cabinet (comme
  pour les réglages IA).
- Émettre la facture de l'abonnement : Lettra pourrait se facturer elle-même avec Lettra (un dossier « Lettra » dans
  une cellule interne).

Voir aussi les cellules mutualisées et dédiées ([plan-mode-5.md](plan-mode-5.md), § 10).
