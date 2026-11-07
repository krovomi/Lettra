# Restaurer un dossier, puis une cellule (étape 5)

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).
> État : à éprouver.

- **Un dossier** : restaurer sa base depuis la sauvegarde du cluster (CloudNativePG, restauration à un instant donné,
  dans un cluster temporaire). Puis copier la base. La DEK du dossier doit être encore au catalogue, sans quoi la base
  restaurée reste illisible : c'est voulu après un effacement.
- **Une cellule** : restaurer le cluster du catalogue, puis les clusters des dossiers, puis le KMS. Les clés d'abord :
  sans elles, rien ne se relit.
- **Objectifs à écrire** : perte de données maximale (proposé : 15 minutes, grâce à l'archivage continu des journaux de
  PostgreSQL) et durée d'interruption maximale (proposé : 4 heures). **À faire valider par Leub.**
- **Sauvegardes** : désactivées par défaut dans les valeurs de la cellule (`postgres.backup.enabled`). À activer pour
  la recette, avec un stockage objet sous verrou (**achat : feu vert de Leub**).
