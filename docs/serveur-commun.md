# Serveur commun : les mêmes données sur tous les appareils

Le navigateur travaille toujours sur le serveur qui l'a servi. Les applications installées (Windows, macOS, Android,
iOS) travaillaient jusqu'ici seules, chacune avec sa propre base : un client créé sur le téléphone n'apparaissait pas
dans le navigateur. L'écran **Paramètres › Serveur du cabinet** relie maintenant l'application installée au serveur du
cabinet : elle passe alors par la même API, sur le même dossier que le navigateur, et tous les appareils reliés voient
les mêmes données.

## Comment ça marche

1. Sur l'appareil, ouvrir **Serveur du cabinet** (menu Paramètres, ou le lien en bas de l'écran de connexion).
2. Saisir l'adresse du serveur, par exemple `http://localhost:8080` pour la démo, ou `192.168.1.20:8443` (sans
   `https://`, l'application essaie https puis http).
3. L'application lit sur le serveur l'adresse `/.well-known/lettra-server` (sans connexion) : elle vérifie que c'est
   bien un serveur Lettra et y trouve l'adresse de connexion du cabinet (Keycloak) et l'identifiant de l'application.
4. Elle redémarre (Windows : automatiquement ; Android, macOS : bouton « Fermer l'application », puis la rouvrir ; iOS :
   fermer et rouvrir l'application soi-même).
5. Au lancement suivant, « Se connecter » ouvre la page de connexion du cabinet dans le navigateur du système
   (mot de passe et code à usage unique), puis l'application ouvre le dossier du navigateur.

Règles de sécurité appliquées :

- https obligatoire, sauf pour `localhost` et `127.0.0.1` (démo sur le même poste ou par câble USB) ;
- changer de serveur efface les jetons de connexion : ils ne partent jamais vers un autre serveur ;
- un certificat non reconnu est refusé avec un message clair (installer l'autorité de certification du cabinet).

## Ce qu'il faut savoir

- **L'appareil relié a besoin du réseau.** Dans ce mode il n'y a pas de base sur l'appareil : sans réseau, les écrans
  affichent « serveur injoignable ». Rien n'est perdu, il suffit de retrouver le réseau.
- **Les données de l'appareil seul ne bougent pas.** Elles restent sur l'appareil, intactes, et reviennent avec
  « Revenir à cet appareil seul ». Elles ne sont pas copiées sur le serveur.
- **Reste à faire pour le hors-ligne** : soit étendre la synchronisation existante (mode réplique) aux devis, factures
  et règlements, soit reprendre pour l'application installée les brouillons hors ligne du navigateur (ADR-0008).

## Essayer sur chaque appareil

La démo Docker (`deploy/demo`) sert le navigateur sur `http://localhost:8080` et Keycloak sur `http://localhost:8081`.

| Appareil | Adresse à saisir | État |
| --- | --- | --- |
| Navigateur (tout système, Linux compris) | rien : il est déjà sur le serveur | **vérifié** |
| Application installée, même logique sur le même poste (harnais `deploy/demo/e2e/serveur-commun`) | `http://localhost:8080` | **vérifié** de bout en bout : connexion PKCE + mot de passe + code, client créé par l'appareil vu dans le navigateur et inversement |
| Windows, application installée | `http://localhost:8080` | compilé par la CI, **non vérifié** sur un poste |
| Android, émulateur ou téléphone par câble USB | après `adb reverse tcp:8080 tcp:8080` et `adb reverse tcp:8081 tcp:8081` : `http://localhost:8080` | compilé par la CI, **non vérifié** sur un appareil |
| Android par Wi-Fi | l'adresse https du poste (profil réseau local de la démo, autorité de certification installée sur le téléphone) | **non vérifié** |
| iOS, simulateur sur Mac | `http://localhost:8080` | compilé par la CI, **non vérifié** |
| iPhone réel | — | impossible pour l'instant : signature Apple nécessaire |
| macOS (Mac Catalyst) | `http://localhost:8080` | **non vérifié** : l'application non signée ne dépasse pas le premier dossier |

Vérification automatique (après `node deploy/demo/e2e/parcours.mjs premier`) :

```bash
dotnet run --project deploy/demo/e2e/serveur-commun -- "$(cat sortie/client.txt)"
node deploy/demo/e2e/appareil.mjs voir "$(cat sortie/client-appareil.txt)"
```

## Mise à jour de la démo

Cette version corrige le realm Keycloak : l'application installée n'obtenait pas les droits de connexion (portées
par défaut absentes du client `lettra-maui`, rôles par défaut absents de l'utilisateur fictif). Keycloak n'important
un realm qu'une fois, la démo garde maintenant une base Keycloak par version du realm : après la mise à jour,
**l'enrôlement du code à usage unique de l'utilisateur fictif est à refaire une fois**. Les données Lettra (clients,
devis, factures) sont conservées.

## Code

- API : `GET /.well-known/lettra-server` (`src/Hosts/Lettra.Api/Endpoints/ServerDiscoveryEndpoints.cs`), anonyme ;
  `Lettra:Identity:Oidc:PublicAuthority` et `Lettra:Identity:Oidc:DeviceClientId` permettent de forcer les valeurs.
- Gateways : `src/Presentation/Lettra.UI.Gateway.Http/Device/SharedServerProbe.cs` (vérification de l'adresse),
  `SharedServerSession.cs` (connexion puis choix du dossier), `SharedServerDependencyInjection.cs`.
- Écran : `src/Presentation/Lettra.UI.Shared/Pages/SharedServer.razor`, contrat `IServerConnection`
  (`Lettra.UI.Abstractions/ServerConnection.cs`).
- Application installée : `src/Hosts/Lettra.App.Maui/SharedServer.cs` (fichier `serveur-commun.json` dans le dossier
  de l'application, composition, redémarrage, connexion `fr.lettra.app://callback` sous Android et iOS).
- Tests : `tests/Lettra.Api.Tests/SharedServerTests.cs`, `tests/Lettra.UI.Tests/SharedServerPageTests.cs`,
  `tests/Lettra.Identity.Tests/RealmConfigurationTests.cs`.
