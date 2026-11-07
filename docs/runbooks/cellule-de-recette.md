# Mettre la cellule de recette en ligne (étape 4)

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).
> État : feu vert de Leub pour les étapes 1 à 3.

1. Appliquer `deploy/opentofu/envs/recette` chez l'hébergeur, en région française (achat et compte chez l'hébergeur).
2. DNS et certificats : `recette.lettra.fr`, `auth.recette.lettra.fr` (achat du domaine).
3. Argo CD synchronise `lettra-cell` avec `deploy/cells/recette/values.yaml`.
4. Secrets posés par l'exploitation, jamais dans Git :
   - le jeton du KMS (`lettra-kms`) ;
   - le secret du compte de service `lettra-provisioning` ;
   - le secret du client BFF.
5. Passerelle devant l'API : accepter 513 Mio de corps sur `POST /api/v1/cabinets/{cabinet}/dossiers/import`, et
   plusieurs minutes de délai sur cette route.
6. Vérifier : l'essai de la cellule (`deploy/tests/kind/essai-mode5.sh`) donne la liste des contrôles à refaire à la
   main ; l'écran « Plateforme » inscrit un cabinet fictif.
