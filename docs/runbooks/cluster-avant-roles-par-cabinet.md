# Un cluster existant avant les rôles par cabinet

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).

Un cluster PostgreSQL créé avant les rôles par cabinet et le rôle d'administration du Worker (`lettra_admin`) n'a
pas reçu ce rôle, et son rôle applicatif (`lettra`) garde des droits qu'il n'a plus à avoir.

1. Créer le secret `<cluster>-admin` (OpenTofu le fait, module `cell-bootstrap`, variable `database_clusters`).
2. Laisser l'opérateur CloudNativePG créer `lettra_admin` (avec `CREATEDB`, `CREATEROLE` et les connexions
   réservées), puis déployer la cellule avec `Lettra:Catalog:DatabaseAdministration: Worker` et `CabinetRoles`.
3. La passe du Worker fait passer les bases actives au rôle de leur cabinet, tables comprises.
4. En superutilisateur, retirer au rôle applicatif ce qu'il n'a plus à avoir :

   ```sql
   ALTER ROLE lettra NOCREATEDB NOCREATEROLE;
   -- pour chaque rôle de cabinet c_… :
   REVOKE c_<identifiant du cabinet> FROM lettra;
   ```

**Vérifier** : la validation du déploiement passe (l'API n'a pas la connexion d'administration, le Worker l'a) ; une
création de dossier depuis « Dossiers du cabinet » aboutit ; `lettra` ne peut plus se connecter à une base de dossier.
