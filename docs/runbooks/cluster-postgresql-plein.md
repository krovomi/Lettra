# Un cluster PostgreSQL plein

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).

- **Signe** : alertes `DossiersActifsAuDelaDuBudget`, `BudgetConnexionsPresqueAtteint` ou `DisqueBasesPresquePlein` ;
  créations de dossier refusées, faute de serveur sous sa limite de bases (le journal dit « Aucun serveur de bases
  disponible dans la cellule »).
- **Quoi faire** :
  1. Fermer le cluster aux nouveaux dossiers : `Lettra:Clusters:<cluster>:AcceptsNewDossiers: "false"`. Les dossiers
     déjà là continuent de tourner.
  2. Ajouter un cluster : une entrée de plus dans `postgres.clusters` des valeurs de la cellule (son budget de dossiers
     actifs, au plus 600), puis ses réglages `Lettra:Clusters:<nouveau>:…` (chaînes de connexion, `CabinetRoles`).
  3. Les nouveaux dossiers vont au cluster le moins rempli de la cellule.
- **À éprouver** : déplacer un dossier d'un cluster à l'autre (export puis import du paquet, en attendant une copie
  directe).
