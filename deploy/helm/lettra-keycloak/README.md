# lettra-keycloak

Keycloak de la cellule (ADR-0010, annexe F §2), déployé par `lettra-cell` (dépendance `lettra-keycloak`, activée par
défaut) et donc par l'application Argo CD « cellule » (`deploy/argocd/lettra-racine`). Chart maison, sur l'image
officielle `quay.io/keycloak/keycloak` : pas de chart tiers à suivre, mêmes règles de sécurité que les autres pods
de l'espace de noms `lettra` (Pod Security « restricted »).

## Ce que le chart pose

| Ressource | Rôle |
|---|---|
| `Deployment/keycloak` | `kc.sh start --import-realm` (jamais `start-dev`), santé sur le port de gestion 9000, cache distribué entre instances (JGroups, découverte par la base). |
| `ConfigMap/keycloak-realm` | Le realm versionné, tel quel : `files/realm-lettra.json` est un lien vers `deploy/keycloak/realm-lettra.json`. |
| `Cluster/keycloak-db` | Base PostgreSQL dédiée (CloudNativePG), hors du budget des dossiers ; sauvegarde quotidienne si `database.cnpg.backup.enabled`. |
| `Service/keycloak` | `http` (8080) pour la passerelle et l'API, `management` (9000) pour la supervision. |
| `NetworkPolicy/keycloak` | Entrées limitées à la passerelle, à l'API de la cellule, aux autres instances et à la supervision. |
| `PodDisruptionBudget/keycloak` | Dès deux instances. |

## Adresses

- `hostname` : adresse **publique** de Keycloak (TLS terminé à la passerelle de la cellule), par exemple
  `https://auth.recette.lettra.fr`. Elle fixe l'émetteur (`iss`) des jetons et les pages vues par le navigateur.
- `publicUrl` : origine publique de l'API ; donne la seule URL de retour du client `lettra-bff`.
- L'API joint Keycloak par le Service interne : `lettra-api.oidc.authority: http://keycloak:8080/realms/lettra`
  (valeur commune de `lettra-cell`). Avec `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`, la découverte lue par l'API garde
  l'émetteur public et donne des points d'accès serveur internes : l'API vérifie bien l'émetteur public des jetons.
  Ce trafic reste en HTTP dans la cellule (d'où `requireHttpsMetadata: false`), borné par la NetworkPolicy.

## Secrets

Aucun secret dans Git. Le chart lit le secret `existingSecret` (par défaut `lettra-keycloak`), créé par OpenTofu
(module `cell-bootstrap`) puis repris par External Secrets en production :

| Clé | Usage |
|---|---|
| `admin-password` | Compte d'amorçage `lettra-amorcage` du realm master, temporaire : le remplacer par des administrateurs nominatifs avec MFA, puis le supprimer. |
| `bff-client-secret` | Secret du client `lettra-bff` (`${LETTRA_BFF_CLIENT_SECRET}` du realm) ; même valeur que `Lettra__Identity__Oidc__BffClientSecret` dans le secret `lettra-api`. |

Les identifiants de la base viennent du secret `keycloak-db-app`, créé par CloudNativePG. Base externe (sur site) :
`database.cnpg.enabled: false`, `database.host` et `database.existingSecret` (clés `username` et `password`).

## Realm

Importé au premier démarrage. Un realm déjà présent en base n'est **pas** réécrit (comportement de Keycloak) : la
cellule de recette, recréée de zéro, prend toujours le realm du commit déployé ; sur une cellule qui vit, une évolution
du realm passe par l'administration (ou `kcadm.sh`) en plus de la PR sur `realm-lettra.json`, et le test
`RealmConfigurationTests` garde le fichier versionné comme référence. Les cabinets (organisations) ne sont jamais dans
le fichier : voir `deploy/keycloak/README.md`, « Ajouter un cabinet ».

## Valeurs par cellule

```yaml
# deploy/cells/<cellule>/values.yaml
lettra-keycloak:
  hostname: https://auth.<cellule>.lettra.fr
  publicUrl: https://<cellule>.lettra.fr
  database:
    cnpg:
      backup: { enabled: true, destinationPath: s3://…, endpointURL: https://… }
```

## Vérifications

- `deploy/tests/valider.sh` (job `valider`) : lint, rendu contre les schémas Kubernetes et CRD, realm du ConfigMap
  identique au realm versionné, recette pointée sur le Keycloak de la cellule, refus sans adresse ou sans secret.
- `deploy/tests/kind/essai-keycloak.sh` (job `keycloak-kind`) : cluster kind jetable, valeurs **CI seulement**
  (`deploy/tests/kind/valeurs-ci.yaml`) et secrets tirés au hasard. Découverte du realm et émetteur, `/health` et
  connexion BFF de l'API, boucle locale et PKCE du client `lettra-maui`, MFA et ajout d'un cabinet fictif.
