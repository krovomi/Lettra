# Keycloak du realm Lettra

Fournisseur d'identité des modes 2 à 5 (annexe F §2, ADR-0010). Le mode 1 (poste isolé) n'utilise pas Keycloak :
il s'appuie sur le compte local protégé par phrase de passe (`Lettra.Infrastructure.Identity.Local`).

Keycloak dit **qui** est l'utilisateur et **à quel cabinet** il appartient. Il ne dit jamais ce qu'il peut faire :
les rôles et leurs portées (cabinet, groupe, dossier) sont dans le catalogue Lettra et évalués par
`IPermissionService` à chaque cas d'usage.

## Contenu

| Fichier | Rôle |
|---|---|
| `realm-lettra.json` | Realm versionné : organisations, MFA obligatoire, jetons, clients, mappers. Vérifié par `RealmConfigurationTests`. |
| `docker-compose.yml` | Keycloak de développement qui importe le realm au démarrage. |
| `ajouter-cabinet.sh` | Crée l'organisation Keycloak d'un cabinet et affiche son identifiant (voir « Ajouter un cabinet »). |

## Démarrer en développement

```bash
export KEYCLOAK_ADMIN_PASSWORD='…'
export LETTRA_BFF_CLIENT_SECRET="$(openssl rand -hex 32)"
export LETTRA_PUBLIC_URL=https://localhost:7180
docker compose -f deploy/keycloak/docker-compose.yml up
```

Puis, côté API (secrets utilisateur ou variables d'environnement) :

```json
{
  "Lettra": {
    "Identity": {
      "Oidc": {
        "Authority": "http://localhost:8080/realms/lettra",
        "RequireHttpsMetadata": false,
        "BffClientSecret": "<même valeur que LETTRA_BFF_CLIENT_SECRET>"
      }
    }
  }
}
```

`RequireHttpsMetadata: false` n'est acceptable qu'en local. Les jetons d'accès durent 5 minutes ; le BFF les
rafraîchit sans que le navigateur ne voie jamais un jeton.

## Variables substituées à l'import

| Variable | Usage |
|---|---|
| `LETTRA_PUBLIC_URL` | Origine publique de l'API ; donne la seule URL de retour du BFF (`/bff/signin-oidc`). Aucune redirection générique (`*`). |
| `LETTRA_BFF_CLIENT_SECRET` | Secret du client confidentiel `lettra-bff`. Vient du coffre de la cellule, jamais du dépôt. |

## Clients

| Client | Type | Usage |
|---|---|---|
| `lettra-bff` | Confidentiel, code + PKCE S256 | Navigateur via le BFF de l'API : cookie `__Host-lettra`, jetons gardés côté serveur. |
| `lettra-maui` | Public, code + PKCE S256 | Application MAUI (modes 2 à 5), redirection par schéma `fr.lettra.app://` ou boucle locale `http://127.0.0.1/callback` (RFC 8252 §7.3). Le poste écoute sur un port libre tiré à chaque connexion (`LoopbackAuthorizationBrowser`) : Keycloak ignore le port d'une adresse de bouclage, l'URL enregistrée n'en porte donc pas. |
| `lettra-api` | Bearer only | Audience des jetons ; l'API la vérifie. |

Aucun client n'autorise le flux implicite ni le mot de passe direct (`directAccessGrantsEnabled: false`).
Chaque jeton porte `amr` (niveau d'authentification), `organization` (cabinet) et l'audience `lettra-api`.

## Règles portées par le realm

- **MFA obligatoire** : flux `lettra-browser-mfa`, mot de passe puis second facteur (REQUIRED) : OTP ou WebAuthn pour un
  utilisateur enrôlé, sinon formulaire OTP obligatoire, qui impose l'enrôlement TOTP (action requise). L'API refuse aussi
  un jeton sans `amr` fort (`Security.MfaRequired`), en défense en profondeur ; après un enrôlement (que Keycloak ne compte
  pas dans `amr`), le BFF redemande une fois une authentification complète (`prompt=login`).
- **amr** : chaque valeur reste valable la durée maximale d'une session SSO (`default.reference.maxAge`) ; sans cette
  durée, Keycloak ne l'émet que dans la seconde de l'authentification.
- **Jetons** : accès 5 min, session inactive 30 min, rotation du refresh token avec **une réutilisation tolérée**
  (`refreshTokenMaxReuse: 1`). C'est la grâce validée par le POC synchro hors ligne : si la réponse d'un rafraîchissement
  se perd, l'ancien jeton reste utilisable une fois, jusqu'à l'usage du nouveau. Le BFF regroupe en plus les rafraîchissements
  simultanés d'une même session.
- **Force brute** : verrouillage temporaire après 5 échecs, attente croissante jusqu'à 15 minutes.
- **Mots de passe** : 12 caractères minimum, différents de l'identifiant et de l'e-mail, historique de 5.

## Ajouter un cabinet

**Une organisation Keycloak par cabinet**, dans le realm `lettra` du Keycloak de la cellule. Les organisations ne sont
jamais dans `realm-lettra.json` (le fichier est le même pour toutes les cellules) : elles vivent dans la base de Keycloak.

1. Créer l'organisation du cabinet. Avec `ajouter-cabinet.sh` (curl et jq), qui affiche l'identifiant créé :

   ```bash
   KEYCLOAK_URL=https://auth.recette.lettra.fr KEYCLOAK_TOKEN="$(…jeton d'un administrateur du realm…)" \
     deploy/keycloak/ajouter-cabinet.sh cabinet-exemple "Cabinet Exemple" cabinet-exemple.example
   ```

   Ou depuis la console d'administration (*Organizations → Create organization*) : alias stable, nom affiché, domaine
   e-mail du cabinet. Le script est celui qu'exerce le job CI `keycloak-kind` sur un cabinet fictif.
2. Déclarer le cabinet dans le catalogue Lettra avec **cet identifiant** comme `OrganisationId` : c'est la valeur que
   porte le claim `organization` des jetons (`{"alias": {"id": "…"}}`) et que l'API lit (`OidcClaims.Cabinet`).
3. Inviter les membres depuis l'organisation (*Members → Invite member*, ou `POST /organizations/{id}/members/invite-user`) :
   pas d'inscription libre. À la première connexion, chaque membre enrôle son second facteur (TOTP imposé).
4. Un utilisateur appartient à **une seule** organisation : un jeton qui en porte plusieurs n'ouvre aucun cabinet.
5. Attribuer les rôles dans Lettra (catalogue), pas dans Keycloak.

## SSO du cabinet (Entra ID, Google Workspace…)

1. Déclarer le fournisseur du cabinet comme *identity provider* rattaché à son organisation, avec ses domaines e-mail
   (redirection automatique par `identity-provider-redirector`).
2. Régler son **post login flow** sur `lettra-post-broker-mfa` : le SSO du cabinet ne dispense pas du second facteur
   Lettra, sauf si le cabinet prouve un MFA fort côté fournisseur (décision à tracer en commission).
3. Le provisionnement automatique des comptes (SCIM) est prévu en V2.

## Production

Keycloak tourne **dans la cellule** : chart `deploy/helm/lettra-keycloak` (voir son README), dépendance de `lettra-cell`,
déployé par l'application Argo CD « cellule ». En bref :

- `kc.sh start --import-realm` (jamais `start-dev`), image officielle `quay.io/keycloak/keycloak:26.4` ; le realm importé
  est ce fichier (lien `files/realm-lettra.json` du chart, comparaison vérifiée par `deploy/tests/valider.sh`).
- Base PostgreSQL dédiée (cluster CloudNativePG `keycloak-db`), sauvegardée comme les clusters de dossiers.
- Adresse publique (`lettra-keycloak.hostname`, TLS à la passerelle) = émetteur des jetons. L'API joint Keycloak par le
  Service interne (`Lettra:Identity:Oidc:Authority` = `http://keycloak:8080/realms/lettra`), en HTTP borné à la cellule
  par une NetworkPolicy ; la découverte lue par l'API garde l'émetteur public.
- Secrets (`lettra-keycloak` : compte d'amorçage et secret du BFF) créés par OpenTofu (`cell-bootstrap`), jamais dans Git ;
  le secret du BFF est le même dans `lettra-api`.
- Les clés DataProtection de l'API doivent être partagées entre ses instances (Redis) pour que les cookies du BFF restent lisibles.

Vérifications en CI :

- `KeycloakRealmImportTests` (CI .NET) importe ce realm dans un vrai Keycloak 26 : découverte, offline_access, boucle
  locale du client MAUI sur un port quelconque, PKCE exigé.
- Job `keycloak-kind` (`.github/workflows/deploy.yml`, script `deploy/tests/kind/essai-keycloak.sh`) : la cellule dans un
  cluster kind jetable (valeurs **CI seulement**, secrets tirés au hasard), découverte et émetteur, `/health` et connexion
  BFF de l'API pointée sur le Keycloak de la cellule, MFA imposé, ajout d'un cabinet fictif.
