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
| `lettra-maui` | Public, code + PKCE S256 | Application MAUI (modes 2 à 5), redirection par schéma `fr.lettra.app://` ou boucle locale. Branché côté application en phase suivante. |
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

1. Créer une **organisation** Keycloak par cabinet ; son identifiant est l'`OrganisationId` du cabinet dans le catalogue.
2. Inviter les membres depuis l'organisation (pas d'inscription libre).
3. Un utilisateur appartient à **une seule** organisation : un jeton qui en porte plusieurs n'ouvre aucun cabinet.
4. Attribuer les rôles dans Lettra (catalogue), pas dans Keycloak.

## SSO du cabinet (Entra ID, Google Workspace…)

1. Déclarer le fournisseur du cabinet comme *identity provider* rattaché à son organisation, avec ses domaines e-mail
   (redirection automatique par `identity-provider-redirector`).
2. Régler son **post login flow** sur `lettra-post-broker-mfa` : le SSO du cabinet ne dispense pas du second facteur
   Lettra, sauf si le cabinet prouve un MFA fort côté fournisseur (décision à tracer en commission).
3. Le provisionnement automatique des comptes (SCIM) est prévu en V2.

## Production

Le déploiement passe par le Helm de la cellule (chantier IaC et observabilité). Points d'attention :

- Base PostgreSQL dédiée à Keycloak, `start` (jamais `start-dev`), TLS de bout en bout.
- Les clés DataProtection de l'API doivent être partagées entre ses instances (Redis) pour que les cookies du BFF restent lisibles.
- Le test `KeycloakRealmImportTests` importe ce realm dans un vrai Keycloak 26 à chaque CI.
