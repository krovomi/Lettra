# Lettra dans l'assistant IA du poste (MCP)

Un assistant IA installé sur le poste de l'utilisateur (Claude Desktop, VS Code, ou tout client compatible avec le
protocole MCP) peut interroger Lettra en lecture seule, avec les droits de l'utilisateur connecté. Ce n'est pas
l'assistant de Lettra : l'IA est celle du poste, Lettra ne lui offre que des outils.

## Ce que l'assistant peut faire

Adresse : `POST /api/v1/assistant/mcp` (protocole MCP, transport HTTP, réponses JSON ; `DELETE` ferme la session).

Outils proposés, tous en lecture seule :

- `meteo_des_paiements` ;
- `factures_en_retard` ;
- `chiffre_d_affaires_du_mois` ;
- `dossier_a_un_instant` ;
- `simuler_et_si` ;
- `fiche_d_appel` ;
- les outils communs de l'assistant : calculatrice et références réglementaires.

Chaque outil appelle le même cas d'usage que l'écran correspondant, donc avec les mêmes droits et le même calcul C#.

## Garde-fous

- **Rien ne s'écrit** : seuls des outils en lecture seule sont proposés.
- **Aucun nom ne sort en clair** : les clients, fournisseurs et personnes deviennent des jetons de pseudonymisation
  comme `[CLIENT_1]`. Un jeton renvoyé par l'assistant est rétabli côté serveur. La table de correspondance reste en
  mémoire et est oubliée à la fermeture de la session ou après 30 minutes sans appel.
- **Tout est tracé** : chaque connexion, appel et refus va au journal IA du dossier (`GET /api/v1/assistant/journal`),
  lui aussi pseudonymisé.
- **Fermé par défaut** : le cabinet ouvre le point « Assistant IA du poste (MCP) » dans ses réglages IA. Ouvert seul,
  il ne demande aucun moteur à Lettra. Les autres points ne l'ouvrent pas. Un dossier exclu de l'IA l'est aussi ici.
- **Accord du DPO** : envoyer des données du cabinet, même pseudonymisées, à une IA extérieure demande l'accord du DPO
  avant d'ouvrir ce point.
- **Droits requis** : « Utiliser l'assistant IA », plus « Lire le dossier » pour les outils du dossier.

## Connexion d'un client du poste

La connexion suit la spécification d'autorisation de MCP (OAuth 2.1) :

1. Le client appelle `POST /api/v1/assistant/mcp` sans jeton. L'API répond 401 avec
   `WWW-Authenticate: Bearer resource_metadata="https://<serveur>/.well-known/oauth-protected-resource/api/v1/assistant/mcp"`.
2. Le client lit ce document, publié sans connexion (RFC 9728). Il désigne le realm Keycloak du cabinet, à l'adresse
   que voit le poste (la même que la découverte du serveur commun, voir [Serveur commun](serveur-commun.md)).
3. Le client ouvre la page de connexion de Keycloak avec le client public `lettra-mcp` : code d'autorisation, PKCE
   S256, retour par la boucle locale `http://127.0.0.1:<port>/<chemin>`. L'utilisateur saisit son mot de passe et son
   second facteur, comme dans le navigateur.
4. Le client appelle le point MCP avec le jeton porteur et l'en-tête `X-Dossier` (identifiant du dossier, donné par
   `GET /api/v1/dossiers`). Avec un jeton porteur, l'en-tête anti-CSRF n'est pas demandé.

Le client `lettra-mcp` n'accepte de retour que vers `127.0.0.1`, sur n'importe quel port et n'importe quel chemin :
chaque client MCP choisit le sien. Ni `localhost` (résolution détournable), ni site web, ni jeton hors ligne.

Une page web d'un autre hôte qui tente d'appeler le point MCP depuis le navigateur reçoit 403 : l'API compare l'en-tête
`Origin` à son propre hôte. Un client du poste n'envoie pas cet en-tête.

## Ouvrir le point (administrateur du cabinet)

Il n'y a pas encore d'écran de réglages IA. Après l'accord du DPO, un administrateur du cabinet ouvre le point par
l'API :

```http
PUT /api/v1/cabinets/{cabinet}/assistant/reglages
{"enabled":true,"engine":"None","features":["ExternalAssistant"]}
```

`engine: "None"` suffit : l'IA est celle du poste. Pour le refermer, le même appel porte `"enabled":false` et la
`version` lue par `GET` sur la même adresse (sinon 409, le réglage a changé entre-temps).

## Exemple de configuration (VS Code, non vérifié)

```json
{
  "servers": {
    "lettra": {
      "type": "http",
      "url": "http://localhost:8080/api/v1/assistant/mcp",
      "headers": { "X-Dossier": "<identifiant du dossier>" }
    }
  }
}
```

Le client doit savoir utiliser un client OAuth déjà enregistré (`lettra-mcp`) : Keycloak n'ouvre pas l'enregistrement
dynamique des clients.

## Ce qui a été vérifié

| Où | Vérifié |
|---|---|
| Démo Docker, client MCP officiel (SDK TypeScript) : 401, métadonnées, connexion Keycloak `lettra-mcp` (mot de passe et code TOTP), retour sur `127.0.0.1`, outils listés et appelés, noms pseudonymisés | Oui |
| Démo Docker, point fermé : jeton accepté, appel refusé (403 `Ai.Disabled`) | Oui |
| Démo Docker, page d'un autre hôte (`Origin` étranger) : 403 | Oui |
| Claude Desktop ou VS Code branchés sur la démo | Non vérifié |
