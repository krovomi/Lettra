# Une création de dossier en échec

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).

- **Signe** : l'écran « Plateforme » montre le dossier « en échec », avec le type de l'erreur.
- **Ce que fait déjà Lettra** : le Worker réessaie tout seul les échecs au bout de 30 secondes, et reprend les
  créations interrompues au bout de 5 minutes.
- **Quoi faire** :
  1. Lire le journal technique de l'API ou du Worker à l'heure de l'échec ; le message complet y est, pas au catalogue.
  2. Causes déjà vues : serveur de bases injoignable ; rôle de Lettra sans `CREATEDB` ou sans `CREATEROLE` ; cluster
     plein (§ 5.3).
  3. Une fois la cause réglée, bouton « Relancer les créations en échec » de l'écran « Plateforme » : il réessaie
     aussitôt.
- **Vérifier** : le dossier passe « actif » et s'ouvre.
