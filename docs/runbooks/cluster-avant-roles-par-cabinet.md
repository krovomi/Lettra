# Un cluster existant avant les rôles par cabinet

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).

En superutilisateur, avant d'activer `CabinetRoles` :

```sql
ALTER ROLE lettra CREATEROLE;
GRANT pg_use_reserved_connections TO lettra;
```

Ensuite, la passe du Worker fait passer les bases actives au rôle de leur cabinet.
