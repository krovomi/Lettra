# Le KMS injoignable

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).

- **Signe** : les dossiers ne s'ouvrent plus après quelques minutes (les clés gardées en mémoire expirent au bout de
  5 minutes, `Lettra:Kms:Transit:CacheLifetime`) ; erreurs de connexion vers `openbao:8200` dans les journaux.
- **Quoi faire** :
  1. Vérifier le pod OpenBao et son jeton (`lettra-kms`).
  2. Ne **jamais** recréer une clé de cabinet : une clé recréée ne déchiffre pas ce que l'ancienne a chiffré. Restaurer
     le KMS depuis sa sauvegarde.
- **Manque** : aucune alerte dédiée au KMS. À ajouter (règle sur les échecs d'appel au KMS), et un KMS en haute
  disponibilité pour la production.
- **À éprouver** : la restauration du KMS. Le KMS d'essai garde ses clés en mémoire, et un redémarrage les perd.
